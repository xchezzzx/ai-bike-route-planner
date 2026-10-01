#Requires -Version 7.4
[CmdletBinding()]
param(
    [switch]$Stop,
    [ValidateSet('OpenRouteService', 'GraphHopper')][string]$RoutingProvider = 'OpenRouteService',
    [string]$GraphHopperUrl = 'http://127.0.0.1:8989/'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'local-routing.ps1')
$root = Split-Path $PSScriptRoot -Parent
$statePath = Join-Path $root 'artifacts/local-servers.json'

function Get-OwnedProcess($entry) {
    $process = Get-Process -Id $entry.id -ErrorAction SilentlyContinue
    if ($process -and $process.StartTime.ToUniversalTime().Ticks -eq $entry.startTicks) { return $process }
    return $null
}

function Invoke-LocalSession {
if (-not $Stop) {
    $routingEnvironment = Get-LocalRoutingEnvironment -RoutingProvider $RoutingProvider -GraphHopperUrl $GraphHopperUrl
    $RoutingProvider = $routingEnvironment.Routing__Provider
}
if (Test-Path -LiteralPath $statePath) {
    $state = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    $owned = @($state.processes | ForEach-Object { Get-OwnedProcess $_ } | Where-Object { $null -ne $_ })
    if ($Stop) {
        foreach ($process in $owned) { Stop-Process -InputObject $process }
        Remove-Item -LiteralPath $statePath
        Write-Output 'Stopped this launcher session (unrelated processes were not touched).'
        return
    }
    if ($owned.Count -gt 0) {
        Assert-LocalRoutingSession -State $state -RoutingProvider $RoutingProvider
        Write-Output "Existing local session: $($state.frontendUrl) (API $($state.backendUrl))."
        Write-Output 'Use tools/start-local.ps1 -Stop before starting another session.'
        return
    }
}
if ($Stop) { Write-Output 'No owned local session.'; return }
if ($RoutingProvider -eq 'GraphHopper') {
    Assert-GraphHopperReady -GraphHopperUrl $routingEnvironment.Routing__GraphHopper__BaseUrl
}

function Find-Port([int]$preferred) {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $preferred)
    try { $listener.Start(); return $listener.LocalEndpoint.Port }
    catch {
        $fallback = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
        try { $fallback.Start(); return $fallback.LocalEndpoint.Port } finally { $fallback.Stop() }
    } finally { $listener.Stop() }
}

Push-Location $root
try {
    & dotnet build src/backend/CyclingRoutes.Api --configuration Release --maxcpucount:1 --verbosity minimal
    if ($LASTEXITCODE -ne 0) { throw 'Backend build failed.' }
    if (-not (Test-Path src/frontend/node_modules/vite/bin/vite.js)) {
        & npm --prefix src/frontend ci
        if ($LASTEXITCODE -ne 0) { throw 'Frontend dependency installation failed.' }
    }
} finally { Pop-Location }

$backendUrl = 'http://127.0.0.1:' + (Find-Port 5080)
$frontendPort = Find-Port 5173
$frontendUrl = "http://127.0.0.1:$frontendPort"
$logs = Join-Path $root ('artifacts/local-' + (Get-Date -Format yyyyMMddTHHmmss))
[IO.Directory]::CreateDirectory($logs) | Out-Null
$children = [Collections.Generic.List[Diagnostics.Process]]::new()
$common = @{ PassThru = $true }
if ($IsWindows) { $common.WindowStyle = 'Hidden' }
$committed = $false
try {
    $dll = Join-Path $root 'src/backend/CyclingRoutes.Api/bin/Release/net10.0/CyclingRoutes.Api.dll'
    $backendEnvironment = @{ ASPNETCORE_ENVIRONMENT = 'Development'; DOTNET_ENVIRONMENT = 'Development'; Logging__LogLevel__Default = 'Warning' }
    foreach ($key in $routingEnvironment.Keys) { $backendEnvironment[$key] = $routingEnvironment[$key] }
    $backend = Start-Process @common -FilePath (Resolve-LocalExecutable -Name dotnet) -ArgumentList @(('"' + $dll + '"'), '--urls', $backendUrl) -WorkingDirectory (Join-Path $root 'src/backend/CyclingRoutes.Api') -Environment $backendEnvironment -RedirectStandardOutput (Join-Path $logs 'backend.log') -RedirectStandardError (Join-Path $logs 'backend-error.log')
    $children.Add($backend)
    $healthy = $false
    for ($i = 0; $i -lt 40; $i++) {
        if ($backend.HasExited) { throw 'Backend exited; inspect local logs.' }
        try { if ((Invoke-WebRequest "$backendUrl/health" -TimeoutSec 2).Content -eq 'Healthy') { $healthy = $true; break } } catch { Start-Sleep -Milliseconds 250 }
    }
    if (-not $healthy) { throw 'Backend health check failed.' }
    $vite = Join-Path $root 'src/frontend/node_modules/vite/bin/vite.js'
    $frontend = Start-Process @common -FilePath (Resolve-LocalExecutable -Name node) -ArgumentList @(('"' + $vite + '"'), '--host', '127.0.0.1', '--port', $frontendPort, '--strictPort') -WorkingDirectory (Join-Path $root 'src/frontend') -Environment @{ BACKEND_URL = $backendUrl } -RedirectStandardOutput (Join-Path $logs 'frontend.log') -RedirectStandardError (Join-Path $logs 'frontend-error.log')
    $children.Add($frontend)
    $ready = $false
    for ($i = 0; $i -lt 60; $i++) {
        if ($frontend.HasExited) { throw 'Frontend exited; inspect local logs.' }
        try { if ((Invoke-WebRequest $frontendUrl -TimeoutSec 2).StatusCode -eq 200) { $ready = $true; break } } catch { Start-Sleep -Milliseconds 250 }
    }
    if (-not $ready) { throw 'Frontend did not become ready.' }
    @{ frontendUrl = $frontendUrl; backendUrl = $backendUrl; routingProvider = $RoutingProvider; logDirectory = $logs; processes = @($children | ForEach-Object { @{ id = $_.Id; startTicks = $_.StartTime.ToUniversalTime().Ticks } }) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $statePath -Encoding utf8
    $committed = $true
    Write-Output "Frontend: $frontendUrl"
    Write-Output "API: $backendUrl"
    Write-Output "Routing provider: $RoutingProvider"
    Write-Output "Logs: $logs"
    Write-Output 'Stop with: pwsh -NoProfile -File tools/start-local.ps1 -Stop'
} finally {
    if (-not $committed) {
        foreach ($child in $children) { if (-not $child.HasExited) { $child.Kill($true); $child.WaitForExit() } }
    }
}
}

[IO.Directory]::CreateDirectory((Split-Path $statePath -Parent)) | Out-Null
$lockPath = Join-Path $root 'artifacts/local-servers.lock'
try { $lock = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None) }
catch [IO.IOException] { throw 'Another local start/stop is in progress. Wait for it to finish.' }
try { Invoke-LocalSession } finally { $lock.Dispose() }
