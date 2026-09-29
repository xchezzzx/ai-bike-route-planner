# Track Segment Display Verification

Date: 2026-09-29. Branch: `oleg/track-segment-display`.
Implementation base: `9078382` (approved design and plan).

## Delivered Behavior

- ORS surface and way-type ranges survive parsing as coalesced geometry-indexed
  segments. Aggregate road-v1 evidence is unchanged.
- Direct generation and both candidate endpoints expose the same additive
  segment metadata. Geometry and exported GPX are unchanged.
- Selected tracks support Surface / Road type patterns, a matching legend,
  pointer and keyboard segment inspection, and EN/RU/HE labels.
- Missing or malformed metadata renders unknown without disabling the route
  or GPX export. Generic paved/concrete is distinct from explicit asphalt.
- A continuous hit area protects endpoint picking when tapping dash gaps.
- Background road styling and route selection/ranking remain unchanged.

## Verification Evidence

- Baseline: 289 backend unit + 349 integration; 116 frontend tests.
- Parser RED: 22 added cases failed on missing segment evidence. GREEN: all 72
  parser cases passed, including independent boundaries and zero-length edges.
- API RED: six direct/candidate/plan cases failed on missing metadata. The first
  implementation exposed an additional A-B evidence gap; the follow-up fix and
  full backend run passed: 289 unit + 377 integration.
- Frontend normalization: 18 added cases, covering invalid complete partitions,
  legacy metadata, coordinate slicing and immutable inputs.
- Rendering/controls: eight added component tests, including map retry, selected
  hit precedence, endpoint preservation, locale controls and camera stability.
- A browser-discovered framing bug received a failing component assertion:
  synchronize map dimensions before fitting new results. The assertion passed
  after the fix; display-mode switching still does not refit.
- Ten new browser scenarios passed on desktop and mobile: all three locales,
  pixel-verified solid/dashed rendering, clicking a white dash gap, keyboard
  selection, exact GPX downloads, legacy/invalid metadata and empty results.
- Inspected desktop surface and Hebrew mobile road-type screenshots: map is
  visible, legend patterns distinguish categories, controls do not overlap and
  attribution is visible. Screenshots remain in ignored Playwright test-results.
- Full regression: `dotnet test src/backend/CyclingRoutes.slnx --no-restore`
  passed 289 unit + 377 integration; `npm test` passed 142 tests; production
  build passed; `npm run test:e2e` passed all 44 desktop/mobile scenarios.
- One concurrent Vitest/Playwright run failed two existing App tests:
  `uses one shape control and restores the inactive destination when returning
  to A-B` timed out at 5s, followed by `omits even malformed hidden destination
  in Prompt mode`. Repeating the entire Vitest suite after browser completion
  passed 142/142 in 40s without code/test timeout changes. Avoid running those
  resource-intensive suites concurrently on this host.
- Independent review and final CI remain pending at this checkpoint.

## Decisions and Limits

- A-B had not requested extras before this work. It now requests surface and
  waytype on its existing ORS request, with the same geometry/search options and
  no additional requests. Malformed evidence is now rejected consistently.
- No live Gemini or ORS calls were made. Automated tests intercept upstream
  requests; map screenshots use a deterministic test-only basemap.
- No theme support or distance/time ranges in this slice; both remain queued.
- No safety/access permission is inferred from cycleway/footway classification.
- Existing Vite warning about MapLibre chunks larger than 500 kB remains.
- Existing CI only runs for main-target PRs. Use a Draft PR targeting main with
  explicit dependency on PR #15 instead of broadening workflow triggers.
- Neither offline feature tests nor green CI satisfies PR #15's outstanding
  Gemini live qualification / route comparison gate. No merge is performed.
