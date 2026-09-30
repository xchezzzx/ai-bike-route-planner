# Geographic Track Names

## Scope and Design

Backend, deterministic importer, offline tests, and documentation only. Base:
`main d186bac`, branch `oleg/geographic-track-names`. No frontend, auth,
deployment, commits, pushes, PRs, live routing, or live LLM requests.

The application resolves one canonical name from the actual routed geometry's
first and last positions, requested shape/profile, and provider-reported actual
distance. Never use requested endpoints or target distance for naming.
`GeneratedRoute.Name` travels to JSON `name` and GPX `trk/name`; clients use
`name + ".gpx"` without reconstructing the name.

- Loop: `Place-loop-road-40` (start of actual geometry).
- A-B, distinct settlement IDs: `Place-Other-road-105`.
- A-B, same settlement ID: `Place-road-40` (no loop marker).
- Unknown loop: `Route-loop-road-40`; both unknown A-B: `Route-road-40`.
- One unknown A-B endpoint: `Place-Unknown-road-40` or `Unknown-Place-road-40`.
- Profile labels: `road`, `gravel`; kilometres rounded AwayFromZero, invariant.
- ASCII letters/digits/hyphens only, runs of punctuation become a hyphen;
  decompose accents, no guessed transliterations. Empty labels are unknown.
- Each place token is at most 40 characters; whole name at most 120 characters,
  with profile/distance suffix retained. No uniqueness guarantee across routes.

An application `ISettlementLookup` returns stable IDs, ASCII labels, and optional
attribution. Infrastructure loads a small embedded GeoNames IL subset once.
Lookup uses haversine nearest-point distance <= 10 km; equal distances use lowest
GeoNames ID. This is NOT containment, jurisdiction, or global coverage. Border,
coastal, and overlapping urban areas can be named for a nearby settlement.
Missing data/matches return neutral names without runtime HTTP requests. The
nearest-point scan is bounded by the shipped local subset, not a network timeout.

Dataset: official IL.zip, UTF-8 19-column TSV, country IL, class P, allowlist
PPL/PPLA/PPLA2/PPLA3/PPLA4/PPLA5/PPLC/PPLG. Exclude historical, abandoned,
destroyed, sections/neighborhoods, aggregates, religious and farm features.
Do not use a population threshold (small villages can have zero/unknown values).
Use `asciiname`, not alternate names. Preserve source IDs/coordinates/code/date.
Record source ZIP SHA-256, entry SHA-256, source entry timestamp, importer/schema
version, filters and derived-data hash. Same input ZIP => byte-identical output.
Fresh upstream downloads may differ; reproducibility requires the pinned ZIP.
GeoNames attribution is added to exported GPX metadata when a name uses it;
original provider attribution remains intact and also travels in API attribution.

## Execution Checklist

- [x] Verify clean isolated worktree and inspect generation/GPX/response flow.
- [x] Red: API name/GPX identity and attribution using fake routing providers.
- [x] Red/green: resolver, sanitization, actual endpoints/distance, fallback.
- [x] Red/green: deterministic importer fixtures, filtering and provenance.
- [x] Import official subset; embed it and verify real coordinates/radius/ties.
- [x] Wire DI and narrow result-construction changes in all three services.
- [x] Full backend tests, importer tests, reproducibility and diff/scope checks.

## Verification

Baseline: 316 unit + 377 integration tests passed. Red evidence: all three API
naming cases failed for absent `name`; 28 resolver cases failed against a minimal
placeholder; importer test failed for absent importer; six real lookup cases
failed against an empty lookup. Four invalid-distance cases also failed without
the filename guard. A full run caught a 10 km floating-point boundary defect
(computed 10000.000000000002 m), fixed with a one-micrometre boundary tolerance.

Latest full backend run: `dotnet test src/backend/CyclingRoutes.slnx --verbosity minimal --logger trx`
passed 349 unit + 401 integration = 750 tests, zero failures/skips. Logs are under
the two backend test projects' ignored `TestResults` directories. Tests use fake
HTTP/routing/advisor dependencies; no live Gemini/ORS requests or credentials.
Release verification also passed 349 unit + 401 integration tests with zero
failures/skips using `--configuration Release --verbosity minimal --logger trx`.
Importer fixture suite passed 23 offline assertions on PowerShell 7.6.6 / .NET
10.0.12. Re-importing the cached original ZIP into a separate ignored output
directory reproduced both files byte-for-byte:

