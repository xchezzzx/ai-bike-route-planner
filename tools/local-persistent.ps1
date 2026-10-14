#Requires -Version 7.4

function Invoke-PersistentLocalSession {
    param(
        [Parameter(Mandatory)][string]$Root,
        [ValidateSet('OpenRouteService', 'GraphHopper')][string]$RoutingProvider,
        [string]$GraphHopperUrl = 'http://127.0.0.1:8989/',
        [ValidateRange(1, 1800)][int]$WaitSeconds = 600
    )
    if (-not $IsWindows) { throw '-Persistent is supported only on Windows; use the ordinary launcher elsewhere.' }
    $routing = Get-LocalRoutingEnvironment -RoutingProvider $RoutingProvider -GraphHopperUrl $GraphHopperUrl
    $pwsh = Resolve-LocalExecutable -Name pwsh
    $null = Resolve-LocalExecutable -Name dotnet
    $null = Resolve-LocalExecutable -Name node
    if (-not (Test-Path -LiteralPath (Join-Path $Root 'src/frontend/node_modules/vite/bin/vite.js'))) {
        $null = Resolve-LocalExecutable -Name npm
    }
    $Root = [IO.Path]::GetFullPath($Root)
    $runner = Join-Path $Root 'tools/run-local-persistent.ps1'
    if (-not (Test-Path -LiteralPath $runner)) { throw 'Persistent runner is missing.' }
    $directory = Join-Path $Root ('artifacts/persistent-' + [guid]::NewGuid().ToString('N'))
    [IO.Directory]::CreateDirectory($directory) | Out-Null
    $request = @{ RoutingProvider = $routing.Routing__Provider }
    if ($request.RoutingProvider -eq 'GraphHopper') { $request.GraphHopperUrl = $routing.Routing__GraphHopper__BaseUrl }
    $requestPath = Join-Path $directory 'request.json'
    $request | ConvertTo-Json | Set-Content -LiteralPath $requestPath -Encoding utf8NoBOM
    # WMI does not inherit the invoking shell's environment. Pass it in memory,
    # with local-provider credential overrides; never serialize it to artifacts.
    $environment = [Environment]::GetEnvironmentVariables()
    foreach ($key in $routing.Keys) { $environment[$key] = $routing[$key] }
    $block = [string[]]@($environment.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" })
    try {
        $startup = New-CimInstance -ClassName Win32_ProcessStartup -ClientOnly -Property @{
            ShowWindow = [uint16]0
            CreateFlags = [uint32]0x01000400 # BREAKAWAY_FROM_JOB | UNICODE_ENVIRONMENT
            EnvironmentVariables = $block
        }
        $commandLine = '"' + $pwsh + '" -NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -File "' + $runner + '" -RequestPath "' + $requestPath + '"'
        $created = Invoke-CimMethod -ClassName Win32_Process -MethodName Create -Arguments @{
            CommandLine = $commandLine; CurrentDirectory = $Root; ProcessStartupInformation = $startup
        }
    } catch { throw 'Windows process broker is unavailable; no fallback was started.' }
    if ($created.ReturnValue -ne 0) { throw "Windows process broker rejected launch (code $($created.ReturnValue)); no fallback was started." }
    $resultPath = Join-Path $directory 'result.json'
    $timer = [Diagnostics.Stopwatch]::StartNew()
    while (-not (Test-Path -LiteralPath $resultPath)) {
        if ($timer.Elapsed.TotalSeconds -ge $WaitSeconds) {
            throw "Persistent startup timed out and may still complete. Inspect $directory/startup.log; use tools/start-local.ps1 -Stop after completion."
        }
        Start-Sleep -Milliseconds 200
    }
    $result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
    if ($result.success -ne $true) { throw "Persistent child failed; inspect $directory/startup.log. No fallback was started." }
    $state = Get-Content -LiteralPath (Join-Path $Root 'artifacts/local-servers.json') -Raw | ConvertFrom-Json
    Write-Output "Frontend: $($state.frontendUrl)"
    Write-Output "API: $($state.backendUrl)"
    Write-Output "Routing provider: $($state.routingProvider)"
    Write-Output "Persistent startup log: $directory/startup.log"
    Write-Output 'Stop with: pwsh -NoProfile -File tools/start-local.ps1 -Stop'
}
