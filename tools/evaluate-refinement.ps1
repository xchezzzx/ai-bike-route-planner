[CmdletBinding()]
param(
    [switch]$RunLive,
    [string]$BaseUrl,
    [ValidateRange(1,4)][int]$CaseLimit = 4,
    [string]$OutputPath,
    [string]$CorpusPath = (Join-Path $PSScriptRoot '../docs/evaluation/route-refinement-v1.json')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Is-Number($n) { return $null -ne $n -and $n -is [ValueType] -and $n -isnot [bool] -and [double]::IsFinite([double]$n) }
function Measure-Search($body, $intent, [bool]$advised) {
    $search = if($advised){$body.search}else{$body}
    if ($search -isnot [Collections.IDictionary] -or $search.candidates -isnot [array] -or $search.candidates.Count -lt 1 -or $search.candidates.Count -gt 3 -or
        -not (Is-Number $search.attemptedCount) -or $search.attemptedCount -notin @(1,2,3) -or $search.warnings -isnot [array]) { throw 'Invalid search response' }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $metrics = @($search.candidates | ForEach-Object {
        $route = $_.route
        if ($route.geometry -isnot [array] -or $route.geometry.Count -lt 4 -or -not (Is-Number $route.distanceMeters) -or $route.distanceMeters -le 0 -or
            -not (Is-Number $route.estimatedDurationSeconds) -or $route.estimatedDurationSeconds -le 0 -or $route.gpx -isnot [string] -or -not $route.gpx -or
            ($null -ne $route.ascentMeters -and (-not (Is-Number $route.ascentMeters) -or $route.ascentMeters -lt 0))) { throw 'Invalid route response' }
        $positions = @($route.geometry | ForEach-Object {
            if(-not (Is-Number $_.latitude) -or [Math]::Abs($_.latitude) -gt 90 -or -not (Is-Number $_.longitude) -or [Math]::Abs($_.longitude) -gt 180){throw 'Invalid geometry'}
            @($_.latitude, $_.longitude) | ConvertTo-Json -Compress
        })
        if($positions[0] -cne $positions[-1]){throw 'Unclosed route'}
        if(@($positions | Select-Object -Unique).Count -lt 3){throw 'Degenerate route'}
        $forward = $positions -join '|'
        [array]::Reverse($positions)
        $reverse = $positions -join '|'
        $key = if([string]::CompareOrdinal($forward,$reverse) -le 0){$forward}else{$reverse}
        [void]$seen.Add($key)
        $errors = [Collections.Generic.List[double]]::new()
        $distanceError = $null; $durationError = $null
        if($intent.Contains('targetDistanceMeters') -and $null -ne $intent.targetDistanceMeters){ $distanceError=[Math]::Abs($route.distanceMeters-$intent.targetDistanceMeters)/$intent.targetDistanceMeters; $errors.Add($distanceError) }
        if($intent.Contains('targetDurationSeconds') -and $null -ne $intent.targetDurationSeconds){ $durationError=[Math]::Abs($route.estimatedDurationSeconds-$intent.targetDurationSeconds)/$intent.targetDurationSeconds; $errors.Add($durationError) }
        @{distanceMeters=$route.distanceMeters;durationSeconds=$route.estimatedDurationSeconds;ascentMeters=$route.ascentMeters;
          distanceRelativeError=$distanceError;durationRelativeError=$durationError;meanTargetError=($errors | Measure-Object -Average).Average;
          targetsMatched=(@($errors | Where-Object {$_ -gt 0.1000000001}).Count -eq 0)}
    })
    $advisorCalls = 0; $advisorStatus = $null; $advisorFailure = $null
    if($advised){
        if($body.advisorCallCount -notin @(0,1) -or $body.advisorStatus -cnotin @('notNeeded','skippedNoCandidates','skippedRoutingFailure','searched','stopped','failed') -or
            ($null -ne $body.advisorFailure -and $body.advisorFailure -cnotin @('notConfigured','authentication','quota','unavailable','timeout','invalidResponse')) -or
            $body.attempts -isnot [array] -or $body.attempts.Count -ne $search.attemptedCount){throw 'Invalid advisor response'}
        $advisorCalls=$body.advisorCallCount; $advisorStatus=$body.advisorStatus; $advisorFailure=$body.advisorFailure
    }
    $incomplete = 'candidate_generation_incomplete' -cin $search.warnings -or $null -ne $advisorFailure -or $seen.Count -ne $metrics.Count
    $knownFailures = @('routing_rate_limited','routing_not_configured','routing_credentials_rejected','routing_unavailable','routing_timeout','route_not_found','routing_invalid_response','routing_limit_exceeded')
    $routingFailures = @($search.warnings | Where-Object {$_ -is [string] -and $_ -cin $knownFailures})
    if($advised){$routingFailures += @($body.attempts | Where-Object {$_ -is [Collections.IDictionary] -and $_.Contains('failure') -and $_.failure -cin $knownFailures} | ForEach-Object {$_.failure})}
    return @{status=$(if($incomplete){'incomplete'}else{'passed'});routingCalls=$search.attemptedCount;advisorCalls=$advisorCalls;
        advisorStatus=$advisorStatus;advisorFailure=$advisorFailure;usableCount=$metrics.Count;uniqueCount=$seen.Count;
        ascentAvailableCount=@($metrics | Where-Object {$null -ne $_.ascentMeters}).Count;
        bestMeanTargetError=($metrics | Measure-Object meanTargetError -Minimum).Minimum;
        routingFailures=@($routingFailures | Select-Object -Unique);
        targetsMatched=(@($metrics | Where-Object targetsMatched).Count -gt 0);candidates=$metrics;
        quota=($advisorFailure -ceq 'quota' -or 'routing_rate_limited' -cin $search.warnings)}
}

try {
    $corpus = Get-Content -LiteralPath $CorpusPath -Raw | ConvertFrom-Json -AsHashtable
    if($corpus.version -ne 1 -or $corpus.contractVersion -cne 'route-search-v1' -or $corpus.routeCases.Count -ne 4 -or $corpus.advisorCases.Count -ne 6){throw 'Invalid corpus'}
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach($case in $corpus.routeCases){
        $r=$case.request
        if(-not $ids.Add($case.id) -or $r.shape -cne 'loop' -or $r.profile -cne 'road' -or $r.elevation -cnotin @('balanced','minimize','seekClimbs') -or
            -not (Is-Number $r.start.latitude) -or [Math]::Abs($r.start.latitude) -gt 90 -or -not (Is-Number $r.start.longitude) -or [Math]::Abs($r.start.longitude) -gt 180){throw 'Invalid route case'}
        $hasTarget=$false
        foreach($key in @('targetDistanceMeters','targetDurationSeconds')){if($r.Contains($key) -and $null -ne $r[$key]){if(-not (Is-Number $r[$key]) -or $r[$key] -le 0){throw 'Invalid target'}; $hasTarget=$true}}
        if(-not $hasTarget){throw 'Missing target'}
    }
    if(-not $RunLive){Write-Output 'Refinement corpus valid: 6 advisor and 4 route cases. No network calls.'; exit 0}
    $uri=$null; $ip=$null
    if(-not [Uri]::TryCreate($BaseUrl,[UriKind]::Absolute,[ref]$uri) -or $uri.Scheme -notin @('http','https') -or $uri.UserInfo -or $uri.Query -or $uri.Fragment -or $uri.AbsolutePath -ne '/' -or
        -not [Net.IPAddress]::TryParse($uri.DnsSafeHost,[ref]$ip) -or -not [Net.IPAddress]::IsLoopback($ip)){throw 'Use an explicit loopback API URL'}
    if(-not $OutputPath){$OutputPath=Join-Path $PSScriptRoot ("../artifacts/refinement-comparison-{0}-{1}.json" -f (Get-Date -Format yyyyMMddTHHmmss),[guid]::NewGuid().ToString('N').Substring(0,8))}
    $OutputPath=[IO.Path]::GetFullPath($OutputPath)
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($OutputPath)) | Out-Null
    $results=@(foreach($case in ($corpus.routeCases | Select-Object -First $CaseLimit)){foreach($arm in @('baseline','advised')){@{id=$case.id;arm=$arm;intent=$case.request;status='unrun';routingCalls=$null;advisorCalls=$null}}})
    $report=@{contractVersion='route-search-v1';createdAtUtc=[DateTime]::UtcNow.ToString('O');requestDelayMs=5000;results=$results}
    $handler=[Net.Http.HttpClientHandler]::new(); $handler.AllowAutoRedirect=$false
    $client=[Net.Http.HttpClient]::new($handler); $client.Timeout=[TimeSpan]::FromSeconds(100); $client.MaxResponseContentBufferSize=16*1024*1024
    $stop=$false; $sent=0
    try {
        foreach($entry in $results){
            if($stop){continue}
            if($sent -gt 0){Start-Sleep -Milliseconds 5000}
            $timer=[Diagnostics.Stopwatch]::StartNew()
            $content=[Net.Http.StringContent]::new(($entry.intent | ConvertTo-Json -Depth 10 -Compress),[Text.Encoding]::UTF8,'application/json')
            $response=$null
            try {
                $sent++
                $endpoint=if($entry.arm -eq 'baseline'){'/api/routes/candidates'}else{'/api/routes/plan'}
                $response=$client.PostAsync([Uri]::new($uri,$endpoint),$content).GetAwaiter().GetResult()
                $body=$response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json -AsHashtable
                if(-not $response.IsSuccessStatusCode){
                    $entry.status='error'; $entry.error='http_'+[int]$response.StatusCode
                    $known=@('routing_rate_limited','routing_not_configured','routing_credentials_rejected','routing_unavailable','routing_timeout','route_not_found','routing_invalid_response','search_distance_out_of_range','routing_limit_exceeded','unsupported_intent')
                    if($body -is [Collections.IDictionary] -and $body.Contains('code') -and $body.code -is [string] -and $body.code -cin $known){$entry.error=$body.code}
                    $entry.quota=$entry.error -ceq 'routing_rate_limited'
                    $stop=$entry.error -cin @('routing_rate_limited','routing_not_configured','routing_credentials_rejected')
                }else{
                    $measured=Measure-Search $body $entry.intent ($entry.arm -eq 'advised')
                    foreach($key in $measured.Keys){$entry[$key]=$measured[$key]}
                    if($entry.quota -or $entry.advisorFailure -cin @('authentication','notConfigured')){$stop=$true}
                }
            } catch {
                $entry.status='error'; $entry.error='invalid_response'
                if($_.Exception -is [Net.Http.HttpRequestException] -or $_.Exception.InnerException -is [Net.Http.HttpRequestException] -or $_.Exception -is [OperationCanceledException] -or $_.Exception.InnerException -is [OperationCanceledException]){$entry.error='transport_error';$stop=$true}
            } finally {
                $entry.latencyMs=$timer.ElapsedMilliseconds; $content.Dispose(); if($response){$response.Dispose()}
                $report | ConvertTo-Json -Depth 25 | Set-Content -LiteralPath $OutputPath -Encoding utf8
            }
            Write-Output "$($entry.id) $($entry.arm): $($entry.status)"
        }
    } finally { $client.Dispose(); $report | ConvertTo-Json -Depth 25 | Set-Content -LiteralPath $OutputPath -Encoding utf8 }
    Write-Output "Report: $OutputPath"
    if(@($results | Where-Object status -ne 'passed').Count -gt 0){exit 1}
    exit 0
} catch { Write-Error 'Refinement evaluation failed validation or execution. No automatic retries.'; exit 1 }
