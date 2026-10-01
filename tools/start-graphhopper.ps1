#requires -Version 7.4
[CmdletBinding()]
param(
    [string]$DataDirectory,
    [ValidatePattern('^[a-z0-9][a-z0-9_-]+$')][string]$ProjectName = 'ai-bike-gh-local',
    [ValidateRange(1024, 65535)][int]$Port = 8989,
    [string]$OsmFile,
    [switch]$Synthetic,
    [switch]$SkipBuild,
    [switch]$Restart,
    [switch]$Stop,
    [ValidateRange(1, 7200)][int]$WaitSeconds = 1800
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$repo = Split-Path $PSScriptRoot -Parent
$infra = Join-Path $repo 'infra/graphhopper'
if (-not $DataDirectory) { $DataDirectory = Join-Path $infra 'artifacts/regional' }
$DataDirectory = [IO.Path]::GetFullPath($DataDirectory)
if ($Synthetic -and -not $OsmFile -and -not $Stop) { throw '-Synthetic requires an explicit fixture -OsmFile' }
if ($OsmFile -and -not $Synthetic) { throw 'Custom OSM files are only supported with -Synthetic; regional data is pinned' }
if ($Restart -and $Stop) { throw '-Restart and -Stop are mutually exclusive' }
$sourceCommit = 'ebb578f4e94db72e82f2f8d3bc69417758e16e1c'
$jarHash = '8462f758d9ea49edaded557cec5c687a0a24f004ec371cd4daaeb0534824ea33'
$pbfUrl = 'https://download.geofabrik.de/asia/israel-and-palestine-260929.osm.pbf'
$pbfMd5 = 'd5325e11f553ae0580d01d79c9687e8b'
$pbfSha256 = 'f98a8b9ad7fb3df19c846d3a562d9394abc5657173faeddcb033a53f4aa701f3'
$dockerExecutable = (Get-Command docker -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$curlExecutable = (Get-Command curl -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
function Docker([string[]]$Arguments) {
    & $dockerExecutable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Docker command failed ($LASTEXITCODE): $($Arguments[0])" }
}
function Fetch([string]$Url, [string]$Destination) {
    & $curlExecutable --fail --location --silent --show-error --connect-timeout 20 --max-time 900 --output $Destination $Url
    if ($LASTEXITCODE -ne 0) { throw "Public download failed ($LASTEXITCODE). Partial file retained: $Destination" }
}
function Save-Json([object]$Value, [string]$Path) {
    $temp = "$Path.$([guid]::NewGuid().ToString('N')).tmp"
    $Value | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $temp -Encoding utf8NoBOM
    Move-Item -LiteralPath $temp -Destination $Path -Force
}
$names = @('GH_DATA_DIR', 'GH_PORT', 'GH_OSM_FILE', 'GH_MIN_NETWORK_SIZE', 'GH_IMAGE', 'GH_USER')
$saved = @{}
foreach ($name in $names) { $saved[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$compose = @('compose', '--env-file', (Join-Path $infra 'compose.env'), '-f', (Join-Path $infra 'compose.yml'), '-p', $ProjectName)
try {
    $env:GH_DATA_DIR = $DataDirectory.Replace('\', '/')
    $env:GH_PORT = [string]$Port
    $env:GH_MIN_NETWORK_SIZE = if ($Synthetic) { '0' } else { '200' }
    $env:GH_USER = '0:0'
    if (-not $IsWindows) {
        $idExecutable = (Get-Command id -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
        $uid = & $idExecutable -u
        if ($LASTEXITCODE -ne 0 -or $uid -notmatch '^\d+$') { throw 'Cannot determine host UID' }
        $gid = & $idExecutable -g
        if ($LASTEXITCODE -ne 0 -or $gid -notmatch '^\d+$') { throw 'Cannot determine host GID' }
        $env:GH_USER = "${uid}:${gid}"
    }
    $existing = @(Docker ($compose + @('ps', '-a', '-q', 'graphhopper')) | Where-Object { $_ })
    if ($existing.Count -gt 1) { throw 'Expected at most one engine in this Compose project' }
    if ($existing.Count -eq 1) {
        $old = (Docker @('inspect', $existing[0]) | ConvertFrom-Json)[0]
        $mount = @($old.Mounts | Where-Object Destination -eq '/data')[0]
        $mountedPath = $mount.Source.Replace('\', '/').TrimEnd('/')
        if ($mountedPath -ne $env:GH_DATA_DIR.TrimEnd('/')) { throw 'Compose project belongs to another data directory; use its original arguments or a unique -ProjectName' }
        $existingOwnerPath = Join-Path $DataDirectory 'owner.json'
        if (-not (Test-Path -LiteralPath $existingOwnerPath)) { throw 'Existing container has no ownership manifest; refusing to manage it' }
        $existingOwner = Get-Content -Raw -LiteralPath $existingOwnerPath | ConvertFrom-Json
        if ($existingOwner.project -ne $ProjectName -or $existingOwner.workspace -ne $repo -or $old.Config.Labels.'org.opencontainers.image.revision' -ne $sourceCommit) { throw 'Existing container ownership/engine identity mismatch' }
    }
    if ($Stop) {
        if ($existing.Count -eq 1) { Docker ($compose + @('stop', 'graphhopper')) }
        Write-Host 'Owned GraphHopper service stopped; data and unrelated containers preserved.'
        return
    }
    New-Item -ItemType Directory -Path (Join-Path $DataDirectory 'input') -Force | Out-Null
    $ownerPath = Join-Path $DataDirectory 'owner.json'
    if (Test-Path -LiteralPath $ownerPath) {
        $owner = Get-Content -Raw -LiteralPath $ownerPath | ConvertFrom-Json
        if ($owner.project -ne $ProjectName -or $owner.workspace -ne $repo) { throw 'Data directory belongs to a different workspace/project' }
    } else {
        if (@(Get-ChildItem -LiteralPath $DataDirectory -Force | Where-Object Name -ne 'input').Count -gt 0 -or @(Get-ChildItem -LiteralPath (Join-Path $DataDirectory 'input') -Force).Count -gt 0) { throw 'Refusing to adopt nonempty unowned data directory' }
        Save-Json @{ project = $ProjectName; workspace = $repo } $ownerPath
    }
    if ($Synthetic) {
        $inputHash = (Get-FileHash -LiteralPath $OsmFile -Algorithm SHA256).Hash.ToLowerInvariant()
        $inputName = "synthetic-$inputHash.osm"
        $inputPath = Join-Path $DataDirectory "input/$inputName"
        if (-not (Test-Path -LiteralPath $inputPath)) { Copy-Item -LiteralPath $OsmFile -Destination $inputPath }
        if ((Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $inputHash) { throw 'Synthetic cached input hash mismatch' }
    } else {
        $inputName = 'israel-and-palestine-260929.osm.pbf'
        $inputPath = Join-Path $DataDirectory "input/$inputName"
        $metadataPath = "$inputPath.json"
        if (-not (Test-Path -LiteralPath $inputPath)) {
            $suffix = [guid]::NewGuid().ToString('N')
            $partial = "$inputPath.$suffix.partial"
            Write-Host 'Downloading pinned public Geofabrik regional extract (no credentials)...'
            Fetch $pbfUrl $partial
            if ((Get-FileHash -LiteralPath $partial -Algorithm MD5).Hash.ToLowerInvariant() -ne $pbfMd5) { throw 'PBF download failed pinned Geofabrik checksum validation; partial retained' }
            $inputHash = (Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($inputHash -ne $pbfSha256) { throw 'PBF download SHA256 differs from pinned snapshot; partial retained' }
            Save-Json @{ url = $pbfUrl; md5 = $pbfMd5; sha256 = $inputHash; bytes = (Get-Item -LiteralPath $partial).Length; downloadedAtUtc = [DateTime]::UtcNow.ToString('o') } $metadataPath
            Move-Item -LiteralPath $partial -Destination $inputPath
        } else {
            if (-not (Test-Path -LiteralPath $metadataPath)) { throw 'PBF has no verified download manifest; refusing reuse' }
            $metadata = Get-Content -Raw -LiteralPath $metadataPath | ConvertFrom-Json
            $inputHash = (Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($metadata.url -ne $pbfUrl -or $metadata.sha256 -ne $inputHash -or $inputHash -ne $pbfSha256) { throw 'PBF identity mismatch; preserve it and choose a fresh data directory' }
        }
    }
    $env:GH_OSM_FILE = "/data/input/$inputName"
    if (-not $SkipBuild) { Docker ($compose + @('build', 'graphhopper')) }
    $image = (Docker @('image', 'inspect', 'ai-bike-graphhopper:11.1') | ConvertFrom-Json)[0]
    if ($image.Config.Labels.'org.opencontainers.image.revision' -ne $sourceCommit) { throw 'Unexpected GraphHopper image source revision' }
    $env:GH_IMAGE = $image.Id
    $watch = [Diagnostics.Stopwatch]::StartNew()
    if ($Restart -and $existing.Count -eq 1) { Docker ($compose + @('stop', 'graphhopper')) }
    Docker ($compose + @('up', '-d', '--no-build', 'graphhopper'))
    $id = @(Docker ($compose + @('ps', '-q', 'graphhopper')))[0]
    $samples = [Collections.Generic.List[object]]::new()
    $ready = $false
    while ($watch.Elapsed.TotalSeconds -lt $WaitSeconds) {
        $state = (Docker @('inspect', '--format', '{{json .State}}', $id) | ConvertFrom-Json)
        if (-not $state.Running) {
            Docker @('logs', '--tail', '35', $id)
            throw "Engine exited: code=$($state.ExitCode), OOM=$($state.OOMKilled). Data preserved."
        }
        $samples.Add(@{ elapsedSeconds = [math]::Round($watch.Elapsed.TotalSeconds, 1); stats = (Docker @('stats', '--no-stream', '--format', '{{json .}}', $id) | ConvertFrom-Json) })
        try {
            $info = Invoke-RestMethod "http://127.0.0.1:$Port/info" -TimeoutSec 3 -MaximumRedirection 0
            $ev = @($info.encoded_values.PSObject.Properties.Name)
            if ($info.version -eq '11.1' -and $info.elevation -eq $true -and @($info.profiles | Where-Object name -eq 'road').Count -eq 1 -and @('surface', 'road_class', 'road_environment', 'bike_access' | Where-Object { $_ -notin $ev }).Count -eq 0) {
                $ready = $true
                break
            }
        } catch { }
        Start-Sleep -Seconds 3
    }
    $watch.Stop()
    Save-Json $samples (Join-Path $DataDirectory 'resource-samples.json')
    if (-not $ready) { throw "Readiness timeout after $WaitSeconds seconds. Service/data retained; inspect with docker logs $id" }
    $startup = Get-Content -Raw -LiteralPath (Join-Path $DataDirectory 'startup.json') | ConvertFrom-Json
    $manifest = Join-Path $DataDirectory "graphs/$($startup.graphIdentity)/identity.txt"
    if (-not (Select-String -LiteralPath $manifest -SimpleMatch $jarHash -Quiet)) { throw 'Imported JAR hash does not match pinned release' }
    $peak = Docker @('exec', $id, 'cat', '/sys/fs/cgroup/memory.peak')
    $record = @{ url = "http://127.0.0.1:$Port"; containerId = $id; imageId = $image.Id; sourceCommit = $sourceCommit; jarSha256 = $jarHash; osmSha256 = $inputHash; graphIdentity = $startup.graphIdentity; graphReused = $startup.graphReused; readyAfterSeconds = [math]::Round($watch.Elapsed.TotalSeconds, 2); memoryPeakBytes = [long]$peak; checkedAtUtc = [DateTime]::UtcNow.ToString('o'); info = $info }
    Save-Json $record (Join-Path $DataDirectory 'readiness.json')
    $record | ConvertTo-Json -Depth 20 -Compress | Add-Content -LiteralPath (Join-Path $DataDirectory 'readiness-history.jsonl') -Encoding utf8NoBOM
    Write-Host "Ready: http://127.0.0.1:$Port profile=road elevation=true graphReused=$($startup.graphReused) elapsed=$([math]::Round($watch.Elapsed.TotalSeconds, 1))s peakMemory=$([math]::Round([long]$peak / 1MB))MiB"
    Write-Host "Identity and resource evidence: $DataDirectory"
} finally {
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $saved[$name], 'Process') }
}
