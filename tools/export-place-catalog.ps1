#requires -Version 7.6
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArchivePath,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../src/frontend/src/data')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$culture = [cultureinfo]::InvariantCulture
$expected = 'fd0e8659b303cd14ccd37353cde2def17e165d617e3bf7ff72f131fa67636443'
if ((Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant() -cne $expected) { throw 'Pinned GeoNames archive mismatch.' }
$source = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot '../src/backend/CyclingRoutes.Infrastructure/Naming/Data/settlements.json') | ConvertFrom-Json
$ids = @{}
foreach ($place in $source.settlements) { $ids[[string]$place.id] = $place }
$rows = [Collections.Generic.List[object]]::new()
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ArchivePath).Path)
try {
    $entry = @($zip.Entries | Where-Object FullName -CEQ 'IL.txt')
    if ($entry.Count -ne 1) { throw 'Expected exactly one IL.txt entry.' }
    $parser = [Microsoft.VisualBasic.FileIO.TextFieldParser]::new($entry[0].Open(), [Text.UTF8Encoding]::new($false, $true))
    try {
        $parser.TextFieldType = [Microsoft.VisualBasic.FileIO.FieldType]::Delimited
        $parser.SetDelimiters("`t"); $parser.HasFieldsEnclosedInQuotes = $false; $parser.TrimWhiteSpace = $false
        while (-not $parser.EndOfData) {
            $fields = $parser.ReadFields()
            if ($fields.Count -ne 19) { throw 'Expected 19 columns.' }
            if (-not $ids.ContainsKey($fields[0])) { continue }
            $place = $ids[$fields[0]]
            if ($place.asciiName -cne $fields[2] -or $place.latitude -ne [double]::Parse($fields[4], $culture) -or $place.longitude -ne [double]::Parse($fields[5], $culture)) { throw 'Naming snapshot does not match source.' }
            # The convenience aliases are not language-tagged; keep source strings, not invented translations.
            $aliases = @(@($fields[1]) + @($fields[3].Split(',')) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and $_.Length -le 200 -and $_ -notmatch '[\x00-\x1F\x7F]' } | Sort-Object -Unique -Culture '' -CaseSensitive)
            $rows.Add([ordered]@{ id = $place.id; name = $place.asciiName; aliases = $aliases; latitude = $place.latitude; longitude = $place.longitude })
        }
    } finally { $parser.Dispose() }
} finally { $zip.Dispose() }
if ($rows.Count -ne $ids.Count) { throw 'Incomplete settlement catalog.' }
$encoding = [Text.UTF8Encoding]::new($false)
$text = (@($rows | Sort-Object { [long]$_.id }) | ConvertTo-Json -Depth 5 -Compress) + "`n"
$hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($encoding.GetBytes($text))).ToLowerInvariant()
$manifest = [ordered]@{ schemaVersion = 1; sourceArchiveSha256 = $expected; settlementCount = $rows.Count; outputSha256 = $hash; sourceUrl = 'https://download.geonames.org/export/dump/IL.zip'; aliases = 'name and comma-separated alternatenames; not language-tagged'; attribution = 'GeoNames, CC BY 4.0; filtered IL snapshot. No completeness or positional accuracy guarantee.' }
[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($OutputDirectory)) | Out-Null
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'places.json'), $text, $encoding)
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'places-manifest.json'), (($manifest | ConvertTo-Json).Replace("`r`n", "`n") + "`n"), $encoding)
Write-Output "Exported $($rows.Count) places; SHA-256: $hash"
