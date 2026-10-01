#Requires -Version 7.4
param([switch]$EnvironmentProbe, [switch]$ConfigurationOnly)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$credentialKeys = @('Routing__OpenRouteService__ApiKey', 'Ai__Gemini__ApiKey', 'Ai__Gemini__Model')
if ($EnvironmentProbe) {
    $values = [Environment]::GetEnvironmentVariables()
    $result = @{}
    foreach ($key in $credentialKeys) { $result[$key] = @{ present = $values.Contains($key); value = $values[$key] } }
    $result | ConvertTo-Json -Depth 4 -Compress
    exit 0
}

function Assert-True($value, [string]$message) { if (-not $value) { throw $message } }
function Assert-Rejected([scriptblock]$action, [string]$message) {
    $caught = $null
    try { & $action } catch { $caught = $_ }
    Assert-True ($null -ne $caught) $message
    Assert-True (-not $caught.ToString().Contains('sensitive-marker')) 'Sensitive URL or response leaked.'
}

$helper = Join-Path $PSScriptRoot 'local-routing.ps1'
Assert-True (Test-Path -LiteralPath $helper) 'Routing helpers are missing: launcher cannot select a safe local provider.'
. $helper

function Test-ChildConfiguration([string]$temp, [string[]]$launcherArguments = @()) {
    $probe = Join-Path $temp 'configuration-probe'
    [IO.Directory]::CreateDirectory($probe) | Out-Null
    $id = 'local-routing-probe-' + [guid]::NewGuid()
    $appData = Join-Path $probe 'appdata'
    $secrets = Join-Path $appData "Microsoft/UserSecrets/$id"
    [IO.Directory]::CreateDirectory($secrets) | Out-Null
    [IO.File]::WriteAllText((Join-Path $secrets 'secrets.json'), '{"Routing:OpenRouteService:ApiKey":"synthetic-secret","Ai:Gemini:ApiKey":"synthetic-secret","Ai:Gemini:Model":"synthetic-secret"}')
    [IO.File]::WriteAllText((Join-Path $probe 'Probe.csproj'), '<Project Sdk="Microsoft.NET.Sdk.Web"><PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType></PropertyGroup></Project>')
    $source = @'
using System;
using System.Linq;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration.UserSecrets;
[assembly: UserSecretsId("__ID__")]
var keys = new[] { "Routing:OpenRouteService:ApiKey", "Ai:Gemini:ApiKey", "Ai:Gemini:Model" };
var drop = args.Contains("--probe-drop-environment");
if (drop) foreach (var key in keys) Environment.SetEnvironmentVariable(key.Replace(":", "__"), null);
var builder = WebApplication.CreateBuilder(args.Where(a => a != "--probe-drop-environment").ToArray());
Console.WriteLine(JsonSerializer.Serialize(keys.ToDictionary(key => key, key => new {
    empty = builder.Configuration[key] == "",
    secret = builder.Configuration[key] == "synthetic-secret",
    environmentPresent = Environment.GetEnvironmentVariables().Contains(key.Replace(":", "__")),
    environmentEmpty = Environment.GetEnvironmentVariable(key.Replace(":", "__")) == ""
})));
'@
    [IO.File]::WriteAllText((Join-Path $probe 'Program.cs'), $source.Replace('__ID__', $id))
    $dotnet = (Get-Command dotnet -CommandType Application).Source
    & $dotnet build (Join-Path $probe 'Probe.csproj') --configuration Release --verbosity quiet
    Assert-True ($LASTEXITCODE -eq 0) '.NET configuration probe build failed.'
    $dll = Join-Path $probe 'bin/Release/net10.0/Probe.dll'
    foreach ($mode in @('secretsBaseline', 'emptyEnvironment', 'droppedEnvironment')) {
        $environment = @{ APPDATA = $appData; DOTNET_ENVIRONMENT = 'Development'; ASPNETCORE_ENVIRONMENT = 'Development' }
        $arguments = @(('"' + $dll + '"'))
        if ($mode -eq 'secretsBaseline') {
            $arguments += '--probe-drop-environment'
        } else {
            $local = Get-LocalRoutingEnvironment -RoutingProvider GraphHopper
            foreach ($key in $local.Keys) { $environment[$key] = $local[$key] }
            $arguments += $launcherArguments
            if ($mode -eq 'droppedEnvironment') { $arguments += '--probe-drop-environment' }
        }
        $parameters = @{
            FilePath = $dotnet; ArgumentList = $arguments; Environment = $environment
            WorkingDirectory = $probe; PassThru = $true
            RedirectStandardOutput = (Join-Path $probe "$mode.json")
            RedirectStandardError = (Join-Path $probe "$mode-error.log")
        }
        if ($IsWindows) { $parameters.WindowStyle = 'Hidden' }
        $child = Microsoft.PowerShell.Management\Start-Process @parameters
        try {
            if (-not $child.WaitForExit(15000)) { $child.Kill($true); $child.WaitForExit(); throw 'Configuration probe timed out.' }
            Assert-True ($child.ExitCode -eq 0) '.NET configuration probe failed.'
            $observed = Get-Content -LiteralPath $parameters.RedirectStandardOutput -Raw | ConvertFrom-Json -AsHashtable
            foreach ($key in @('Routing:OpenRouteService:ApiKey', 'Ai:Gemini:ApiKey', 'Ai:Gemini:Model')) {
                if ($mode -in @('secretsBaseline', 'droppedEnvironment')) {
                    Assert-True ($observed[$key].secret -and -not $observed[$key].environmentPresent) "Absent-variable control did not restore User Secrets for $key."
                } else {
                    Assert-True ($observed[$key].empty -and $observed[$key].environmentPresent -and $observed[$key].environmentEmpty) "Real .NET child did not retain an explicit empty override for $key."
                }
            }
            Write-Output "PASS .NET child configuration: $mode"
        } finally { $child.Dispose() }
    }
}

