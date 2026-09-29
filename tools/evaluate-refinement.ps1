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
function Assert-RoadAssessment($candidate, $intent, [bool]$excluded) {
    $a=$candidate.assessment
    if($a -isnot [Collections.IDictionary] -or $a.targetsMatched -isnot [bool] -or -not (Is-Number $a.score) -or $a.score -lt 0){throw 'Invalid assessment'}
    $q=$a.quality
    if($q -isnot [Collections.IDictionary] -or $q.policyVersion -isnot [string] -or $q.policyVersion -cne 'road-v1' -or -not (Is-Number $q.geometryLengthMeters) -or $q.geometryLengthMeters -le 0 -or
        $q.surfaceEvidenceState -isnot [string] -or $q.surfaceEvidenceState -cnotin @('unavailable','partial','complete') -or $q.waytypeSupplied -isnot [bool]){throw 'Invalid quality'}
    $length=[double]$q.geometryLengthMeters; $epsilon=$length*1e-8
    $partitions=@{
        surface=@('pavedMeters','nonRoadMeters','otherKnownMeters','unknownMeters')
        ways=@('unknownMeters','stateRoadMeters','roadMeters','streetMeters','pathMeters','trackMeters','cyclewayMeters','footwayMeters','stepsMeters','ferryMeters','constructionMeters')
    }
    foreach($name in $partitions.Keys){
        $part=$q[$name]; $sum=0.0
        if($part -isnot [Collections.IDictionary]){throw 'Invalid partition'}
        foreach($key in $partitions[$name]){
            if(-not (Is-Number $part[$key]) -or $part[$key] -lt 0){throw 'Invalid category'}
            $sum += $part[$key]
        }
        if([Math]::Abs($sum-$length) -gt $epsilon){throw 'Inconsistent partition'}
    }
    if(($q.surfaceEvidenceState -ceq 'complete' -and $q.surface.unknownMeters -ne 0) -or
        ($q.surfaceEvidenceState -ceq 'partial' -and $q.surface.unknownMeters -le 0) -or
        ($q.surfaceEvidenceState -ceq 'unavailable' -and [Math]::Abs($q.surface.unknownMeters-$length) -gt $epsilon) -or
        (-not $q.waytypeSupplied -and [Math]::Abs($q.ways.unknownMeters-$length) -gt $epsilon)){throw 'Inconsistent evidence state'}
    foreach($key in @('repeatedMeters','sharedStemMeters','remainingRepeatedMeters')){
        if(-not (Is-Number $q[$key]) -or $q[$key] -lt 0){throw 'Invalid retracing'}
    }
    if($q.repeatedMeters -gt $length+$epsilon -or $q.sharedStemMeters -gt $q.repeatedMeters -or
        [Math]::Abs($q.repeatedMeters-$q.sharedStemMeters-$q.remainingRepeatedMeters) -gt $epsilon){throw 'Inconsistent retracing'}
    $route=if($excluded){$candidate}else{$candidate.route}
    if($route -isnot [Collections.IDictionary]){throw 'Invalid route metrics'}
    $matched=$true
    foreach($target in @(@('targetDistanceMeters','distanceMeters','distanceDeltaMeters'),@('targetDurationSeconds','estimatedDurationSeconds','durationDeltaSeconds'))){
        $metric=$route[$target[1]]; $delta=$a[$target[2]]
        if(-not (Is-Number $metric) -or $metric -le 0 -or -not $a.Contains($target[2])){throw 'Invalid target metric'}
        if($intent.Contains($target[0]) -and $null -ne $intent[$target[0]]){
            $requested=$intent[$target[0]]; $expected=$metric-$requested
            if(-not (Is-Number $delta) -or [Math]::Abs($delta-$expected) -gt $requested*1e-8){throw 'Inconsistent target delta'}
            if([Math]::Abs($expected)/$requested -gt 0.1000000001){$matched=$false}
        }elseif($null -ne $delta){throw 'Unexpected target delta'}
    }
    if($a.targetsMatched -ne $matched){throw 'Inconsistent target match'}
    $reasons=[Collections.Generic.List[string]]::new()
    if(-not $matched){$reasons.Add('targets_not_met')}
    if($q.surface.nonRoadMeters -gt [Math]::Max(100,0.005*$length)){$reasons.Add('road_surface_limit_exceeded')}
    if($q.ways.stepsMeters -gt 0 -or $q.ways.ferryMeters -gt 0 -or $q.ways.constructionMeters -gt 0){$reasons.Add('road_waytype_excluded')}
    if($excluded){
        if($candidate.reasons -isnot [array] -or $reasons.Count -eq 0 -or $candidate.reasons.Count -ne $reasons.Count){throw 'Invalid reasons'}
        foreach($reason in $reasons){if($reason -cnotin $candidate.reasons){throw 'Inconsistent reasons'}}
    }elseif($reasons.Count -gt 0){throw 'Ineligible retained route'}
}
function Measure-Search($body, $intent, [bool]$advised) {
    $search = if($advised){$body.search}else{$body}
    if ($search -isnot [Collections.IDictionary] -or $search.candidates -isnot [array] -or $search.excludedCandidates -isnot [array] -or
        $search.candidates.Count + $search.excludedCandidates.Count -notin @(1,2,3) -or
        -not (Is-Number $search.attemptedCount) -or $search.attemptedCount -notin @(1,2,3) -or $search.warnings -isnot [array]) { throw 'Invalid search response' }
    if($search.candidates.Count + $search.excludedCandidates.Count -gt $search.attemptedCount){throw 'Invalid candidate count'}
    $seeds = [Collections.Generic.HashSet[int]]::new()
    foreach($candidate in @($search.candidates) + @($search.excludedCandidates)){
        if(-not (Is-Number $candidate.seed) -or $candidate.seed -lt 1 -or $candidate.seed -gt 16 -or [Math]::Truncate($candidate.seed) -ne $candidate.seed -or -not $seeds.Add([int]$candidate.seed)){throw 'Invalid seed'}
    }
    foreach($candidate in $search.candidates){Assert-RoadAssessment $candidate $intent $false}
    foreach($candidate in $search.excludedCandidates){Assert-RoadAssessment $candidate $intent $true}
    $exclusions = @($search.excludedCandidates | ForEach-Object {
        if(-not (Is-Number $_.distanceMeters) -or $_.distanceMeters -le 0 -or -not (Is-Number $_.estimatedDurationSeconds) -or $_.estimatedDurationSeconds -le 0 -or
            $_.reasons -isnot [array] -or $_.reasons.Count -lt 1 -or $_.reasons.Count -gt 3 -or
            @($_.reasons | Where-Object {$_ -cnotin @('targets_not_met','road_surface_limit_exceeded','road_waytype_excluded')}).Count -gt 0){throw 'Invalid exclusion'}
        @{seed=$_.seed;reasons=$_.reasons}
    })
    if($search.candidates.Count -eq 0 -and 'no_candidate_meets_requirements' -cnotin $search.warnings){throw 'Missing no-match warning'}
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
    $incomplete = $incomplete -or $routingFailures.Count -gt 0
    return @{status=$(if($incomplete){'incomplete'}elseif($metrics.Count -eq 0){'noMatch'}else{'passed'});routingCalls=$search.attemptedCount;advisorCalls=$advisorCalls;
        advisorStatus=$advisorStatus;advisorFailure=$advisorFailure;usableCount=$metrics.Count;uniqueCount=$seen.Count;
        ascentAvailableCount=@($metrics | Where-Object {$null -ne $_.ascentMeters}).Count;
        bestMeanTargetError=$(if($metrics.Count -gt 0){($metrics | Measure-Object meanTargetError -Minimum).Minimum}else{$null});
        excludedCount=$exclusions.Count;excludedCandidates=$exclusions;
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
