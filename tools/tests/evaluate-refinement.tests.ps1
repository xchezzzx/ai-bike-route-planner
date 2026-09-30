param([string[]]$Scenarios = @('arrayPolicy', 'excludedArrayPolicy', 'missingQuality', 'badQualityTotal', 'badQualityState', 'unjustifiedExclusion', 'falseTargetMatch', 'noMatch', 'emptyPartial', 'badExclusion', 'duplicateSeed', 'degenerate', 'partial', 'pass', 'duplicate', 'quota', 'invalid', 'disconnect'))
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$runner = Join-Path $PSScriptRoot '../evaluate-refinement.ps1'
function Assert-True($value, $message) { if (-not $value) { throw $message } }
& pwsh -NoProfile -File $runner
Assert-True ($LASTEXITCODE -eq 0) 'Offline refinement validation failed.'
$oldCorpusPath = Join-Path $PSScriptRoot '../../docs/evaluation/route-refinement-v1.json'
& pwsh -NoProfile -File $runner -CorpusPath $oldCorpusPath
Assert-True ($LASTEXITCODE -eq 1) 'Legacy advisor corpus must not qualify the v3 contract.'
$oldCorpus = Get-Content $oldCorpusPath -Raw | ConvertFrom-Json
$newCorpus = Get-Content (Join-Path $PSScriptRoot '../../docs/evaluation/route-refinement-v2.json') -Raw | ConvertFrom-Json
foreach ($field in @('advisorCases', 'routeCases')) {
    Assert-True (($oldCorpus.$field | ConvertTo-Json -Depth 20 -Compress) -ceq ($newCorpus.$field | ConvertTo-Json -Depth 20 -Compress)) 'Qualification scenarios changed during the contract correction.'
}
$root = Join-Path ([IO.Path]::GetTempPath()) ('refinement-test-' + [guid]::NewGuid())
[IO.Directory]::CreateDirectory($root) | Out-Null
try {
    foreach ($scenario in $Scenarios) {
        $requestCount = if ($scenario -in @('quota', 'disconnect')) { 1 } else { 2 }
        $geometry = @(@{latitude=32;longitude=34}, @{latitude=32.01;longitude=34.01}, @{latitude=32.02;longitude=34.01}, @{latitude=32;longitude=34})
        if ($scenario -eq 'degenerate') { $geometry = @($geometry[0], $geometry[0], $geometry[0], $geometry[0]) }
        $quality = @{
            policyVersion='road-v1'; geometryLengthMeters=10000; surfaceEvidenceState='unavailable'; waytypeSupplied=$false
            surface=@{pavedMeters=0;nonRoadMeters=0;otherKnownMeters=0;unknownMeters=10000}
            ways=@{unknownMeters=10000;stateRoadMeters=0;roadMeters=0;streetMeters=0;pathMeters=0;trackMeters=0;cyclewayMeters=0;footwayMeters=0;stepsMeters=0;ferryMeters=0;constructionMeters=0}
            repeatedMeters=0;sharedStemMeters=0;remainingRepeatedMeters=0
        }
        $assessment = @{targetsMatched=$true;score=0;distanceDeltaMeters=0;durationDeltaSeconds=$null;quality=$quality}
        $candidate = @{ seed=1; assessment=$assessment; route=@{geometry=$geometry;distanceMeters=20000;estimatedDurationSeconds=3600;ascentMeters=100;gpx='<gpx/>'} }
        $search = @{requestedLengthMeters=20000; attemptedCount=2; assumptions=@(); warnings=@(); candidates=@($candidate); excludedCandidates=@()}
        $excludedAssessment=$assessment.Clone(); $excludedAssessment.targetsMatched=$false; $excludedAssessment.distanceDeltaMeters=10000
        $excluded = @{seed=2; distanceMeters=30000; estimatedDurationSeconds=3600; assessment=$excludedAssessment; reasons=@('targets_not_met')}
        if ($scenario -in @('noMatch', 'emptyPartial', 'badExclusion', 'unjustifiedExclusion', 'excludedArrayPolicy')) {
            $search.candidates=@(); $search.excludedCandidates=@($excluded)
            $search.warnings=@('no_candidate_meets_requirements','candidates_excluded')
        }
        if ($scenario -eq 'emptyPartial') { $search.warnings += @('candidate_generation_incomplete','routing_timeout') }
        if ($scenario -eq 'badExclusion') { $excluded.reasons=@('secret') }
        if ($scenario -eq 'missingQuality') { [void]$assessment.Remove('quality') }
        if ($scenario -in @('arrayPolicy','excludedArrayPolicy')) { $quality.policyVersion=@('road-v1') }
        if ($scenario -eq 'badQualityTotal') { $quality.surface.unknownMeters=1 }
        if ($scenario -eq 'badQualityState') { $quality.surfaceEvidenceState=@('complete') }
        if ($scenario -eq 'unjustifiedExclusion') { $excluded.reasons=@('road_surface_limit_exceeded') }
        if ($scenario -eq 'falseTargetMatch') { $candidate.route.distanceMeters=30000 }
        if ($scenario -eq 'duplicateSeed') { $excluded.seed=1; $search.excludedCandidates=@($excluded) }
        if ($scenario -eq 'partial') { $search.warnings = @('candidate_generation_incomplete', 'routing_timeout', 'secret') }
        if ($scenario -eq 'duplicate') { $second=$candidate.Clone(); $second.seed=2; $search.candidates = @($candidate, $second) }
        $plan = @{search=$search;advisorCallCount=1;advisorStatus='stopped';advisorFailure=$null;attempts=@(@{seed=1},@{seed=2})}
        $responses = @($search, $plan)
        $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
        $listener.Start()
        $port = $listener.LocalEndpoint.Port
        $job = Start-ThreadJob -ArgumentList $listener, $responses, $requestCount, $scenario -ScriptBlock {
            param($listener, $responses, $count, $scenario)
            $gaps = [Collections.Generic.List[double]]::new()
            $timer = [Diagnostics.Stopwatch]::new()
            for ($i=0; $i -lt $count; $i++) {
                $connection = $listener.AcceptTcpClient()
                if ($i -gt 0) { $gaps.Add($timer.Elapsed.TotalMilliseconds) }
                try {
                    $stream = $connection.GetStream()
                    $stream.ReadTimeout = 10000
                    $header = ''
                    while (-not $header.EndsWith("`r`n`r`n")) { $b=$stream.ReadByte(); if($b -lt 0){throw 'EOF'}; $header += [char]$b; if($header.Length -gt 16384){throw 'Header too large'} }
                    $length = [int]([regex]::Match($header, '(?im)^Content-Length: (\d+)').Groups[1].Value)
                    for($j=0;$j -lt $length;$j++){ if($stream.ReadByte() -lt 0){throw 'Missing body'} }
                    if ($scenario -eq 'disconnect') { continue }
                    $status = if($scenario -eq 'quota'){503}else{200}
                    $body = if($scenario -eq 'quota'){'{"code":"routing_rate_limited","detail":"secret"}'}elseif($scenario -eq 'invalid'){'{"candidates":"bad"}'}else{$responses[$i] | ConvertTo-Json -Depth 20 -Compress}
                    $bytes = [Text.Encoding]::UTF8.GetBytes($body)
                    $head = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 $status Test`r`nContent-Type: application/json`r`nContent-Length: $($bytes.Length)`r`nConnection: close`r`n`r`n")
                    $stream.Write($head); $stream.Write($bytes); $stream.Flush(); $timer.Restart()
                } finally { $connection.Dispose() }
            }
            @{calls=$count;gaps=$gaps.ToArray()}
        }
        try {
            $output = Join-Path $root ($scenario + '.json')
            & pwsh -NoProfile -File $runner -RunLive -BaseUrl "http://127.0.0.1:$port" -CaseLimit 1 -OutputPath $output
            $code = $LASTEXITCODE
            Assert-True ($(if($scenario -eq 'pass'){$code -eq 0}else{$code -ne 0})) "Wrong status: $scenario"
            Assert-True ($null -ne (Wait-Job $job -Timeout 15)) 'Stub did not complete'
            $observed = Receive-Job $job -ErrorAction Stop
            Assert-True ($observed.calls -eq $requestCount) 'Unexpected calls'
            if($requestCount -eq 2){ Assert-True ($observed.gaps[0] -ge 4900) 'Missing pacing' }
            $raw = Get-Content $output -Raw
            Assert-True (-not $raw.Contains('secret')) 'Provider detail leaked'
            $report = $raw | ConvertFrom-Json -AsHashtable
			Assert-True ($report.contractVersion -ceq 'route-search-v3') 'Report must identify the current advisor prompt contract.'
            Assert-True ($report.results.Count -eq 2) 'Missing arm in report'
            Assert-True (@($report.results | Where-Object status -eq 'unrun').Count -eq 2-$requestCount) 'Wrong unrun count'
            if($scenario -eq 'pass'){ Assert-True ($report.results[0].bestMeanTargetError -eq 0) 'Wrong error metric' }
            if($scenario -eq 'duplicate'){ Assert-True ($report.results[0].uniqueCount -eq 1) 'Duplicate count wrong' }
            if($scenario -eq 'partial'){ Assert-True ($report.results[0].routingFailures -ccontains 'routing_timeout') 'Routing failure reason missing' }
            if($scenario -eq 'noMatch') {
                Assert-True ($report.results[0].status -ceq 'noMatch') 'Empty valid result is not a provider error'
                Assert-True ($report.results[0].usableCount -eq 0 -and $null -eq $report.results[0].bestMeanTargetError) 'Empty result has false metrics'
            }
            if($scenario -eq 'emptyPartial'){ Assert-True ($report.results[0].status -ceq 'incomplete') 'Partial failure was hidden' }
            if($scenario -in @('badExclusion','duplicateSeed','missingQuality','badQualityTotal','badQualityState','unjustifiedExclusion','falseTargetMatch','arrayPolicy','excludedArrayPolicy')){
                Assert-True (@($report.results | Where-Object { $_.status -cne 'error' -or $_.error -cne 'invalid_response' }).Count -eq 0) "Malformed result accepted: $scenario"
            }
        } finally { $listener.Stop(); Stop-Job $job -ErrorAction SilentlyContinue; Remove-Job $job -Force }
    }
    Write-Output 'Refinement harness passed: offline, pass, partial, duplicate, quota, malformed response, disconnect, pacing, no retries.'
} finally {
    Get-ChildItem -LiteralPath $root -File | Remove-Item -Force
    Remove-Item -LiteralPath $root
}
# Negative child scenarios intentionally exit 1; do not leak that to the CI wrapper.
exit 0