if ($ConfigurationOnly) {
    $temp = Join-Path ([IO.Path]::GetTempPath()) ('local-routing-config-test-' + [guid]::NewGuid())
    [IO.Directory]::CreateDirectory($temp) | Out-Null
    try { Test-ChildConfiguration -temp $temp } finally { Remove-Item -LiteralPath $temp -Recurse -Force }
    exit 0
}

$default = Get-LocalRoutingEnvironment
Assert-True ($default.Count -eq 1 -and $default.Routing__Provider -ceq 'OpenRouteService') 'Default must select ORS without changing its credentials or AI settings.'
$local = Get-LocalRoutingEnvironment -RoutingProvider GraphHopper
Assert-True ($local.Routing__Provider -ceq 'GraphHopper') 'Local provider not selected.'
Assert-True ($local.Routing__GraphHopper__BaseUrl -ceq 'http://127.0.0.1:8989/') 'Wrong local root URL.'
Assert-True ($local.Routing__GraphHopper__Profile -ceq 'road') 'Wrong local profile.'
Assert-True ((Get-LocalRoutingEnvironment -RoutingProvider graphhopper).Routing__Provider -ceq 'GraphHopper') 'Case-insensitive PowerShell selection must produce a canonical backend provider.'
foreach ($key in @('Routing__OpenRouteService__ApiKey', 'Ai__Gemini__ApiKey', 'Ai__Gemini__Model')) {
    Assert-True ($local.ContainsKey($key) -and $local[$key] -ceq '') "Local environment must explicitly empty $key."
}
Assert-True ((Get-LocalRoutingEnvironment -RoutingProvider GraphHopper -GraphHopperUrl 'http://localhost:9000').Routing__GraphHopper__BaseUrl -ceq 'http://localhost:9000/') 'Root URL without slash was not normalized.'
foreach ($url in @('', 'sensitive-marker', 'ftp://sensitive-marker/', 'http://sensitive-marker:password@localhost/', 'http://localhost/route', 'http://localhost/?sensitive-marker', 'http://localhost/#sensitive-marker', 'http://localhost/?', 'http://localhost/#')) {
    Assert-Rejected { Get-LocalRoutingEnvironment -RoutingProvider GraphHopper -GraphHopperUrl $url } 'Invalid root URL accepted.'
}
Assert-Rejected { Get-LocalRoutingEnvironment -RoutingProvider Unknown } 'Unknown provider accepted.'
# An irrelevant GH URL must not change the default ORS workflow.
Assert-True ((Get-LocalRoutingEnvironment -GraphHopperUrl 'sensitive-marker').Routing__Provider -ceq 'OpenRouteService') 'ORS unexpectedly validates GH configuration.'

Assert-LocalRoutingSession -State ([pscustomobject]@{}) -RoutingProvider OpenRouteService
Assert-LocalRoutingSession -State ([pscustomobject]@{ routingProvider = 'GraphHopper' }) -RoutingProvider GraphHopper
Assert-Rejected { Assert-LocalRoutingSession -State ([pscustomobject]@{}) -RoutingProvider GraphHopper } 'Legacy ORS session mislabeled GH.'
Assert-Rejected { Assert-LocalRoutingSession -State ([pscustomobject]@{ routingProvider = 'OpenRouteService' }) -RoutingProvider GraphHopper } 'Existing ORS session reused as GH.'
Assert-Rejected { Assert-LocalRoutingSession -State ([pscustomobject]@{ routingProvider = 'GraphHopper' }) -RoutingProvider OpenRouteService } 'Existing GH session reused as ORS.'

