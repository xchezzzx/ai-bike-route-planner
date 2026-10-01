#requires -Version 7.4
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repo = Split-Path (Split-Path (Split-Path $PSScriptRoot -Parent) -Parent) -Parent
$infra = Join-Path $repo 'infra/graphhopper'
$docker = (Get-Command docker -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
$saved = [Environment]::GetEnvironmentVariable('GH_USER', 'Process')
$volume = "gh-permissions-$([guid]::NewGuid().ToString('N'))"
$created = $false
try {
    $env:GH_USER = '1001:1001'
    $configText = & $docker compose --env-file (Join-Path $infra 'compose.env') -f (Join-Path $infra 'compose.yml') config --format json
    if ($LASTEXITCODE -ne 0) { throw 'Compose permission test configuration failed' }
    $config = $configText | ConvertFrom-Json
    if ($config.services.graphhopper.user -ne '1001:1001') { throw 'Compose must honor the host runtime UID:GID' }
    & $docker volume create $volume | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Permission fixture volume creation failed' }
    $created = $true
    # A Docker-managed Linux filesystem reproduces runner ownership even on Windows.
    & $docker run --rm --volume "${volume}:/data" --entrypoint sh ai-bike-graphhopper:11.1 -c 'chown 1001:1001 /data && chmod 755 /data'
    if ($LASTEXITCODE -ne 0) { throw 'Permission fixture ownership failed' }
    $denied = & $docker run --rm --cap-drop ALL --security-opt no-new-privileges:true --user 0:0 --volume "${volume}:/data" --entrypoint sh ai-bike-graphhopper:11.1 -c 'mkdir /data/graphs' 2>&1
    if ($LASTEXITCODE -eq 0 -or ($denied -join "`n") -notmatch 'Permission denied') { throw 'Expected unprivileged root to fail on a host-owned 755 directory' }
    & $docker run --rm --cap-drop ALL --security-opt no-new-privileges:true --user $config.services.graphhopper.user --volume "${volume}:/data" --entrypoint sh ai-bike-graphhopper:11.1 -c 'mkdir /data/graphs /data/elevation && touch /data/engine.lock && test -r /opt/graphhopper/graphhopper.jar'
    if ($LASTEXITCODE -ne 0) { throw 'Mapped host runtime user must write data and read the engine' }
    Write-Host 'PASS: host UID:GID writes persistent data without root capabilities or world-writable permissions'
} finally {
    [Environment]::SetEnvironmentVariable('GH_USER', $saved, 'Process')
    if ($created) { & $docker volume rm $volume | Out-Null }
}
