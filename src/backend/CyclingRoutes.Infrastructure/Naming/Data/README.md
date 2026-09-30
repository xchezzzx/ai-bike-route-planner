# GeoNames IL Settlement Subset

Settlement names and coordinates are derived from [GeoNames](https://www.geonames.org/),
licensed under [Creative Commons Attribution 4.0](https://creativecommons.org/licenses/by/4.0/).
The data is provided without guarantees of completeness, accuracy or currency.
This application filters and reformats the source; it does not claim GeoNames endorsement.

## Sources and Snapshot

- [Official country dump](https://download.geonames.org/export/dump/IL.zip)
- [Source format and license](https://download.geonames.org/export/dump/readme.txt)
- [Feature code definitions](https://www.geonames.org/export/codes.html)
- Source `IL.txt` ZIP-entry wall timestamp: `2026-09-29T03:53:42` (no timezone implied).
- Archive SHA-256: `fd0e8659b303cd14ccd37353cde2def17e165d617e3bf7ff72f131fa67636443`.
- Entry SHA-256: `5746135e2c735f211951f6e3b4135a4c579a72813b60e5b9ea0f502028fe5658`.
- Derived JSON SHA-256: `6847f4eb16c5f1089c50549824378e0ec257872397086b5124cce10c2b00b4e5`.
- Importer/schema version: `1`; records: `1224`.
- Machine-readable provenance and exact filters: [manifest.json](manifest.json).

The country code is a source-data filter, not an assertion of boundaries or
jurisdiction. This subset is not global. Lookup chooses the nearest settlement
point within 10 km, not the municipality containing a coordinate. Missing matches
use neutral names. A nearby point can be in another urban area or across a border.

## Transformations

Parse the 19-column UTF-8 TSV with .NET `TextFieldParser`, disabling quote handling
(quotes in GeoNames are literal). Keep country `IL`, feature class `P`, and codes
`PPL`, `PPLA`, `PPLA2`, `PPLA3`, `PPLA4`, `PPLA5`, `PPLC`, `PPLG` only. Exclude
historical/abandoned/destroyed places, neighborhood sections and other feature
types. Keep small settlements without a population threshold. Use `asciiname`
without alternate-name selection, translation or invented names. Drop blank or
unusable ASCII labels. Validate coordinates, modification dates and unique IDs.
Preserve source coordinates, feature code and modification date, sort numeric ID
ascending, serialize UTF-8 without BOM and with LF. Runtime filename sanitization
is separate from this source-data transformation.

## Reproduce or Refresh

Run from the repository root with PowerShell 7.6 (.NET 10). This snapshot was
verified with PowerShell 7.6.6 / .NET 10.0.12:

```powershell
pwsh -NoProfile -File tools/tests/import-geonames.tests.ps1
pwsh -NoProfile -File tools/import-geonames.ps1 `
  -ArchivePath src/backend/CyclingRoutes.Infrastructure/obj/geonames/IL.zip `
  -ExpectedSha256 fd0e8659b303cd14ccd37353cde2def17e165d617e3bf7ff72f131fa67636443
```

The original ZIP is retained in the ignored `obj/geonames` cache in the naming
worktree only; it is not distributed in Git. Preserve the pinned ZIP elsewhere
before deleting that cache if future byte-for-byte reproduction is needed.
The official URL is mutable and does not guarantee access to older snapshots.
An archive with a different hash is intentionally rejected by the command above.

To refresh, explicitly download the official ZIP into a local cache, compute its
SHA-256 with `Get-FileHash`, inspect/approve the new snapshot, then supply that hash
to the importer. Review generated changes, update this snapshot section and run
the backend tests. `-OutputDirectory` can target another directory for a comparison
without changing the shipped data. Same ZIP and importer version produce identical
data and manifest bytes; no current date, absolute path or machine identity enters
the output. `.gitattributes` prevents checkout newline conversion of the JSON.

The subset is embedded in the infrastructure assembly, so publish/container output
does not need a loose data file. No runtime download, geocoder call, API key,
account, new NuGet package or paid service is required. GPX metadata and API
attribution credit GeoNames when a geographic label is actually used; provider
attribution is retained verbatim before the additional credit.