- `settlements.json`: `6847f4eb16c5f1089c50549824378e0ec257872397086b5124cce10c2b00b4e5`.
- `manifest.json`: `58e38e43e3de8625e5c582c2a0c598f99452630c2c4ad7e028a254f03e09f7ae`.

`git diff --check` passed. Data `.gitattributes` pins JSON LF despite local
`core.autocrlf=true`. The original ZIP and comparison output are cached only under
ignored `src/backend/CyclingRoutes.Infrastructure/obj/geonames/`, not root `.tmp`.
No staging, commits, pushes, PRs, merges, authentication or deployment changes
were performed. No independent review agent was available; an inline source and
diff review was completed. Device GPX import and post-ranges integration have not
been performed. The parent began disjoint frontend edits in this worktree during
the final backend audit; those files were neither modified nor verified here.

## Integration Handoff

Parent frontend implementation now consumes the canonical API name for route
labels and selected GPX downloads, with a safe legacy fallback for responses
without a valid name. Long labels wrap in EN/RU/HE and RTL layouts. Verification:
159 frontend tests, production build and six desktop/mobile naming browser cases
passed. An independent read-only review of all 38 changed files, including the
1224-record catalog against the cached source ZIP, found no actionable defects.
Review and these test totals precede integration with ranges and geolocation.

Integration after the ranges branch remains the parent task's responsibility.
The three service diffs change only constructor injection (`RouteNameResolver
names`) and final result creation (`GeneratedRoute.Create(..., names)`). Keep the
ranges branch's validation/search changes when combining them. Unit-test service
helpers now require `RouteNamingFixture.NeutralNames`; GPX assertions pass an
explicit expected track name. All generated-route endpoints share the mapper.

`GeneratedRoute.Name` and `GeneratedRouteResponse.Name` are required strings.
`GpxWriter.Write(path, name)` now requires the name explicitly. `GeneratedRoute.Create`
resolves once, preserves provider geometry/metrics/warnings, and appends attribution
on a copy of the path before serializing GPX. Do not re-resolve in frontend.
Update frontend contract/display/download later using JSON `name` as documented
in [the API contract](../api/geographic-track-names.md). Do not infer uniqueness.

No naming decision is currently blocked. Limitations: nearest-point rather than
containment; IL-only source; possible collisions; truncated long labels; mutable
upstream snapshots. All are explicit, tested where behavioral, and documented.

## Changed Files

Application (`src/backend/CyclingRoutes.Application/Routing/`):
`ISettlementLookup.cs`, `RouteNameResolver.cs`, `RoutedPath.cs`, `GpxWriter.cs`,
`RouteGenerationService.cs`, `RouteCandidateService.cs`, `RoutePlanningService.cs`.

API/contracts: `src/backend/CyclingRoutes.Api/Program.cs`,
`src/backend/CyclingRoutes.Api/RoutePlanning/GeneratedRouteResponseMapper.cs`,
`src/backend/CyclingRoutes.Contracts/RoutePlanning/GeneratedRouteResponse.cs`.

Infrastructure: `src/backend/CyclingRoutes.Infrastructure/CyclingRoutes.Infrastructure.csproj`,
`Naming/GeoNamesSettlementLookup.cs`, `Naming/Data/settlements.json`,
`Naming/Data/manifest.json`, `Naming/Data/README.md`, `Naming/Data/.gitattributes`
(the `Naming/` paths are relative to the infrastructure project).

Unit tests (`src/backend/CyclingRoutes.Tests.Unit/RoutePlanning/`):
`RouteNameResolverTests.cs`, `GeneratedRouteNamingTests.cs`, `RouteNamingFixture.cs`,
`GpxWriterTests.cs`, `AdaptiveLoopSearchTests.cs`, `RouteCandidateServiceTests.cs`,
`RoutePlanningServiceTests.cs`.

Integration tests (`src/backend/CyclingRoutes.Tests.Integration/`):
`GeographicTrackNameEndpointTests.cs`, `GeoNamesSettlementLookupTests.cs`,
`RoutePlanEndpointTests.cs`.

Importer: `tools/import-geonames.ps1`, `tools/tests/import-geonames.tests.ps1`.
Docs: this file, `docs/api/geographic-track-names.md`, `docs/api/route-generation.md`.
