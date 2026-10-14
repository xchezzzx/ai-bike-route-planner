#Requires -Version 7.4
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
function Assert-True($value, [string]$message) { if (-not $value) { throw $message } }
function Assert-Rejected([scriptblock]$action, [string]$expected) {
    $failure = $null
    try { & $action | Out-Null } catch { $failure = $_ }
    Assert-True ($null -ne $failure -and $failure.ToString().Contains($expected)) "Expected rejection: $expected; got: $failure"
}
$tools = Split-Path $PSScriptRoot -Parent
$helper = Join-Path $tools 'local-persistent.ps1'
Assert-True (Test-Path -LiteralPath $helper) 'Persistent launcher helper is missing.'
. (Join-Path $tools 'local-routing.ps1')
. $helper
if (-not $IsWindows) {
    Assert-Rejected { Invoke-PersistentLocalSession -Root (Split-Path $tools -Parent) -RoutingProvider GraphHopper } 'only on Windows'
    Write-Output 'PASS persistent launcher: unsupported platform rejects explicitly.'
    exit 0
}
$temp = Join-Path ([IO.Path]::GetTempPath()) ('persistent local ' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force (Join-Path $temp 'tools'), (Join-Path $temp 'src/frontend/node_modules/vite/bin') | Out-Null
Copy-Item -LiteralPath (Join-Path $tools 'run-local-persistent.ps1') -Destination (Join-Path $temp 'tools')
# The native-broker probe runs a controlled launcher, never the application or a provider.
$fixture = @'
param($RoutingProvider, $GraphHopperUrl)
$root = Split-Path $PSScriptRoot -Parent
$manifest = Join-Path $root 'artifacts/local-servers.json'
@{ routingProvider=$RoutingProvider; launchMode=$env:CYCLING_LOCAL_PERSISTENT_BROKER; frontendUrl='http://127.0.0.1:5173'; backendUrl='http://127.0.0.1:5080'; processes=@() } | ConvertTo-Json | Set-Content -LiteralPath $manifest
@{ provider=$RoutingProvider; url=$GraphHopperUrl; marker=$env:PERSISTENT_TEST_MARKER; ors=$env:Routing__OpenRouteService__ApiKey; gemini=$env:Ai__Gemini__ApiKey } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'observed.json')
'fixture launched'
'@
try {
    [IO.File]::WriteAllText((Join-Path $temp 'tools/start-local.ps1'), $fixture)
    $env:PERSISTENT_TEST_MARKER = 'synthetic-marker'
    $env:Routing__OpenRouteService__ApiKey = 'synthetic-secret'
    $env:Ai__Gemini__ApiKey = 'synthetic-secret'
    Invoke-PersistentLocalSession -Root $temp -RoutingProvider GraphHopper -GraphHopperUrl 'http://127.0.0.1:8989/' -WaitSeconds 30 | Out-Null
    $observed = Get-Content -LiteralPath (Join-Path $temp 'observed.json') -Raw | ConvertFrom-Json
    Assert-True ($observed.provider -ceq 'GraphHopper' -and $observed.url -ceq 'http://127.0.0.1:8989/') 'Native broker lost routing arguments.'
    Assert-True ($observed.marker -ceq 'synthetic-marker' -and [string]::IsNullOrEmpty($observed.ors) -and [string]::IsNullOrEmpty($observed.gemini)) 'Broker lost caller environment or retained external credentials.'
    $requests = Get-ChildItem -LiteralPath (Join-Path $temp 'artifacts') -Recurse -Filter request.json
    foreach ($request in $requests) {
        $saved = Get-Content -LiteralPath $request.FullName -Raw
        Assert-True (-not $saved.Contains('synthetic-secret') -and -not $saved.Contains('synthetic-marker')) 'Request persisted environment or secrets.'
    }
    Write-Output 'PASS native Windows broker: paths with spaces, arguments, sanitized in-memory environment and result handshake.'
    & {
        $calls = [pscustomobject]@{ broker=0 }
        function Invoke-CimMethod { $calls.broker++; return [pscustomobject]@{ ReturnValue=2; ProcessId=0 } }
        function Start-Process { throw 'Unexpected ordinary-launch fallback' }
        Assert-Rejected { Invoke-PersistentLocalSession -Root $temp -RoutingProvider GraphHopper -WaitSeconds 1 } 'broker rejected'
        Assert-True ($calls.broker -eq 1) 'Broker rejection retried.'
        Copy-Item -LiteralPath (Join-Path $tools 'start-local.ps1'), $helper, (Join-Path $tools 'local-routing.ps1') -Destination (Join-Path $temp 'tools')
        Assert-Rejected { & (Join-Path $temp 'tools/start-local.ps1') -Persistent -RoutingProvider GraphHopper } 'broker rejected'
        Assert-Rejected { & (Join-Path $temp 'tools/start-local.ps1') -Persistent -Stop } 'mutually exclusive'
    }
    & {
        function Resolve-LocalExecutable { param($Name); throw 'missing executable fixture' }
        function Invoke-CimMethod { throw 'must not reach broker' }
        Assert-Rejected { Invoke-PersistentLocalSession -Root $temp -RoutingProvider GraphHopper } 'missing executable'
    }
    & {
        function Invoke-CimMethod { throw 'synthetic-secret transport detail' }
        $failure = $null
        try { Invoke-PersistentLocalSession -Root $temp -RoutingProvider GraphHopper | Out-Null } catch { $failure=$_ }
        Assert-True ($null -ne $failure -and -not $failure.ToString().Contains('synthetic-secret') -and $failure.ToString().Contains('broker is unavailable')) 'Broker transport failure leaked detail or was not explicit.'
    }
    $self = Get-Process -Id $PID
    @{ routingProvider='GraphHopper'; frontendUrl='http://127.0.0.1:5173'; backendUrl='http://127.0.0.1:5080';
        processes=@(@{ id=$PID; startTicks=$self.StartTime.ToUniversalTime().Ticks }, @{ id=$PID; startTicks=$self.StartTime.ToUniversalTime().Ticks }) } |
        ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $temp 'artifacts/local-servers.json')
    Assert-Rejected { Invoke-PersistentLocalSession -Root $temp -RoutingProvider GraphHopper -WaitSeconds 30 } 'child failed'
    Write-Output 'PASS ordinary/legacy sessions cannot be silently upgraded by persistent reuse.'
    [IO.File]::WriteAllText((Join-Path $temp 'tools/start-local.ps1'), "throw 'synthetic child failure'")
    Assert-Rejected { Invoke-PersistentLocalSession -Root $temp -RoutingProvider GraphHopper -WaitSeconds 30 } 'child failed'
    Assert-Rejected { Invoke-PersistentLocalSession -Root $temp -RoutingProvider GraphHopper -GraphHopperUrl 'https://synthetic-secret@localhost/' } 'without credentials'
    & {
        function Invoke-CimMethod { return [pscustomobject]@{ ReturnValue=0; ProcessId=0 } }
        Assert-Rejected { Invoke-PersistentLocalSession -Root $temp -RoutingProvider GraphHopper -WaitSeconds 1 } 'may still complete'
    }
    Write-Output 'PASS persistent failures: broker rejection, missing dependency, child error, invalid URL; no fallback.'
} finally {
    # Acknowledgement is atomic but precedes the broker child's process exit.
    foreach ($file in @(Get-ChildItem -LiteralPath (Join-Path $temp 'artifacts') -Recurse -Filter result.json -ErrorAction SilentlyContinue)) {
        $result = Get-Content -LiteralPath $file.FullName -Raw | ConvertFrom-Json
        $child = Get-Process -Id $result.launcherPid -ErrorAction SilentlyContinue
        if ($child -and $child.StartTime.ToUniversalTime() -le ([DateTime]$result.completedUtc).ToUniversalTime()) {
            Assert-True ($child.WaitForExit(10000)) 'Native broker fixture did not exit after acknowledgement.'
        }
    }
    Remove-Item Env:PERSISTENT_TEST_MARKER, Env:Routing__OpenRouteService__ApiKey, Env:Ai__Gemini__ApiKey -ErrorAction SilentlyContinue
    $resolved = [IO.Path]::GetFullPath($temp)
    Assert-True ($resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) 'Unsafe test cleanup.'
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
