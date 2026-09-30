$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$runner = Join-Path $PSScriptRoot '../import-geonames.ps1'
if (-not (Test-Path -LiteralPath $runner)) { throw 'GeoNames importer is missing.' }
$temp = Join-Path ([IO.Path]::GetTempPath()) ('geonames-tests-' + [guid]::NewGuid())
[IO.Directory]::CreateDirectory($temp) | Out-Null
$checks = 0
function Assert-True($condition, $message) {
    if (-not $condition) { throw $message }
    $script:checks++
}
function Row([int]$id, [string]$code = 'PPL', [string]$country = 'IL', [string]$name = 'Test Place', [string]$latitude = '32.1') {
    @($id, 'Original', $name, '', $latitude, '34.8', 'P', $code, $country, '', '', '', '', '', '0', '', '0', 'Asia/Jerusalem', '2026-09-01') -join "`t"
}
function Archive([string[]]$rows, [string]$name = 'IL.txt') {
    $path = Join-Path $temp ([guid]::NewGuid().ToString() + '.zip')
    $zip = [IO.Compression.ZipFile]::Open($path, [IO.Compression.ZipArchiveMode]::Create)
    try {
        $entry = $zip.CreateEntry($name)
        $entry.LastWriteTime = [DateTimeOffset]::Parse('2026-09-01T00:00:00+00:00')
        $writer = [IO.StreamWriter]::new($entry.Open(), [Text.UTF8Encoding]::new($false))
        try { $writer.Write(($rows -join "`n") + "`n") } finally { $writer.Dispose() }
    } finally { $zip.Dispose() }
    $path
}
function Import([string]$path, [string]$output, [string]$hash = '') {
    if (-not $hash) { $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash }
    & $runner -ArchivePath $path -OutputDirectory $output -ExpectedSha256 $hash | Out-Null
}
$previous = [cultureinfo]::CurrentCulture
try {
    $rows = @((Row 100 'PPLC' 'IL' 'Quoted "Place"'), (Row 2), (Row 3 'PPLA'),
        (Row 4 'PPLA2'), (Row 5 'PPLA3'), (Row 6 'PPLA4'), (Row 7 'PPLA5'), (Row 8 'PPLG'))
    $id = 200
    foreach ($code in @('PPLH', 'PPLQ', 'PPLW', 'PPLX', 'PPLS', 'PPLR', 'STLMT')) { $rows += Row ($id++) $code }
    $rows += Row 300 'PPL' 'US'
    $rows += Row 301 'PPL' 'IL' ''
    $rows += (Row 302).Replace("`tP`tPPL`t", "`tS`tPPL`t")
    $archive = Archive $rows
    $first = Join-Path $temp 'first'
    Import $archive $first
    $data = Get-Content (Join-Path $first 'settlements.json') -Raw | ConvertFrom-Json
    Assert-True ($data.settlements.Count -eq 8) 'Settlement allowlist/country/class/asciiname filters failed.'
    Assert-True (($data.settlements.id -join ',') -eq '2,3,4,5,6,7,8,100') 'IDs must sort numerically, not lexically.'
    Assert-True ($data.settlements[-1].asciiName -ceq 'Quoted "Place"') 'TSV quotes are literal data.'
    Assert-True ($data.settlements[0].latitude -eq 32.1) 'Coordinates must parse invariantly.'
    $manifest = Get-Content (Join-Path $first 'manifest.json') -Raw | ConvertFrom-Json
    Assert-True ($manifest.sourceArchiveSha256 -eq (Get-FileHash $archive).Hash.ToLowerInvariant()) 'Missing source provenance.'
    Assert-True ($manifest.outputSha256 -eq (Get-FileHash (Join-Path $first 'settlements.json')).Hash.ToLowerInvariant()) 'Output hash mismatch.'
    Assert-True ($manifest.attribution -match 'GeoNames.*CC BY 4.0') 'Attribution missing.'
    [cultureinfo]::CurrentCulture = [cultureinfo]::GetCultureInfo('fr-FR')
    $second = Join-Path $temp 'second'
    Import $archive $second
    foreach ($file in @('settlements.json', 'manifest.json')) {
        Assert-True ((Get-FileHash (Join-Path $first $file)).Hash -eq (Get-FileHash (Join-Path $second $file)).Hash) "Non-deterministic $file."
    }
    foreach ($scenario in @('hash', 'columns', 'latitude', 'nan', 'duplicate', 'missing-entry', 'empty')) {
        $bad = switch ($scenario) {
            'columns' { Archive @('broken') }
            'latitude' { Archive @((Row 1 'PPL' 'IL' 'Bad' '91')) }
            'nan' { Archive @((Row 1 'PPL' 'IL' 'Bad' 'NaN')) }
            'duplicate' { Archive @((Row 1), (Row 1)) }
            'missing-entry' { Archive @((Row 1)) 'other.txt' }
            'empty' { Archive @((Row 1 'PPLQ')) }
            default { $archive }
        }
        $output = Join-Path $temp $scenario
        $failed = $false
        try { Import $bad $output $(if ($scenario -eq 'hash') { '0' * 64 } else { '' }) }
        catch { $failed = $true }
        Assert-True $failed "Invalid input accepted: $scenario"
        Assert-True (-not (Test-Path (Join-Path $output 'settlements.json'))) "Failed import wrote data: $scenario"
    }
    Write-Output "Passed: $checks importer assertions (offline)."
} finally {
    [cultureinfo]::CurrentCulture = $previous
    $resolved = [IO.Path]::GetFullPath($temp)
    if (-not $resolved.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
