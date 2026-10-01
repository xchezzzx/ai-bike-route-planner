#requires -Version 7.4
[CmdletBinding()]
param([switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$repo = Split-Path $root -Parent
$launcher = Join-Path $repo 'tools/start-graphhopper.ps1'
foreach ($file in @($launcher, (Join-Path $root 'graphhopper/Dockerfile'), (Join-Path $root 'graphhopper/config.yml'))) {
    if (-not (Test-Path -LiteralPath $file)) { throw "Missing engine implementation: $file" }
}
$run = Join-Path $root "graphhopper/artifacts/smoke-$([guid]::NewGuid().ToString('N'))"
$project = "gh-smoke-$([guid]::NewGuid().ToString('N').Substring(0, 12))"
$port = 18989
$started = $false
$dockerExecutable = (Get-Command docker -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
function Assert-True($condition, $message) { if (-not $condition) { throw $message } }
$model = Get-Content -Raw -LiteralPath (Join-Path $root 'graphhopper/road.json') | ConvertFrom-Json
Assert-True (($model.priority.if -join ' ') -match 'road_environment == FORD') 'Road model must explicitly exclude fords'
Assert-True (($model.priority.if -join ' ') -match 'road_class == CONSTRUCTION') 'Road model must explicitly exclude construction'
function Route($points) {
    $body = @{ profile = 'road'; points = $points; points_encoded = $false; elevation = $true; instructions = $true; details = @('surface', 'road_class', 'road_environment'); timeout_ms = 14000 } | ConvertTo-Json -Depth 8
    (Invoke-RestMethod "http://127.0.0.1:$port/route" -Method Post -ContentType 'application/json' -Body $body -TimeoutSec 15).paths[0]
}
try {
    $elevation = Join-Path $run 'elevation/srtm3-kurviger'
    New-Item -ItemType Directory -Path $elevation -Force | Out-Null
    # SRTM3 is 1201x1201 signed big-endian samples. A flat 100m tile proves
    # elevation ingestion deterministically without contacting the public mirror.
    $samples = [byte[]]::new(1201 * 1201 * 2)
    for ($i = 1; $i -lt $samples.Length; $i += 2) { $samples[$i] = 100 }
    $zip = [IO.Compression.ZipFile]::Open((Join-Path $elevation 'N32E034.hgt.zip'), [IO.Compression.ZipArchiveMode]::Create)
    try {
        $entry = $zip.CreateEntry('N32E034.hgt')
        $entry.LastWriteTime = [DateTimeOffset]::new(2000, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
        $stream = $entry.Open()
        try { $stream.Write($samples, 0, $samples.Length) } finally { $stream.Dispose() }
    } finally { $zip.Dispose() }
    # The launcher owns this fresh directory; fixture generation is test-owned.
    @{ project = $project; workspace = $repo } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $run 'owner.json') -Encoding utf8NoBOM
    $started = $true
    & $launcher -DataDirectory $run -ProjectName $project -Port $port -OsmFile (Join-Path $PSScriptRoot 'synthetic.osm') -Synthetic -SkipBuild:$SkipBuild -WaitSeconds 240
    $info = Invoke-RestMethod "http://127.0.0.1:$port/info" -TimeoutSec 10
    Assert-True (@($info.profiles | Where-Object name -eq 'road').Count -eq 1) 'Expected road profile'
    Assert-True ($info.elevation -eq $true) 'Expected SRTM elevation enabled'
    $path = Route @(@(34.800, 32.000), @(34.810, 32.010))
    Assert-True ($path.distance -gt 1700 -and $path.distance -lt 2500) 'Expected paved detour, not gravel/steps shortcut'
    Assert-True ($path.points.coordinates[0].Count -eq 3) 'Expected 3D coordinates'
    Assert-True (@($path.points.coordinates | Where-Object { [math]::Abs($_[2] - 100) -gt 0.01 }).Count -eq 0) 'Expected generated 100m elevation, not missing-tile zeroes'
    Assert-True (@($path.details.surface | Where-Object { $_[2] -ne 'asphalt' }).Count -eq 0) 'Expected asphalt only'
    Assert-True (@($path.details.road_environment | Where-Object { $_[2] -eq 'ferry' }).Count -eq 0) 'Ferry shortcut must be avoided'
    $access = Route @(@(34.805, 32.000), @(34.805, 32.010))
    Assert-True ($access.distance -gt 1900) 'Prohibited bicycle shortcut must remain inaccessible'
    $cycleway = Route @(@(34.801, 32.000), @(34.809, 32.000))
    Assert-True (@($cycleway.details.road_class | Where-Object { $_[2] -eq 'cycleway' }).Count -gt 0) 'Cycleways must survive import'
    $ferry = Route @(@(34.810, 32.000), @(34.800, 32.010))
    Assert-True ($ferry.distance -gt 1700) 'Ferry must not be used as a diagonal shortcut'
    $loopBody = @{ profile = 'road'; points = (,@(34.800, 32.000)); algorithm = 'round_trip'; 'round_trip.distance' = 4000; 'round_trip.seed' = 1; points_encoded = $false; elevation = $true; instructions = $true; details = @('surface', 'road_class', 'road_environment'); timeout_ms = 14000 } | ConvertTo-Json -Depth 8
    $loop = (Invoke-RestMethod "http://127.0.0.1:$port/route" -Method Post -ContentType 'application/json' -Body $loopBody -TimeoutSec 15).paths[0]
    Assert-True ($loop.distance -gt 0 -and $loop.points.coordinates.Count -gt 2) 'Native round_trip must work without CH override'
    $before = Get-Content -Raw -LiteralPath (Join-Path $run 'readiness.json') | ConvertFrom-Json
    & $launcher -DataDirectory $run -ProjectName $project -Port $port -OsmFile (Join-Path $PSScriptRoot 'synthetic.osm') -Synthetic -SkipBuild -Restart -WaitSeconds 120
    $after = Get-Content -Raw -LiteralPath (Join-Path $run 'readiness.json') | ConvertFrom-Json
    Assert-True ($before.graphIdentity -eq $after.graphIdentity) 'Restart must reuse graph identity'
    Assert-True ($after.graphReused -eq $true) 'Restart must load the completed graph'
    & $launcher -DataDirectory $run -ProjectName $project -Port $port -Stop
    $graph = Join-Path $run "graphs/$($after.graphIdentity)"
    $input = Get-ChildItem -LiteralPath (Join-Path $run 'input') -Filter '*.osm' | Select-Object -First 1
    foreach ($name in @('import-complete.sha256', 'data-files.sha256', 'nodes')) {
        $file = Join-Path $graph $name
        Move-Item -LiteralPath $file -Destination "$file.test-backup"
        try {
            $output = & $dockerExecutable run --rm --network none --volume "${run}:/data" --env "GH_OSM_FILE=/data/input/$($input.Name)" --env GH_MIN_NETWORK_SIZE=0 $after.imageId 2>&1
            $exitCode = $LASTEXITCODE
            Assert-True ($exitCode -eq 4) "Expected refusal for missing $name; got $exitCode"
            Assert-True (($output -join "`n") -notmatch 'GRAPH_IMPORT|GRAPH_REUSE') 'Partial graph must not import or reuse silently'
        } finally { Move-Item -LiteralPath "$file.test-backup" -Destination $file }
    }
    $geometry = Join-Path $graph 'geometry'
    foreach ($damage in @('nonempty truncation', 'same-size corruption')) {
        Copy-Item -LiteralPath $geometry -Destination "$geometry.test-backup"
        try {
            $stream = [IO.File]::Open($geometry, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite)
            try {
                if ($damage -eq 'nonempty truncation') { $stream.SetLength(100) }
                else {
                    $stream.Position = 128
                    $byte = $stream.ReadByte()
                    $stream.Position = 128
                    $stream.WriteByte($byte -bxor 1)
                }
            } finally { $stream.Dispose() }
            $output = & $dockerExecutable run --rm --network none --volume "${run}:/data" --env "GH_OSM_FILE=/data/input/$($input.Name)" --env GH_MIN_NETWORK_SIZE=0 --entrypoint timeout $after.imageId 15 /opt/graphhopper/entrypoint.sh 2>&1
            $exitCode = $LASTEXITCODE
            Assert-True ($exitCode -eq 4) "Expected refusal for geometry $damage; got $exitCode"
            Assert-True (($output -join "`n") -notmatch 'GRAPH_IMPORT|GRAPH_REUSE') 'Corrupted geometry must be refused before Java opens it'
        } finally { Move-Item -LiteralPath "$geometry.test-backup" -Destination $geometry -Force }
    }
    Write-Host 'PASS: synthetic profile/elevation/details, paved detour, bicycle access, cycleway retention, native loop, graph reuse, missing/truncated/corrupted-cache refusal'
} finally {
    if ($started) { & $launcher -DataDirectory $run -ProjectName $project -Port $port -Stop }
    Write-Host "Synthetic evidence retained at $run"
}
