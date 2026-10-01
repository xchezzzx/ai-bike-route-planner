#Requires -Version 7.4

function Resolve-LocalExecutable([string]$Name) {
    return (Get-Command -Name $Name -CommandType Application -ErrorAction Stop | Select-Object -First 1).Source
}

function Get-GraphHopperRootUri([AllowEmptyString()][string]$GraphHopperUrl) {
    $uri = $null
    if (-not [Uri]::TryCreate($GraphHopperUrl, [UriKind]::Absolute, [ref]$uri) -or
        $uri.Scheme -notin @('http', 'https') -or $uri.UserInfo.Length -ne 0 -or
        $uri.Query.Length -ne 0 -or $uri.Fragment.Length -ne 0 -or $uri.AbsolutePath -ne '/') {
        throw 'GraphHopperUrl must be a root HTTP(S) URL without credentials, query or fragment.'
    }
    return $uri
}

function Get-LocalRoutingEnvironment {
    param(
        [ValidateSet('OpenRouteService', 'GraphHopper')][string]$RoutingProvider = 'OpenRouteService',
        [AllowEmptyString()][string]$GraphHopperUrl = 'http://127.0.0.1:8989/'
    )
    $RoutingProvider = if ($RoutingProvider -eq 'GraphHopper') { 'GraphHopper' } else { 'OpenRouteService' }
    $environment = @{ Routing__Provider = $RoutingProvider }
    if ($RoutingProvider -eq 'GraphHopper') {
        $environment.Routing__GraphHopper__BaseUrl = (Get-GraphHopperRootUri $GraphHopperUrl).AbsoluteUri
        $environment.Routing__GraphHopper__Profile = 'road'
        # Empty configuration values override inherited environment and user secrets.
        $environment.Routing__OpenRouteService__ApiKey = ''
        $environment.Ai__Gemini__ApiKey = ''
        $environment.Ai__Gemini__Model = ''
    }
    return $environment
}

function Assert-LocalRoutingSession($State, [string]$RoutingProvider) {
    $provider = if ($State.PSObject.Properties['routingProvider']) { $State.routingProvider } else { 'OpenRouteService' }
    if ($provider -cne $RoutingProvider) {
        throw 'Existing local session uses a different routing provider. Use tools/start-local.ps1 -Stop first.'
    }
}

function Assert-GraphHopperReady([string]$GraphHopperUrl) {
    $uri = Get-GraphHopperRootUri $GraphHopperUrl
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(15)
    $client.MaxResponseContentBufferSize = 8MB
    $response = $null
    try {
        $response = $client.GetAsync([Uri]::new($uri, 'info')).GetAwaiter().GetResult()
        if ($response.StatusCode -ne [Net.HttpStatusCode]::OK) { throw 'Not ready.' }
        $info = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json -AsHashtable
        if ($info -isnot [System.Collections.IDictionary] -or $info.elevation -isnot [bool] -or
            -not $info.elevation -or $info.profiles -isnot [array] -or
            @($info.profiles | Where-Object { $_ -is [System.Collections.IDictionary] -and $_.name -ceq 'road' }).Count -ne 1) {
            throw 'Not ready.'
        }
    } catch {
        # Never include server bodies, URLs or underlying HTTP exception details.
        throw 'GraphHopper readiness failed: /info must report profile road and elevation true (15-second limit, no redirects).'
    } finally {
        if ($response) { $response.Dispose() }
        $client.Dispose()
    }
}
