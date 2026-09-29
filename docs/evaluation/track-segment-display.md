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
- Independent review found dash phase restarting across dense evidence intervals
  and a malformed no-match browser fixture. Both were reproduced: each dense-mode
  test saw zero white gap pixels, and the no-match assertion timed out.
- Visual runs now coalesce adjacent identical patterns, while a separate hit-test
  source preserves exact evidence intervals. Dense 200-interval regressions pass
  in both modes on desktop/mobile, including inspection of an original interval.
- The no-match fixture now satisfies the API contract. All three locales verify
  the successful no-match message and absence of errors before checking controls.
- After review fixes, all 144 frontend tests, the production build and all 48
  browser scenarios passed. The fix commit is `647a6a3`; final-head CI is tracked
  on PR #16, independently of these local results.
- Both GitHub CI jobs passed on the reviewed/fixed code head `647a6a3`:
  [backend](https://github.com/xchezzzx/ai-bike-route-planner/actions/runs/36606047552)
  and [frontend](https://github.com/xchezzzx/ai-bike-route-planner/actions/runs/36606047530).
  Current-head status, including documentation-only updates, is tracked on PR #16.

## Review Limits

- No unresolved review findings. The fixture finding was promoted from minor to
  a required fix because the plan explicitly requires successful no-match coverage.
- No real-provider frequency claim follows from the synthetic dense-segment case.
- Repeated retry/intersection stress and large-selector performance have not been
  exhaustively qualified; existing lifecycle/interaction tests remain the coverage.
- The local preview is http://127.0.0.1:61765/ (API proxy health returned Healthy).

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