# Real loopback HTTP fixtures: no app, engine, or quota API is contacted.
foreach ($scenario in @('ready', 'wrongProfile', 'noElevation', 'stringElevation', 'missingProfiles', 'stringProfiles', 'invalidJson', 'providerError', 'redirect', 'slowHeaders', 'slowBody')) {
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $job = Start-ThreadJob -ArgumentList $listener, $scenario, $port -ScriptBlock {
        param($listener, $scenario, $port)
        $connection = $listener.AcceptTcpClient()
        try {
            $stream = $connection.GetStream()
            $stream.ReadTimeout = 5000
            $header = ''
            while (-not $header.EndsWith("`r`n`r`n")) {
                $b = $stream.ReadByte()
                if ($b -lt 0 -or $header.Length -gt 16384) { throw 'Invalid fixture request.' }
                $header += [char]$b
            }
            if ($scenario -eq 'slowHeaders') { Start-Sleep -Seconds 18; return @{ header = $header; redirected = $false } }
            $body = switch ($scenario) {
                'wrongProfile' { '{"profiles":[{"name":"car"}],"elevation":true}' }
                'noElevation' { '{"profiles":[{"name":"road"}],"elevation":false}' }
                'stringElevation' { '{"profiles":[{"name":"road"}],"elevation":"true"}' }
                'missingProfiles' { '{"elevation":true}' }
                'stringProfiles' { '{"profiles":["road"],"elevation":true}' }
                'invalidJson' { 'sensitive-marker' }
                'providerError' { '{"message":"sensitive-marker"}' }
                default { '{"profiles":[{"name":"road"}],"elevation":true}' }
            }
            $status = switch ($scenario) { 'providerError' { 503 }; 'redirect' { 302 }; default { 200 } }
            $bytes = [Text.Encoding]::UTF8.GetBytes($body)
            $redirect = if ($scenario -eq 'redirect') { "Location: http://127.0.0.1:$port/sensitive-marker`r`n" } else { '' }
            $head = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 $status Test`r`n${redirect}Content-Type: application/json`r`nContent-Length: $($bytes.Length)`r`nConnection: close`r`n`r`n")
            $stream.Write($head)
            if ($scenario -eq 'slowBody') { Start-Sleep -Seconds 18; return @{ header = $header; redirected = $false } }
            $stream.Write($bytes)
            $stream.Flush()
            if ($scenario -eq 'redirect') { Start-Sleep -Milliseconds 500 }
            @{ header = $header; redirected = $listener.Pending() }
        } finally { $connection.Dispose() }
    }
    try {
        $watch = [Diagnostics.Stopwatch]::StartNew()
        if ($scenario -eq 'ready') { Assert-GraphHopperReady -GraphHopperUrl "http://127.0.0.1:$port/" }
        else { Assert-Rejected { Assert-GraphHopperReady -GraphHopperUrl "http://127.0.0.1:$port/" } "Readiness accepted $scenario." }
        if ($scenario -in @('slowHeaders', 'slowBody')) {
            Assert-True ($watch.Elapsed.TotalSeconds -ge 14 -and $watch.Elapsed.TotalSeconds -lt 17) "15-second bound not enforced for $scenario."
        }
        Assert-True ($null -ne (Wait-Job $job -Timeout 5)) 'Fixture did not finish.'
        $observed = Receive-Job $job
        Assert-True ($observed.header.StartsWith('GET /info HTTP/1.1')) 'Readiness did not request /info.'
        Assert-True (-not $observed.redirected) 'Readiness followed a redirect or retried.'
        Write-Output "PASS readiness: $scenario"
    } finally {
        $listener.Stop()
        Stop-Job $job -ErrorAction SilentlyContinue
        Remove-Job $job -Force
    }
}
Write-Output 'PASS local routing: defaults, explicit empty credentials, URL validation, session reuse, HTTP readiness and 15-second bounds.'

