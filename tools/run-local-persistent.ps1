#Requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string]$RequestPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$RequestPath = [IO.Path]::GetFullPath($RequestPath)
$directory = Split-Path $RequestPath -Parent
$artifacts = [IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
if ((Split-Path $directory -Parent) -ne $artifacts -or
    (Split-Path $directory -Leaf) -notmatch '^persistent-[a-f0-9]{32}$' -or
    (Split-Path $RequestPath -Leaf) -ne 'request.json') { throw 'Invalid persistent request path.' }
$success = $false
$log = Join-Path $directory 'startup.log'
try {
    $request = Get-Content -LiteralPath $RequestPath -Raw | ConvertFrom-Json -AsHashtable
    if (@($request.Keys | Where-Object { $_ -notin @('RoutingProvider', 'GraphHopperUrl') }).Count -gt 0) { throw 'Invalid persistent request fields.' }
    $env:CYCLING_LOCAL_PERSISTENT_BROKER = '1'
    & (Join-Path $PSScriptRoot 'start-local.ps1') @request *> $log
    $success = $true
} catch {
    $_ | Out-String | Add-Content -LiteralPath $log
} finally {
    $result = Join-Path $directory 'result.json'
    @{ success=$success; launcherPid=$PID; completedUtc=[DateTime]::UtcNow.ToString('o') } |
        ConvertTo-Json | Set-Content -LiteralPath "$result.tmp" -Encoding utf8NoBOM
    Move-Item -LiteralPath "$result.tmp" -Destination $result
}
if (-not $success) { exit 1 }
