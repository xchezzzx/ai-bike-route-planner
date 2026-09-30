#requires -Version 7.6
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ArchivePath,
    [Parameter(Mandatory)][ValidatePattern('^[a-fA-F0-9]{64}$')][string]$ExpectedSha256,
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../src/backend/CyclingRoutes.Infrastructure/Naming/Data')
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$culture = [cultureinfo]::InvariantCulture
$archiveHash = (Get-FileHash -LiteralPath $ArchivePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($archiveHash -cne $ExpectedSha256.ToLowerInvariant()) { throw 'Source ZIP SHA-256 mismatch.' }
$codes = @('PPL', 'PPLA', 'PPLA2', 'PPLA3', 'PPLA4', 'PPLA5', 'PPLC', 'PPLG')
$rows = [Collections.Generic.List[object]]::new()
$ids = [Collections.Generic.HashSet[long]]::new()
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $ArchivePath).Path)
try {
    $entries = @($zip.Entries | Where-Object FullName -CEQ 'IL.txt')
    if ($entries.Count -ne 1) { throw 'Expected exactly one IL.txt entry.' }
    $entry = $entries[0]
    # ZIP timestamps have no reliable timezone: preserve the source wall time.
    $entryTimestamp = $entry.LastWriteTime.ToString('yyyy-MM-ddTHH:mm:ss', $culture)
    $stream = $entry.Open()
    try { $entryHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)).ToLowerInvariant() }
    finally { $stream.Dispose() }
    $parser = [Microsoft.VisualBasic.FileIO.TextFieldParser]::new($entry.Open(), [Text.UTF8Encoding]::new($false, $true))
    try {
        $parser.TextFieldType = [Microsoft.VisualBasic.FileIO.FieldType]::Delimited
        $parser.SetDelimiters("`t")
        $parser.HasFieldsEnclosedInQuotes = $false
        $parser.TrimWhiteSpace = $false
        while (-not $parser.EndOfData) {
            $fields = $parser.ReadFields()
            if ($fields.Count -ne 19) { throw 'Expected 19 GeoNames TSV columns.' }
            if ($fields[6] -cne 'P' -or $fields[8] -cne 'IL' -or $fields[7] -cnotin $codes) { continue }
            if ([string]::IsNullOrWhiteSpace($fields[2]) -or $fields[2] -cnotmatch '[A-Za-z0-9]') { continue }
            if ($fields[2] -match '[^\x20-\x7E]' -or $fields[2].Length -gt 200) { throw 'Invalid GeoNames asciiname.' }
            $id = [long]::Parse($fields[0], [Globalization.NumberStyles]::None, $culture)
            if ($id -le 0 -or -not $ids.Add($id)) { throw 'Invalid or duplicate GeoNames ID.' }
            $latitude = [double]::Parse($fields[4], [Globalization.NumberStyles]::Float, $culture)
            $longitude = [double]::Parse($fields[5], [Globalization.NumberStyles]::Float, $culture)
            if (-not [double]::IsFinite($latitude) -or -not [double]::IsFinite($longitude) -or
                $latitude -lt -90 -or $latitude -gt 90 -or $longitude -lt -180 -or $longitude -gt 180) { throw 'Invalid coordinate.' }
            $modified = [datetime]::ParseExact($fields[18], 'yyyy-MM-dd', $culture).ToString('yyyy-MM-dd', $culture)
            $rows.Add([ordered]@{ id = $id; asciiName = $fields[2]; latitude = $latitude; longitude = $longitude;
                featureCode = $fields[7]; modified = $modified })
        }
    } finally { $parser.Dispose() }
} finally { $zip.Dispose() }
if ($rows.Count -eq 0) { throw 'No settlements survived the filters.' }
$sorted = @($rows | Sort-Object { $_.id })
$data = [ordered]@{ schemaVersion = 1; settlements = $sorted }
$encoding = [Text.UTF8Encoding]::new($false)
$dataText = ($data | ConvertTo-Json -Depth 5 -Compress) + "`n"
$outputHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($encoding.GetBytes($dataText))).ToLowerInvariant()
$manifest = [ordered]@{
    schemaVersion = 1
    importerVersion = 1
    sourceUrl = 'https://download.geonames.org/export/dump/IL.zip'
    sourceReadme = 'https://download.geonames.org/export/dump/readme.txt'
    sourceArchiveSha256 = $archiveHash
    sourceEntry = 'IL.txt'
    sourceEntrySha256 = $entryHash
    sourceEntryTimestamp = $entryTimestamp
    filters = [ordered]@{ countryCode = 'IL'; featureClass = 'P'; featureCodes = $codes; nameColumn = 'asciiname'; populationThreshold = $null }
    sort = 'geonameid ascending (numeric)'
    settlementCount = $rows.Count
    outputSha256 = $outputHash
    attribution = 'Settlement names derived from GeoNames (https://www.geonames.org/), CC BY 4.0 (https://creativecommons.org/licenses/by/4.0/); filtered IL extract.'
}
[IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($OutputDirectory)) | Out-Null
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'settlements.json'), $dataText, $encoding)
[IO.File]::WriteAllText((Join-Path $OutputDirectory 'manifest.json'), (($manifest | ConvertTo-Json -Depth 5).Replace("`r`n", "`n") + "`n"), $encoding)
Write-Output "Imported $($rows.Count) settlements; data SHA-256: $outputHash"
