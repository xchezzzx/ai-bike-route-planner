#requires -Version 7.4
[CmdletBinding()]
param([ValidateRange(1024, 65535)][int]$Port = 8989)
$ErrorActionPreference = 'Stop'
$infra = Split-Path $PSScriptRoot -Parent
$base = "http://127.0.0.1:$Port"
$info = Invoke-RestMethod "$base/info" -TimeoutSec 10 -MaximumRedirection 0
if ($info.version -ne '11.1' -or -not $info.elevation -or 'road' -notin @($info.profiles.name)) { throw 'Unexpected engine readiness' }
$evidence = @()
foreach ($kind in @('a-b', 'round_trip')) {
    # Public sample locations in Tel Aviv, not user GPS or saved routes.
    $request = @{ profile = 'road'; points_encoded = $false; elevation = $true; instructions = $true; details = @('surface', 'road_class', 'road_environment'); timeout_ms = 14000 }
    if ($kind -eq 'a-b') {
        $request.points = @(@(34.7818, 32.0853), @(34.7934, 32.0995))
    } else {
        $request.points = (,@(34.7818, 32.0853))
        $request.algorithm = 'round_trip'
        $request.'round_trip.distance' = 10000
        $request.'round_trip.seed' = 1
    }
    $timer = [Diagnostics.Stopwatch]::StartNew()
    try { $response = Invoke-RestMethod "$base/route" -Method Post -ContentType 'application/json' -Body ($request | ConvertTo-Json -Depth 6) -TimeoutSec 15 -MaximumRedirection 0 }
    catch { throw "Local $kind request failed; response body withheld" }
    $timer.Stop()
    $path = $response.paths[0]
    if (-not $path -or $path.distance -le 10 -or $path.time -le 0 -or $path.points.type -ne 'LineString' -or $path.points.coordinates.Count -lt 2) { throw "Invalid local $kind route" }
    foreach ($key in @('surface', 'road_class', 'road_environment')) {
        if (-not $path.details.$key.Count) { throw "Missing $key details for $kind" }
    }
    if (@($path.points.coordinates | Where-Object { $_.Count -ne 3 }).Count -gt 0) { throw 'Expected 3D route coordinates' }
    $elevations = @($path.points.coordinates | ForEach-Object { $_[2] }) | Measure-Object -Minimum -Maximum
    $item = @{ kind = $kind; distanceMeters = $path.distance; durationMilliseconds = $path.time; geometryPoints = $path.points.coordinates.Count; elevationMinimumMeters = $elevations.Minimum; elevationMaximumMeters = $elevations.Maximum; elapsedMilliseconds = [math]::Round($timer.Elapsed.TotalMilliseconds); detailTypes = @($path.details.PSObject.Properties.Name); checkedAtUtc = [DateTime]::UtcNow.ToString('o') }
    $evidence += $item
    Write-Host "PASS $kind distance=$([math]::Round($path.distance))m points=$($path.points.coordinates.Count) elapsed=$([math]::Round($timer.Elapsed.TotalMilliseconds))ms; no route body printed"
}
$evidence | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $infra 'artifacts/regional/route-verification.json') -Encoding utf8NoBOM
