$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$runner = Join-Path $PSScriptRoot '../evaluate-prompts.ps1'
if (-not (Test-Path -LiteralPath $runner)) { throw 'Evaluation runner is missing.' }
$corpusPath = Join-Path $PSScriptRoot '../../docs/evaluation/prompt-interpretation-v1.json'
$corpus = Get-Content -LiteralPath $corpusPath -Raw | ConvertFrom-Json -AsHashtable
$temp = Join-Path ([IO.Path]::GetTempPath()) ('cycling-eval-' + [guid]::NewGuid())
[IO.Directory]::CreateDirectory($temp) | Out-Null
function Assert-True($condition, $message) { if (-not $condition) { throw $message } }
try {
    & pwsh -NoProfile -File $runner
    Assert-True ($LASTEXITCODE -eq 0) 'Offline corpus validation failed.'
    foreach ($mutation in @('path', 'status', 'locale', 'duplicate')) {
        $invalid = Get-Content -LiteralPath $corpusPath -Raw | ConvertFrom-Json -AsHashtable
        switch ($mutation) {
            'path' { $invalid.cases[0].expectedFields['intent.execute'] = 'anything' }
            'status' { $invalid.cases[0].expectedStatus = 'success' }
            'locale' { $invalid.cases[0].request.locale = 'xx' }
            'duplicate' { $invalid.cases[1].id = $invalid.cases[0].id }
        }
        $invalidPath = Join-Path $temp ($mutation + '.json')
        $invalid | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $invalidPath -Encoding utf8
        & pwsh -NoProfile -File $runner -CorpusPath $invalidPath 2>$null
        Assert-True ($LASTEXITCODE -ne 0) "Invalid corpus accepted: $mutation"
    }

    foreach ($delay in @(-1, 60001)) {
        & pwsh -NoProfile -File $runner -RequestDelayMs $delay 2>$null
        Assert-True ($LASTEXITCODE -ne 0) 'Invalid request delay accepted.'
    }

    foreach ($scenario in @('paced', 'pass', 'mismatch', 'quota', 'auth', 'array-status')) {
        $responses = @($corpus.cases | ForEach-Object {
            $case = $_
            $body = @{ status = $case.expectedStatus; draft = @{}; intent = @{};
                clarifications = $case.expectedClarifications; limitations = $case.expectedLimitations; assumptions = $case.expectedAssumptions }
            foreach ($path in $case.expectedFields.Keys) {
                $parts = $path.Split('.')
                $node = $body
                for ($i = 0; $i -lt $parts.Length - 1; $i++) {
                    if (-not $node.ContainsKey($parts[$i]) -or $null -eq $node[$parts[$i]]) { $node[$parts[$i]] = @{} }
                    $node = $node[$parts[$i]]
                }
                $node[$parts[-1]] = $case.expectedFields[$path]
            }
            if ($scenario -eq 'mismatch') { $body.status = 'wrong' }
            if ($scenario -eq 'array-status') { $body.status = @($case.expectedStatus, 'invalid-extra-status') }
            $status = 200
            if ($scenario -in @('quota', 'auth')) {
                $status = 503
                $body = @{ code = $(if ($scenario -eq 'quota') { 'ai_rate_limited' } else { 'ai_credentials_rejected' }) }
            }
            @{ status = $status; body = ($body | ConvertTo-Json -Depth 30 -Compress) }
        })
        $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
        $listener.Start()
        $port = $listener.LocalEndpoint.Port
        $expectedCalls = if ($scenario -in @('quota', 'auth')) { 1 } else { $responses.Count }
        $job = Start-ThreadJob -ArgumentList $listener, $responses, $expectedCalls -ScriptBlock {
            param($listener, $responses, $expectedCalls)
            $gaps = [Collections.Generic.List[double]]::new()
            $sinceResponse = [Diagnostics.Stopwatch]::new()
            for ($index = 0; $index -lt $expectedCalls; $index++) {
                $connection = $listener.AcceptTcpClient()
                if ($index -gt 0) { $gaps.Add($sinceResponse.Elapsed.TotalMilliseconds) }
                try {
                    $stream = $connection.GetStream()
                    $stream.ReadTimeout = 10000
                    $header = ''
                    while (-not $header.EndsWith("`r`n`r`n")) {
                        $byte = $stream.ReadByte()
                        if ($byte -lt 0) { throw 'Unexpected EOF.' }
                        $header += [char]$byte
                        if ($header.Length -gt 16384) { throw 'Header too large.' }
                    }
                    $length = [int]([regex]::Match($header, '(?im)^Content-Length: (\d+)').Groups[1].Value)
                    for ($i = 0; $i -lt $length; $i++) { if ($stream.ReadByte() -lt 0) { throw 'Missing body.' } }
                    $payload = [Text.Encoding]::UTF8.GetBytes($responses[$index].body)
                    $status = $responses[$index].status
                    $head = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 $status Test`r`nContent-Type: application/json`r`nContent-Length: $($payload.Length)`r`nConnection: close`r`n`r`n")
                    $stream.Write($head)
                    $stream.Write($payload)
                    $stream.Flush()
                    $sinceResponse.Restart()
                } finally { $connection.Dispose() }
            }
            @{ calls = $expectedCalls; gaps = $gaps.ToArray() }
        }
        try {
            $reportPath = Join-Path $temp ($scenario + '.json')
            $delay = if ($scenario -eq 'paced') { 100 } else { 0 }
            & pwsh -NoProfile -File $runner -RunLive -BaseUrl "http://127.0.0.1:$port" -ModelId test-model -OutputPath $reportPath -RequestDelayMs $delay
            $exit = $LASTEXITCODE
            Assert-True ($(if ($scenario -in @('pass', 'paced')) { $exit -eq 0 } else { $exit -ne 0 })) "Wrong exit for $scenario"
            $finished = Wait-Job $job -Timeout 15
            Assert-True ($null -ne $finished -and $job.State -eq 'Completed') "Stub did not finish: $scenario"
            $observed = Receive-Job $job -ErrorAction Stop
            Assert-True ($observed.calls -eq $expectedCalls) "Wrong call count: $scenario"
            if ($scenario -eq 'paced') {
                Assert-True ($observed.gaps.Count -eq $corpus.cases.Count - 1) 'Missing request gaps.'
                Assert-True (@($observed.gaps | Where-Object { $_ -lt 90 }).Count -eq 0) 'Requests were sent without the configured pause.'
            }
            $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json -AsHashtable
            Assert-True ($report.requestDelayMs -eq $delay) 'Report omitted configured request delay.'
            Assert-True ($report.results.Count -eq $corpus.cases.Count) 'Report omitted cases.'
            $unrun = @($report.results | Where-Object status -eq 'unrun').Count
            Assert-True ($unrun -eq $corpus.cases.Count - $expectedCalls) 'Unrun cases were counted incorrectly.'
        } finally {
            $listener.Stop()
            Stop-Job $job -ErrorAction SilentlyContinue
            Remove-Job $job -Force
        }
    }
    Write-Output 'Evaluation runner tests passed (offline validation, malformed corpus, paced requests, delay bounds, pass, mismatch, quota, auth, array-status).'
} finally {
    Get-ChildItem -LiteralPath $temp -File | Remove-Item -Force
    Remove-Item -LiteralPath $temp
}
exit 0
