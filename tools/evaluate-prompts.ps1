[CmdletBinding()]
param(
    [string]$BaseUrl,
    [string]$ModelId,
    [switch]$RunLive,
    [string]$OutputPath,
    [string]$CorpusPath = (Join-Path $PSScriptRoot '../docs/evaluation/prompt-interpretation-v1.json')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Assert-Corpus($corpus) {
    if ($corpus.version -ne 1 -or $corpus.contractVersion -ne 'prompt-interpretation-v1') { throw 'Unsupported corpus version.' }
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($case in $corpus.cases) {
        if ([string]::IsNullOrWhiteSpace($case.id) -or -not $ids.Add($case.id)) { throw 'Invalid or duplicate case ID.' }
        if ($case.core -isnot [bool]) { throw 'core must be boolean.' }
        if ($case.request.locale -cnotin @('en', 'ru', 'he')) { throw 'Invalid locale.' }
        if ($case.request.prompt -isnot [string] -or [string]::IsNullOrWhiteSpace($case.request.prompt) -or $case.request.prompt.Length -gt 4000) { throw 'Invalid prompt.' }
        if ($case.expectedStatus -cnotin @('ready', 'needsClarification', 'unsupported')) { throw 'Invalid expected status.' }
        if ($case.expectedFields -isnot [Collections.IDictionary] -or $case.expectedFields.Count -eq 0) { throw 'Expected fields are required.' }
        foreach ($path in $case.expectedFields.Keys) {
            if ($path -cnotmatch '^(intent|draft)(\.(shape|profile|elevation|targetDistanceMeters|targetDurationSeconds|start\.(latitude|longitude)|destination\.(latitude|longitude)))?$') { throw 'Invalid expected field path.' }
        }
        foreach ($name in @('expectedClarifications', 'expectedLimitations', 'expectedAssumptions')) {
            if ($case[$name] -isnot [array]) { throw "Expected array missing: $name" }
        }
        foreach ($question in $case.expectedClarifications) {
            if ($question.field -cnotin @('start', 'destination', 'shape', 'profile', 'elevation', 'targetDistanceMeters', 'targetDurationSeconds', 'prompt') -or
                $question.code -cnotin @('required', 'target_required', 'must_be_positive', 'out_of_range', 'destination_not_allowed', 'must_differ_from_start', 'ambiguous', 'invalid_value', 'location_requires_map_selection')) { throw 'Invalid expected clarification.' }
        }
        foreach ($code in $case.expectedLimitations) {
            if ($code -cnotin @('unsupported_preference', 'gravel_not_supported', 'point_to_point_elevation_not_supported', 'loop_search_distance_out_of_range')) { throw 'Invalid expected limitation.' }
        }
        foreach ($code in $case.expectedAssumptions) { if ($code -cne 'elevation_balanced') { throw 'Invalid expected assumption.' } }
    }
    foreach ($locale in @('en', 'ru', 'he')) {
        if (@($corpus.cases | Where-Object { $_.core -and $_.request.locale -ceq $locale }).Count -ne 6) { throw 'Six core cases per locale are required.' }
    }
}

function Read-Field($body, [string]$path) {
    $node = $body
    foreach ($part in $path.Split('.')) {
        if ($node -isnot [Collections.IDictionary] -or -not $node.Contains($part)) { return @{ found = $false; value = $null } }
        $node = $node[$part]
    }
    return @{ found = $true; value = $node }
}

function Same-Value($actual, $expected) {
    if ($null -eq $expected) { return $null -eq $actual }
    if ($expected -is [string]) { return $actual -is [string] -and $actual -ceq $expected }
    if ($expected -is [bool]) { return $actual -is [bool] -and $actual -eq $expected }
    if ($expected -is [ValueType]) {
        return $null -ne $actual -and $actual -is [ValueType] -and $actual -isnot [bool] -and [Math]::Abs([double]$actual - [double]$expected) -le 0.000001
    }
    return $false
}

try {
    $corpus = Get-Content -LiteralPath $CorpusPath -Raw | ConvertFrom-Json -AsHashtable
    Assert-Corpus $corpus
    if (-not $RunLive) { Write-Output "Corpus valid: $($corpus.cases.Count) cases. No network calls made."; exit 0 }
    $uri = $null
    $ip = $null
    if (-not [Uri]::TryCreate($BaseUrl, [UriKind]::Absolute, [ref]$uri) -or
        $uri.Scheme -notin @('http', 'https') -or $uri.UserInfo -or $uri.Query -or $uri.Fragment -or $uri.AbsolutePath -ne '/' -or
        -not [Net.IPAddress]::TryParse($uri.DnsSafeHost, [ref]$ip) -or -not [Net.IPAddress]::IsLoopback($ip)) { throw 'Use an explicit loopback API URL, such as http://127.0.0.1:5080.' }
    if ($ModelId -cnotmatch '^[A-Za-z0-9._-]{1,128}$') { throw 'Specify the model ID configured on this API.' }
    if (-not $OutputPath) { $OutputPath = Join-Path $PSScriptRoot ("../artifacts/prompt-evaluation-{0}-{1}.json" -f (Get-Date -Format yyyyMMddTHHmmss), [guid]::NewGuid().ToString('N').Substring(0, 8)) }
    $OutputPath = [IO.Path]::GetFullPath($OutputPath)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputPath)) | Out-Null
    $results = [Collections.Generic.List[object]]::new()
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(45)
    $client.MaxResponseContentBufferSize = 1048576
    $stop = $false
    try {
        foreach ($case in $corpus.cases) {
            if ($stop) { $results.Add(@{ id = $case.id; status = 'unrun' }); continue }
            $timer = [Diagnostics.Stopwatch]::StartNew()
            $entry = @{ id = $case.id; status = 'error'; expectedStatus = $case.expectedStatus; comparisons = @() }
            $content = [Net.Http.StringContent]::new(($case.request | ConvertTo-Json -Depth 10 -Compress), [Text.Encoding]::UTF8, 'application/json')
            $response = $null
            try {
                $response = $client.PostAsync([Uri]::new($uri, '/api/route-intents/interpret'), $content).GetAwaiter().GetResult()
                $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json -AsHashtable
                if (-not $response.IsSuccessStatusCode) {
                    $entry.error = 'http_' + [int]$response.StatusCode
                    if ($body -is [Collections.IDictionary] -and $body.Contains('code') -and $body.code -cin @('ai_not_configured', 'ai_credentials_rejected', 'ai_rate_limited', 'ai_unavailable', 'ai_timeout', 'ai_request_rejected', 'ai_invalid_response')) { $entry.error = $body.code }
                    if ($entry.error -in @('ai_not_configured', 'ai_credentials_rejected', 'ai_rate_limited')) { $stop = $true }
                } else {
                    $match = $body.status -ceq $case.expectedStatus
                    $entry.actualStatus = $body.status
                    foreach ($path in $case.expectedFields.Keys) {
                        $field = Read-Field $body $path
                        $equal = $field.found -and (Same-Value $field.value $case.expectedFields[$path])
                        $entry.comparisons += @{ path = $path; expected = $case.expectedFields[$path]; actual = $field.value; found = $field.found; matched = $equal }
                        $match = $match -and $equal
                    }
                    foreach ($pair in @(@('clarifications', 'expectedClarifications'), @('limitations', 'expectedLimitations'), @('assumptions', 'expectedAssumptions'))) {
                        $actual = @($body[$pair[0]])
                        $expected = @($case[$pair[1]])
                        if ($pair[0] -eq 'clarifications') {
                            $actual = @($actual | ForEach-Object { $_.field + ':' + $_.code })
                            $expected = @($expected | ForEach-Object { $_.field + ':' + $_.code })
                        }
                        $equal = ($body[$pair[0]] -is [array]) -and (($actual | Sort-Object | ConvertTo-Json -Compress) -ceq ($expected | Sort-Object | ConvertTo-Json -Compress))
                        $entry.comparisons += @{ path = $pair[0]; expected = $expected; actual = $actual; matched = $equal }
                        $match = $match -and $equal
                    }
                    $entry.status = if ($match) { 'passed' } else { 'mismatch' }
                }
            } catch {
                $entry.error = 'request_or_response_failed'
                $stop = $true
            } finally {
                if ($null -ne $response) { $response.Dispose() }
                $content.Dispose()
                $timer.Stop()
                $entry.latencyMs = $timer.ElapsedMilliseconds
            }
            $results.Add($entry)
        }
    } finally { $client.Dispose() }
    $report = @{ corpusVersion = $corpus.version; contractVersion = $corpus.contractVersion; declaredModelId = $ModelId;
        modelIdSource = 'operator-provided API configuration, not provider-verified'; createdAtUtc = [DateTime]::UtcNow.ToString('O'); results = $results.ToArray() }
    $report | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath $OutputPath -Encoding utf8
    $passed = @($results | Where-Object status -eq 'passed').Count
    Write-Output "Passed $passed/$($results.Count); report: $OutputPath"
    if ($passed -ne $corpus.cases.Count) { exit 1 }
    exit 0
} catch {
    Write-Error $_.Exception.Message
    exit 1
}