$temp = Join-Path ([IO.Path]::GetTempPath()) ('local-routing-test-' + [guid]::NewGuid())
[IO.Directory]::CreateDirectory($temp) | Out-Null
$original = @{}
$occupied = [Collections.Generic.List[Net.Sockets.TcpListener]]::new()
try {
    foreach ($key in $credentialKeys) {
        $original[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
        [Environment]::SetEnvironmentVariable($key, 'sensitive-marker', 'Process')
    }
    foreach ($provider in @('OpenRouteService', 'GraphHopper')) {
        $output = Join-Path $temp "$provider.json"
        $parameters = @{
            FilePath = (Get-Command pwsh).Source
            ArgumentList = @('-NoProfile', '-File', ('"' + $PSCommandPath + '"'), '-EnvironmentProbe')
            Environment = (Get-LocalRoutingEnvironment -RoutingProvider $provider)
            RedirectStandardOutput = $output
            RedirectStandardError = (Join-Path $temp "$provider-error.log")
            PassThru = $true
        }
        if ($IsWindows) { $parameters.WindowStyle = 'Hidden' }
        $child = Start-Process @parameters
        $child.WaitForExit()
        Assert-True ($child.ExitCode -eq 0) 'Environment probe failed.'
        $observed = Get-Content -LiteralPath $output -Raw | ConvertFrom-Json -AsHashtable
        foreach ($key in $credentialKeys) {
            $expected = if ($provider -eq 'GraphHopper') { '' } else { 'sensitive-marker' }
            Assert-True ($observed[$key].present -and $observed[$key].value -ceq $expected) "Child credential override failed for $provider/$key."
            Assert-True ([Environment]::GetEnvironmentVariable($key, 'Process') -ceq 'sensitive-marker') 'Parent environment was modified.'
        }
        $child.Dispose()
    }
    Write-Output 'PASS real child environment: local credentials empty, ORS inheritance and parent environment preserved.'

    # Execute the actual launcher in a disposable root; stub only app/build boundaries.
    $tools = Join-Path $temp 'tools'
    [IO.Directory]::CreateDirectory($tools) | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'start-local.ps1'), $helper -Destination $tools
    $vite = Join-Path $temp 'src/frontend/node_modules/vite/bin'
    [IO.Directory]::CreateDirectory($vite) | Out-Null
    [IO.File]::WriteAllText((Join-Path $vite 'vite.js'), '')
    $launcher = Join-Path $tools 'start-local.ps1'
    $manifest = Join-Path $temp 'artifacts/local-servers.json'
    $capture = [pscustomobject]@{ starts = [Collections.Generic.List[hashtable]]::new(); arguments = [Collections.Generic.List[object]]::new(); stops = 0; builds = 0 }
    function dotnet { $capture.builds++; $global:LASTEXITCODE = 0 }
    function Start-Process {
        param($Environment, $ArgumentList)
        $capture.starts.Add($Environment)
        $capture.arguments.Add($ArgumentList)
        return Get-Process -Id $PID
    }
    function Invoke-WebRequest { param($Uri) return [pscustomobject]@{ Content = 'Healthy'; StatusCode = 200 } }
    function Stop-Process { param($InputObject) $capture.stops++ }
    foreach ($port in @(5080, 5173)) {
        $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $port)
        try { $listener.Start(); $occupied.Add($listener) } catch { $listener.Stop() }
    }
    & $launcher | Out-Null
    Assert-True ($capture.starts.Count -eq 2 -and $capture.starts[0].Routing__Provider -ceq 'OpenRouteService') 'Launcher default environment changed.'
    $state = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
    Assert-True ($state.routingProvider -ceq 'OpenRouteService' -and $state.processes[0].startTicks -eq (Get-Process -Id $PID).StartTime.ToUniversalTime().Ticks) 'Manifest lost provider or process ownership.'
    Assert-True (([Uri]$state.backendUrl).Port -ne 5080 -and ([Uri]$state.frontendUrl).Port -ne 5173) 'Busy-port fallback failed.'
    Assert-True (-not (Get-Content -LiteralPath $manifest -Raw).Contains('sensitive-marker')) 'Manifest leaked credentials.'
    & $launcher | Out-Null
    Assert-True ($capture.starts.Count -eq 2) 'Matching session unnecessarily restarted.'
    Assert-Rejected { & $launcher -RoutingProvider GraphHopper } 'Launcher reused ORS session as GH.'
    Assert-True ($capture.starts.Count -eq 2) 'Mismatch started new processes.'
    # Legacy manifests are ORS, and reused process IDs with different start ticks are unrelated.
    $state.PSObject.Properties.Remove('routingProvider')
    $state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifest
    Assert-Rejected { & $launcher -RoutingProvider GraphHopper } 'Launcher mislabeled legacy session.'
    & $launcher -Stop | Out-Null
    Assert-True ($capture.stops -eq 2 -and -not (Test-Path -LiteralPath $manifest)) 'Owned stop failed.'
    $state.processes[0].startTicks--
    $state.processes = @($state.processes[0])
    $state | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifest
    & $launcher -Stop | Out-Null
    Assert-True ($capture.stops -eq 2) 'Stop touched a reused/unrelated process ID.'
    $lock = [IO.File]::Open((Join-Path $temp 'artifacts/local-servers.lock'), [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
    try { Assert-Rejected { & $launcher } 'Concurrent session lock ignored.' } finally { $lock.Dispose() }
    foreach ($ready in @($false, $true)) {
        $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
        $listener.Start()
        $port = $listener.LocalEndpoint.Port
        $job = Start-ThreadJob -ArgumentList $listener, $ready -ScriptBlock {
            param($listener, $ready)
            $connection = $listener.AcceptTcpClient()
            try {
                $stream = $connection.GetStream()
                $stream.ReadTimeout = 5000
                $header = ''
                while (-not $header.EndsWith("`r`n`r`n")) { $b = $stream.ReadByte(); if ($b -lt 0) { throw 'EOF' }; $header += [char]$b }
                $body = if ($ready) { '{"profiles":[{"name":"road"}],"elevation":true}' } else { '{"profiles":[{"name":"road"}],"elevation":false}' }
                $bytes = [Text.Encoding]::UTF8.GetBytes($body)
                $head = [Text.Encoding]::ASCII.GetBytes("HTTP/1.1 200 OK`r`nContent-Length: $($bytes.Length)`r`nConnection: close`r`n`r`n")
                $stream.Write($head); $stream.Write($bytes); $stream.Flush()
                $header
            } finally { $connection.Dispose() }
        }
        try {
            if (-not $ready) {
                Assert-Rejected { & $launcher -RoutingProvider GraphHopper -GraphHopperUrl "http://127.0.0.1:$port/" } 'Launcher started before GH readiness.'
                Assert-True ($capture.starts.Count -eq 2 -and $capture.builds -eq 1 -and -not (Test-Path -LiteralPath $manifest)) 'Readiness failure caused startup side effects.'
            } else {
                & $launcher -RoutingProvider graphhopper -GraphHopperUrl "http://127.0.0.1:$port/" | Out-Null
                Assert-True ($capture.starts.Count -eq 4) 'Ready GH did not start backend/frontend.'
                $environment = $capture.starts[2]
                Assert-True ($environment.Routing__Provider -ceq 'GraphHopper' -and $environment.Routing__GraphHopper__BaseUrl -ceq "http://127.0.0.1:$port/" -and $environment.Routing__GraphHopper__Profile -ceq 'road') 'Launcher did not pass GH configuration to backend child.'
                foreach ($key in $credentialKeys) { Assert-True ($environment.ContainsKey($key) -and $environment[$key] -ceq '') 'Launcher omitted empty external credentials.' }
                $state = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
                Assert-True ($state.routingProvider -ceq 'GraphHopper') 'GH session not identified in manifest.'
                Assert-Rejected { & $launcher } 'GH session reused as default ORS.'
                & $launcher -Stop -GraphHopperUrl 'sensitive-marker' | Out-Null
                Assert-True ($capture.stops -eq 4) 'GH stop failed or unnecessarily validated URL.'
            }
            Assert-True ($null -ne (Wait-Job $job -Timeout 5)) 'Launcher readiness fixture did not finish.'
            Assert-True ((Receive-Job $job).StartsWith('GET /info HTTP/1.1')) 'Launcher skipped /info readiness.'
        } finally { $listener.Stop(); Stop-Job $job -ErrorAction SilentlyContinue; Remove-Job $job -Force }
    }
    Write-Output 'PASS launcher: GH readiness/configuration, ORS default, manifest, reuse mismatch, legacy state, process ownership, port fallback and exclusive lock.'
    Test-ChildConfiguration -temp $temp -launcherArguments @($capture.arguments[2] | Select-Object -Skip 1)
} finally {
    foreach ($listener in $occupied) { $listener.Stop() }
    foreach ($key in $original.Keys) { [Environment]::SetEnvironmentVariable($key, $original[$key], 'Process') }
    foreach ($name in @('dotnet', 'Start-Process', 'Invoke-WebRequest', 'Stop-Process')) { Remove-Item -LiteralPath "Function:$name" -ErrorAction SilentlyContinue }
    $resolved = [IO.Path]::GetFullPath($temp)
    Assert-True ($resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) 'Unsafe temporary cleanup path.'
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
