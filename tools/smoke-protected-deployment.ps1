param([string]$Image = 'cycling-routes-protected-smoke', [switch]$SkipBuild)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
$root = Split-Path $PSScriptRoot -Parent
$containers = [System.Collections.Generic.List[string]]::new()
$handler = [System.Net.Http.HttpClientHandler]::new()
$handler.AllowAutoRedirect = $false
$client = [System.Net.Http.HttpClient]::new($handler)
$client.Timeout = [TimeSpan]::FromSeconds(10)
$origin = 'https://testers.example.test'
# Ephemeral smoke-only credential, never a deployment secret or image build input.
$password = [Guid]::NewGuid().ToString('N')
$authorization = 'Basic ' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes("tester:$password"))

function Request([string]$Path, [int]$Expected, [bool]$Authenticated = $false,
    [string]$Method = 'GET', [string]$Body = $null, [string]$Origin = $null, [string]$FetchSite = $null) {
    $request = [System.Net.Http.HttpRequestMessage]::new([System.Net.Http.HttpMethod]::new($Method), "$script:base$Path")
    try {
        if ($Authenticated) { $null = $request.Headers.TryAddWithoutValidation('Authorization', $authorization) }
        if ($Origin) { $null = $request.Headers.TryAddWithoutValidation('Origin', $Origin) }
        if ($FetchSite) { $null = $request.Headers.TryAddWithoutValidation('Sec-Fetch-Site', $FetchSite) }
        if ($Method -eq 'POST') { $request.Content = [System.Net.Http.StringContent]::new($Body, [Text.Encoding]::UTF8, 'application/json') }
        $response = $client.SendAsync($request).GetAwaiter().GetResult()
        try {
            if ([int]$response.StatusCode -ne $Expected) { throw "$Method $Path expected $Expected, received $([int]$response.StatusCode)" }
            if ($response.Headers.Location) { throw 'Unexpected application redirect behind TLS terminator' }
            if ($Expected -eq 401 -and $response.Headers.WwwAuthenticate.ToString() -notmatch '^Basic ') { throw 'Missing Basic challenge' }
            if ($response.Headers.GetValues('Permissions-Policy') -notcontains 'geolocation=(self), camera=(), microphone=()') { throw 'Missing permissions policy' }
            if (-not $response.Headers.CacheControl.NoStore) { throw 'Protected response must not be cached' }
            $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            $codes = @{ 401 = 'access_unauthorized'; 403 = 'access_origin_forbidden'; 429 = 'access_rate_limited' }
            if ($codes.ContainsKey($Expected)) {
                if (($text | ConvertFrom-Json).code -ne $codes[$Expected]) { throw 'Unexpected public access error code' }
            }
            if ($Expected -eq 429 -and $response.Headers.RetryAfter.Delta.TotalSeconds -lt 1) { throw 'Missing Retry-After' }
            return $text
        } finally { $response.Dispose() }
    } finally { $request.Dispose() }
}

try {
    if (-not $SkipBuild) { docker build --progress plain -t $Image -f (Join-Path $root 'Dockerfile') $root }

    $invalid = (docker run -d --cpus 1 --memory 512m -e ASPNETCORE_ENVIRONMENT=Production $Image).Trim()
    $containers.Add($invalid)
    for ($i = 0; $i -lt 30; $i++) {
        $state = docker inspect --format '{{json .State}}' $invalid | ConvertFrom-Json
        if (-not $state.Running) { break }
        Start-Sleep -Seconds 1
    }
    if ($state.Running -or $state.ExitCode -eq 0) { throw 'Unconfigured Production did not reject startup' }
    $logs = docker logs $invalid 2>&1 | Out-String
    if ($logs -notmatch 'OptionsValidationException') { throw 'Container exited for an unexpected reason' }

    # Values are synthetic or empty. No host/provider keys are forwarded.
    $container = (docker run -d --cpus 1 --memory 512m -p '127.0.0.1::8080' `
        -e "Access__Password=$password" -e "Access__PublicOrigin=$origin" -e Access__Mode=Protected `
        -e Ai__Gemini__ApiKey= -e Ai__Gemini__Model= -e Routing__OpenRouteService__ApiKey= $Image).Trim()
    $containers.Add($container)
    $address = (docker port $container 8080/tcp).Trim()
    $script:base = "http://$address"
    $healthy = $false
    for ($i = 0; $i -lt 30; $i++) {
        try {
            $health = Request '/health' 200
            if ($health -eq 'Healthy') { $healthy = $true; break }
        } catch { Start-Sleep -Seconds 1 }
    }
    if (-not $healthy) { throw 'Protected container did not become healthy' }
    $null = Request '/health' 200 -Method HEAD
    $null = Request '/' 401
    $null = Request '/api/route-intents/interpret' 401 -Method POST -Body '{}'
    $html = Request '/' 200 -Authenticated $true
    if ($html -notmatch '<div id="root">') { throw 'SPA HTML is missing' }
    $assets = [regex]::Matches($html, '(?:src|href)="(/assets/[^"?#]+\.(?:js|css))"')
    if ($assets.Count -lt 2) { throw 'Bundled JS/CSS assets are missing' }
    foreach ($asset in $assets) {
        $path = $asset.Groups[1].Value
        $null = Request $path 401
        $content = Request $path 200 -Authenticated $true
        if ($content.Length -lt 20 -or $content -match '<!doctype html>') { throw "Invalid bundled asset: $path" }
    }
    $null = Request '/saved-route' 200 -Authenticated $true
    $body = '{"prompt":"A road loop of 20 km","locale":"en","start":{"latitude":32,"longitude":34}}'
    $null = Request '/api/route-intents/interpret' 403 -Authenticated $true -Method POST -Body $body
    $null = Request '/api/route-intents/interpret' 403 -Authenticated $true -Method POST -Body $body -Origin 'https://evil.example.test'
    $null = Request '/api/route-intents/interpret' 403 -Authenticated $true -Method POST -Body $body -Origin $origin -FetchSite 'cross-site'
    $null = Request '/api/route-intents/interpret' 400 -Authenticated $true -Method POST -Body '{' -Origin $origin
    $problem = Request '/api/route-intents/interpret' 503 -Authenticated $true -Method POST -Body $body -Origin $origin | ConvertFrom-Json
    if ($problem.code -ne 'ai_not_configured') { throw 'Expected honest no-key provider failure' }
    $unknown = Request '/api/unknown' 404 -Authenticated $true
    if ($unknown -match '<!doctype html>') { throw 'Unknown API returned SPA HTML' }
    $null = Request '/api/unknown' 404 -Authenticated $true -Method POST -Body '{}' -Origin $origin
    $null = Request '/API/unknown.json' 404 -Authenticated $true
    $null = Request '/api/unknown' 429 -Authenticated $true
    Write-Host 'Protected full-app Docker smoke passed; no provider keys or live provider calls.'
} finally {
    $client.Dispose()
    foreach ($id in $containers) { docker rm --force $id | Out-Null }
}
