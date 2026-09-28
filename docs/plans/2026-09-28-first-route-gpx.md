# First Route and GPX Implementation Plan

> Execution: inline, test-first, following stage 4 of the approved roadmap.

**Goal:** Wire a real routing adapter and GPX export with honest errors and no
synthetic production fallback; keep live verification explicit.
**Spec:** ../api/route-generation.md
**Architecture:** API -> Application orchestration/provider interface -> ORS adapter.
**Stack:** Existing .NET 10 and xUnit; HttpClient, System.Text.Json, LINQ to XML.

## Tasks

- [x] 1. Add failing HTTP generation tests and provider transport tests.
- [x] 2. Add application routing models, IRoutingProvider.GetRoadRouteAsync,
  RouteGenerationService and GPX serialization. Test XML order, escaping,
  culture, optional elevation, antimeridian and cancellation.
- [x] 3. Add Infrastructure/OpenRouteService adapter with strict parsing,
  bounded HTTP responses, status/error mapping and cancellation propagation.
- [x] 4. Add API endpoint/DTOs/configuration and runnable examples; verify full
  suite, Docker missing-key behavior and independent review.
- [x] 5. Verify one actual Israeli route with the user's configured key and
  validate the exported GPX against the official GPX 1.1 schema.
- [ ] 6. Manually inspect route quality and import GPX into a target device/application.

## Constraints and review focus

- PR #6 stays separate. Work is on oleg/first-route-gpx; the user authorized
  committing and opening a PR after verification. Do not merge automatically.
- No new package, account or paid service; secrets never enter source control.
- Do not relabel cycling-regular as gravel or ignore unsupported preferences.
- A-B route is not a distance/time-optimized candidate; always expose the warning.
- Swapped lat/lon, non-finite or missing metrics, provider error bodies, timeout,
  caller cancellation, and missing keys need explicit tests.
- CI uses controlled HTTP fixtures, not live ORS credentials or quota.

## Ledger

- API-stage PR #6 opened against main after domain PR #5 merged.
- Provider choice: ORS GeoJSON for explicit coordinates and metrics. Current
  plans UI requires account context; do not assert an unverified free quota.
- Red: 6 endpoint cases fail with 404 before implementation. Provider/GPX tests
  initially cannot compile because their implementation types do not yet exist.
- Documentation refresh found the 2026 API host migration. Use
  api.heigit.org/openrouteservice/v2 instead of the deprecated ORS host.
- Green: 95 unit and 68 integration cases pass, including the full HTTP flow
  through the real adapter with controlled provider responses. No test uses
  real credentials or consumes external quota.
- The user configured the key in User Secrets. One live Development request
  succeeded: 6187.9 m, estimated 1417.4 s, ascent 43.2 m, descent 52.3 m,
  137 geometry points and 137 GPX track points. Target was 5000 m; the response
  correctly warns targets_not_optimized. No secret was read or printed.
- Generated artifacts are in ignored artifacts/routes/tel-aviv-road-20260928-144041.*.
  The temporary API process was stopped. Manual route-quality and device import
  verification remain open; a successful HTTP request is not a safety assessment.
- The live GPX passes the official Topografix GPX 1.1 XSD (downloaded to ignored
  artifacts/routes/gpx-1.1.xsd for verification).
- Docker routing-smoke image builds successfully. A non-root Production container
  without credentials returns Healthy on /health and 503 routing_not_configured
  on generation. It was stopped and removed after the smoke test.
- Release solution build: zero warnings/errors. PR #6 Backend CI passed; that
  remote CI covers the previous API stage, not these uncommitted routing changes.
- Independent review identified two P2 issues: custom decimal formatting rounded
  some doubles (including longitude just below 180), and an unsupported response
  charset escaped as InvalidOperationException. New regression tests reproduced
  both failures before the fixes. GPX now expands round-trip formatted exponents;
  response decoding failures map to routing_invalid_response.
- Post-fix suite passes: 98 unit plus 69 integration cases, with no build warnings.
- Deferred coverage opportunities from review: actual redirect-network behavior,
  timed body reads and the 8 MiB limit, HTTP 504, and successful 2D provider payloads.
- PR #6 was subsequently merged. The user requested a PR for this stage;
  a fresh pre-commit run passes all 167 tests. Submit against updated main.
- Post-fix verification: Docker image rebuilt with zero warnings/errors and
  a fresh Production smoke test passed (health 200, missing-key generation 503).
  The temporary container was removed. The final CI-style no-build test command
  passes all 167 cases; git diff --check is clean.
