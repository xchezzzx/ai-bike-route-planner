# MVP roadmap

This is the staged direction agreed in the project conversation. Backend:
C#/.NET 10 modular monolith. Frontend: React + TypeScript, web only. Initial
service area: Israel. Languages: English, Hebrew (RTL), and Russian.

## Current local MVP priority (2026-10-07)

GraphHopper 11.1 integration is delivered in PR #27, linked elevation inspection
and the compact selector in PR #26, and soft near-return ranking in PR #28.
Dot's bounded elevation-input stabilization is being integrated; these features
do not need reimplementation. A geometric warning does not certify rideability.

Use local GraphHopper, Manual/Road and AI refinement off. Follow the
[post-dot plan](superpowers/plans/2026-10-06-post-dot-route-quality.md): stabilize
inspection, consolidate launch/runtime, freeze route-quality controls, propagate
elevation preference into construction, then evaluate native turn-aware loops.
Keep exact targets, surface/access gates and the existing call/deadline budgets.

The [local acceptance checklist](development/local-mvp-acceptance.md) separates
automated fixtures from owner road/device/phone checks. ORS/Gemini live
qualification and cloud hosting remain separate; no host is selected.

## First priority: local GraphHopper route-quality evaluation (2026-10-01)

The user selected a self-hosted GraphHopper engine and local testing as the
first implementation priority, ahead of other feature additions and cloud
deployment. The integration was subsequently delivered in PR #27, not proof
of improved route quality. The original decision and its acceptance criteria
below remain useful for the ongoing quality evaluation.

- Run a pinned GraphHopper release in a separate local Docker service; retain
  the C# modular monolith and the existing provider boundary.
- Prepare a regional OpenStreetMap graph and a configurable road-bike profile.
  Persist and version the generated graph separately from source code; do not
  rebuild it on every normal server start or commit large datasets to Git.
- Write and review the integration design and implementation plan before
  product-code changes, dependencies or engine setup.
- First compare road selection through fixed control points; then assess loop
  generation, distance-range matching, retracing, unnecessary maneuvers, known
  surface coverage and unknown data. Do not infer road safety from road class.
- Use local requests and existing saved evidence for initial evaluation.
  Self-hosted routing needs no commercial GraphHopper API account or key.
  No new live ORS/Gemini calls are authorized by this priority change; retain
  ORS as a baseline without silently calling it as a fallback.
- Record import time, graph size, peak memory, startup/readiness time and route
  latency before choosing resource settings or recommending cloud hosting.
- Azure Container Apps is a possible later destination, not a selected or
  provisioned deployment. Cloud setup, billing and credentials remain deferred
  to a separate joint task.

## Current release acceptance (2026-09-30)

The user approved merging PRs #15-#17 after green CI, including adaptive loop
length calibration, even without proven live quality improvement. Manual testing
will follow completion of the current work. This supersedes the historical merge
holds in the milestone record below; it does not mark negative live results as
passes. The latest v2 advisor corpus passed 6/6, while only Tel Aviv retained
matching routes in the four-city comparison. Calibration was subsequently tested
offline, not live. AI refinement stays off by default. See the
[evidence and accepted limitations](evaluation/route-refinement-diagnostics-2026-09-29.md).

Geolocation shipped in PR #18, explicit distance/time ranges in #19 and geographic
track names in #20, each after green CI. Protected same-origin deployment
preparation now includes a full-app container, tester access, shared rate limits,
no-key Docker CI and a Render blueprint with checks-gated deploys. This is not an
actual cloud deployment: hosting account linkage, secret entry and public HTTPS
acceptance remain owner steps. See the [deployment runbook](deployment/protected-staging.md).

Cloud deployment is deferred by the user on 2026-09-30 and will be handled
together in a separate task. No hosting provider is selected; the Render
blueprint is preparation, not a hosting decision.

Still outstanding: real-road/GPX/device acceptance and public hosting verification.
Extraction v4 and advisor input v3 also require fresh live qualification; the
historical 34/34 and 6/6 live results below apply only to earlier contracts. See the
[remaining MVP delivery plan](plans/2026-09-29-mvp-completion.md).

## Milestone history

1. Repository and CI baseline: solution references, GitHub Actions, protected
   main, pull-request workflow, /health integration test. Completed previously.
2. Route planning domain (issue #4): GeoCoordinate, Distance, profile/shape/
   elevation enums, RouteIntent, invariant tests and documentation. Implemented
   and tested; merged through PR #5.
3. Application/API boundary: explicit request DTOs with units, mapping into the
   domain, validation errors as ProblemDetails, request cancellation and tests.
   Implemented, tested and merged through PR #6.
   See [API contract](api/route-intent-validation.md). Incomplete AI
   extraction remains future work and must stay distinct from a valid RouteIntent.
4. First real route: provider interface and one adapter; route geometry,
   metrics and GPX export. Verify actual road/gravel quality on known Israeli
   routes. Start with a reproducible request before adding natural-language input.
   Point-to-point road adapter and GPX are implemented and merged through PR #7, with a successful
   live Tel Aviv request. Manual route-quality/device checks remain; gravel is
   explicitly unsupported in this first adapter. See [generation contract](api/route-generation.md).
5. Candidate generation and ranking: loops, distance/time/elevation preferences,
   explainable trade-offs, provider limitations and infeasible-request handling.
   Implemented and merged through PR #8 with bounded three-seed ORS search, pure ranking, exact
   deduplication and partial results. One live Tel Aviv search returned three
   closed loops within distance tolerance; all GPX files passed schema validation.
   See [candidate contract](api/route-candidates.md). Local verification passes
   275 tests after review fixes; Docker smoke and live GPX validation passed during
   initial implementation. A repeated independent Application review completed.
   Manual road/device checks remain separate from automated verification.
6. Prompt interpretation: RU/EN/HE structured extraction, clarification for
   missing parameters, strict validation, test prompts and provider abstraction.
   Stage 6a is implemented and merged through PR #9: Gemini adapter, bounded HTTP
   endpoint, deterministic clarification policy and a 27-case evaluation corpus.
   Offline tests pass. After an initial quota-limited evaluation, a paced live
   run attempted all 25 cases: 15 passed, nine received upstream HTTP 503, and
   one injection case was falsely rejected as unsupported. No 429 in that run.
   Those initial runs did not complete live qualification; see the later evidence.
   Prompt contract v2 clarified injection handling: EN/RU/HE injection scenarios
   received correct successful responses; the latest original 25-case run had
   11 passes and 14 upstream 503 failures. No mismatches among those 11 responses.
   See [interpretation contract and runbook](api/prompt-interpretation.md).
   Contract v3 adds selected-map-point references and targetless A-B requests.
   Earlier v3 run: 32 passed, one ai_unavailable and one Russian targetless A-B
   false location clarification. A later unchanged-contract full run on
   2026-09-29 passed all 34 cases, closing the current corpus qualification gate.
   Prior failures remain evidence of variability, not erased by a successful run;
   manual route input remains independent of Gemini.
   Stage 6b is delivered to main through PR #15:
   AI guides candidate construction/refinement through routing tools; graph-based
   routing supplies traversable geometry. Never fabricate GPX coordinates with an LLM.
   The [bounded refinement design](api/agentic-refinement-design.md) was approved
   on 2026-09-29. Its [implementation plan](plans/2026-09-29-agentic-refinement.md)
   is approved and Tasks 1-5 are implemented. Independent review findings were
   corrected with regression tests. Live qualification failed (1/6 advisor cases;
   mixed four-city comparison), so Task 6 delivery was initially held.
   See [evidence and next checks](evaluation/route-refinement-2026-09-29.md).
   A later six-call Gemini-only diagnosis identified contradictory stop fields
   and truncated responses. The user approved the local `route-search-v2`
   correction; live v2 qualification and the corrected comparison remain pending.
   See [diagnosis and correction](evaluation/route-refinement-diagnostics-2026-09-29.md).
   First authorized v2 live run at `8f3831e`: one stop case passed, one search
   case received provider HTTP 503, four were unrun after fail-fast. Used two
   Gemini calls and zero ORS; no retries or comparison. Merge was blocked at that
   point; subsequent acceptance above supersedes that historical hold.
7. React interface: start-point map selection, prompt, visible interpreted
   preferences, candidates, metrics, GPX download, language switch and Hebrew RTL.
   Implemented and merged through PR #10, with manual input as a
   Gemini-independent testing path. Verification: 68 unit/component tests,
   12 desktop/mobile browser tests and a
   live three-candidate ORS search with exact selected-route GPX download.
   Independent review and both PR/main CI workflows passed. The local UI/API
   launch from the primary checkout passed fresh real-map/RTL checks. See the
   [frontend delivery plan](plans/2026-09-28-minimal-test-ui.md).
8. Persistence when a concrete use case needs it: PostgreSQL/PostGIS for saved
   requests/routes and caching. Keep authentication and Strava beyond the MVP.
9. Deployment and CI/CD: container smoke tests, environment configuration,
   hosting secrets, staging deploy, production deploy and basic monitoring.
   Backend CI includes the evaluator harness and no-key Docker smoke; frontend
   CI exercises a production build with deterministic API/map browser fixtures.
   Local loopback launch is available. Protected full-app staging preparation and
   a checks-gated Render blueprint are implemented. Public staging/production is
   not provisioned; hosting access, runtime secrets and cloud checks remain.

## Current execution order

Priority map correction (2026-09-29): highlight explicit cycleways in Liberty
before continuing interpretation qualification and stage 6b. Implemented with
blue path strokes, preserving pedestrian paths, bridge/tunnel ordering and
generated-route overlays. No new map provider or key. Unit regression was RED
before implementation; desktop pixel test found zero blue pixels before the fix
while the pedestrian control passed. Afterward 74 frontend tests, a production
build and all 18 desktop/mobile browser tests passed. Independent scoped review
found no material defects. Real OpenFreeMap tiles at Reading Park show the blue
cycleway next to unchanged white pedestrian paths; screenshot in primary
artifacts/reading-cycleway-20260929.png. This does not verify all on-road bike
lanes or legal access, and it does not change the ORS routing profile.

1. Completed route-request usability delivery through PR #12: optional distance/time for
   A-B, clear generation readiness, supported manual choices and multilingual
   selected-map-point interpretation. See the
   [execution record](plans/2026-09-29-route-request-usability.md).
2. The v3 live interpretation corpus passed 34/34, separately from offline
   fixtures. Explicit ranges changed extraction to v4 and advisor input to v3;
   fresh live qualification is pending. Earlier passes do not qualify new versions.
3. Road-loop quality now takes priority following the negative 40 km experiment:
   assess surface evidence and exact retracing, select zero to three near-target
   candidates, and expose uncertainty/exclusions in both search modes. Scope
   and [written design](superpowers/specs/2026-09-29-road-loop-quality-design.md)
   and [implementation plan](superpowers/plans/2026-09-29-road-loop-quality.md)
   approved and delivered through PR #15. Evidence parsing,
   shared selection, API/UI exclusions and empty-result handling pass local gates.
   Offline replay retained 1/10 saved alternatives; this is filtering, not proof
   of improved construction or all-paved roads. See the
   [verification report](evaluation/road-quality-2026-09-29.md). Controlled road-network waypoint
   construction follows as a separate prototype, not a promised engine migration.
4. Stage 6b: bounded AI-guided candidate construction/refinement using routing
   tools, with application-owned budgets and unchanged user constraints.
   V2 advisor corpus passed 6/6. Adaptive length calibration is implemented and
   offline-tested and merged under the limited release acceptance above; real
   route-quality improvement remains unverified.
5. Loop / A-B selection, opt-in geolocation, canonical track names, settlement
   search and linked elevation inspection are implemented. Stabilize and verify
   these together, then complete actual Israeli route/GPX/device acceptance.
6. Cloud deployment is deferred to a separate joint task: choose a provider,
   verify its current terms, then configure hosting, secrets and HTTPS acceptance.

Persistence is not a prerequisite for these deliveries. Stage 6b is implemented;
provider-contract acceptance is distinct from route-quality acceptance. Field/device
acceptance and public hosting remain outstanding. Persistence, Strava, individual
accounts, gravel routing and the road-network waypoint prototype are later work,
not hidden prerequisites for this closed tester release.

## Backlog: road-loop search reliability (2026-09-30)

Added after manual testing near Haifa/Nesher. The screenshot for a 35-45 km
range showed no retained candidates after three attempts. A later visible run
with a 50-60 km range returned 65.1, 33 and 83.6 km candidates: all failed the
distance range, and the 65.1 and 83.6 km candidates also failed the road-surface
limit. These observations do not establish that a suitable loop cannot exist.

- [ ] Evaluate length calibration while keeping the seed fixed. The current
  search corrects length using the preceding result but changes seed on every
  attempt; a different loop may not follow the preceding length estimate.
  Compare fixed-seed calibration with the existing strategy before choosing an
  implementation. Keep original ranges and surface requirements unchanged.
- [ ] If calibration is insufficient, prototype controlled road-network
  waypoint construction. Compare distance fit, surface evidence, unnecessary
  manoeuvres and retracing against the baseline and the supplied reference GPX.
  No routing-engine migration is assumed by this backlog item.
- [x] Clarify empty-result wording in EN/RU/HE: state that no suitable route was
  found in the actual number of attempts, without implying that no suitable
  route exists. Retain the excluded-candidate reasons and limited-search warning.

Acceptance: record reproducible starts and target ranges, distinguish offline
verification from live results, and report retained candidates and exclusion
reasons. Cover bounded attempts, cancellation and exact range boundaries with
regression tests when implementation changes. Live comparisons require a
separately agreed provider-call budget; adding this backlog makes no live calls.

Offline preparation is implemented in
[the evaluation utility and protocol](evaluation/road-loop-evaluation.md):
eight validated public-city controls, local GPX / raw ORS assessment, and
[reference/replay evidence](evaluation/road-loop-offline-2026-09-30.md).
This does not complete the fixed-seed comparison or improve production search.

The [bounded live comparison](evaluation/road-loop-live-2026-09-30.md) used
9 of 12 authorized ORS calls and zero Gemini, then stopped on HTTP 500.
The simple fixed-seed arm lost diversity in Tel Aviv; the Haifa comparison
remains incomplete. Keep the existing production strategy and the live/manual
quality gate open.

## MVP backlog: interactive elevation profile (2026-09-30)

Requested using the supplied RouteCycle screenshot as a visual reference.
Display a responsive elevation chart below the map for the selected generated
track, using the route's existing geometry and elevation samples first.

The core chart/inspection feature below was delivered in PR #26 using Chart.js.
Slope calculation and slope-based colors remain separate, unimplemented extras.

- [x] Plot cumulative distance along the track in kilometres on the horizontal
  axis and elevation in metres on the vertical axis, with a filled profile.
  Keep route order from start to finish, including loops and repeated sections.
- [x] Show distance and elevation at the inspected point on hover, keyboard
  focus or touch selection, and highlight the corresponding point on the map.
  Update the chart and clear the inspected point when the selected route changes
  or planning results are invalidated.
- [x] Show minimum/maximum known elevation and the existing route ascent/descent
  metrics.
- [ ] Add slope values and slope-based section colours only where the data
  supports a meaningful calculation; define sampling and noise handling during
  implementation. Do not infer surface coverage from the elevation chart.
- [x] Treat missing elevation as missing data: show gaps or an unavailable state,
  never replace unknown samples with zero or fabricate a continuous profile.
  Support valid zero and below-sea-level elevations. Route selection and GPX
  export must remain available when elevation data is absent.
- [x] Support desktop/mobile, light/dark themes, EN/RU/HE and accessible units
  and labels in automated fixtures. Real phone acceptance remains open; no
  external elevation calls are introduced.

Acceptance: test flat, climbing, descending and loop tracks, duplicate points,
partial/missing elevation and below-sea-level samples; verify chart/map point
correspondence, route switching and invalidation. Inspect desktop/mobile layouts
and Hebrew RTL without overflow or overlapping labels. Large tracks must remain
responsive, and display downsampling must preserve track-to-map correspondence.

## Implemented addition: automatic track names

Requested on 2026-09-29; delivered in PR #20 using a licensed embedded GeoNames
Israel settlement subset, without runtime geocoding or LLM-generated place names.
See the [canonical naming contract](api/geographic-track-names.md).

- Name each generated candidate using the settlements nearest its actual start
  and finish, the route surface/profile label, and its actual generated distance.
  Example: `Tel-Aviv-Haifa-road-105` and `Tel-Aviv-Haifa-road-105.gpx`.
- Use actual route distance rounded to the nearest whole kilometre, not the
  requested target distance. Keep precise distance available in route metrics.
- Use one canonical name in the API response, route selection UI, GPX track
  name (`trk/name`) and downloaded filename. Selecting another candidate must
  use that candidate's name and GPX.
- Resolve settlement names from geographic data (reverse geocoding or a local
  settlement dataset). Choose the data source during implementation after
  checking Israel coverage, licensing, free quotas and caching requirements.
- Proposed filename convention: English/Latin place names, hyphen separators,
  safe filesystem characters, independent of the selected UI language.
  Proposed loop format: `Tel-Aviv-loop-road-40`, avoiding duplicate endpoints.
- Distinguish the routing profile from verified physical surface coverage:
  `road`/`gravel` must not imply that every segment's surface was checked when
  only the provider's routing profile is known.
- Missing settlement data or a lookup failure must not block route generation
  or GPX export. Use an explicit neutral fallback name, never fabricated places.
- Acceptance: endpoint lookup, loops, same-settlement A-B routes, distance
  rounding, unavailable place names, filename sanitization and consistency
  across UI/GPX/downloads are covered by tests.

## Implemented addition: current location as start

Requested on 2026-09-29; implemented and merged in PR #18 after green CI.
See the [implementation and verification record](plans/2026-09-29-start-geolocation.md).
Small frontend usability task using
the existing coordinate-selection/invalidation flow; no new backend endpoint,
paid service, API key, native app or location history required.

- Add an accessible location icon button with EN/RU/HE labels and RTL support.
  The current map control with a location-like icon only resets the map view;
  keep reset and actual geolocation actions distinguishable.
- Call `navigator.geolocation.getCurrentPosition` only after an explicit user
  action. Do not request permission on page load or continuously track location.
- On success, use coordinates as start (never destination), center the map and
  show reported accuracy. Accuracy is not guaranteed, even with high-accuracy
  mode. For coarse fixes, propose the position for confirmation instead of
  silently replacing an existing start; choose a concrete threshold in design.
- Reuse normal start edits to invalidate prepared intent/results and cancel or
  fence stale interpretation/generation responses. A late geolocation callback
  must not overwrite a newer manual/map selection or a newer location request.
- Handle denied permission, timeout, unavailable position, unsupported API and
  embedded-browser restrictions without losing the existing start or results.
  Always retain manual/map selection. Release testing includes a regular browser.
- Production requires HTTPS and an allowing Permissions-Policy. No automatic
  route generation, reverse geocoding, analytics logging or persistent storage
  of the obtained position. Coordinates enter the normal API flow only on a
  subsequent explicit planning action; map centering can request map tiles.
- Test success, inaccurate fixes/confirmation, every failure, stale callbacks,
  start-only behavior, result invalidation, localization and desktop/mobile UI
  using mocked browser geolocation. Real-device accuracy is a separate check.

Reference: [Browser Geolocation API](https://developer.mozilla.org/en-US/docs/Web/API/Geolocation/getCurrentPosition).

## Implemented addition: Loop / A-B segmented control

Requested and implemented on 2026-09-29, delivered through PR #15.
One accessible route-shape selector above the coordinate fields serves prompt
and manual modes. The manual shape dropdown is removed.

- Loop: show only start coordinates, hide the destination marker and destination
  pick action, and make map clicks select start. Omit destination from requests;
  a hidden stale value must not cause validation failures or reach interpretation.
- A-B: show both coordinate fields and enable choosing either map point.
  A prepared A-B intent requires both points; retain existing optional targets.
- Preserve the previous destination only in transient form state for returning
  to A-B; it is inactive in Loop. Switching shape invalidates prepared intent,
  route results and downloads, cancels active work and fences late responses.
- The visible selection is a constraint, including the initial Loop default;
  there is no hidden automatic/unset mode. A conflicting interpreted shape
  produces a local clarification and no ready intent. Change the toggle or
  prompt and prepare again. Canonical API intents are never rewritten.
  Gemini prompts, schemas and API contracts are unchanged; this is a UI
  consistency check, not a fresh live interpretation qualification.
- Coordinate selection uses the existing input invalidation flow; the planned
  current-location action will reuse it. Labels, keyboard operation and RTL
  work in EN/RU/HE.
- Tests cover both directions of switching, hidden destination omission/marker
  removal, retained draft destination, map pick reset, conflicting prompt,
  cancelled/stale responses and desktop/mobile layout.

Verification: six new component cases first failed before implementation;
116 frontend tests, production build and all 34 EN/RU/HE desktop/mobile browser
cases passed, including six new selector cases. Screenshots inspected in desktop and
mobile LTR/RTL; independent scoped review found no actionable defects. No live
ORS/Gemini calls, backend changes or geolocation implementation in this step.

## Implemented addition: theme selection

Approved and implemented on 2026-09-29 and delivered through PR #17 after the
track-segment work in PR #16. Light/dark/system selection uses `next-themes`,
persists when browser storage permits, and follows the OS only in system mode.
The interface and OpenFreeMap basemap change together without replacing user
inputs, generated routes, the selected segment or the camera position.
See the [verification record](evaluation/dark-theme.md) for actual checks and
the unchanged predecessor merge gate. Distance/time ranges remain a separate
API/ranking/interpretation task; no interval semantics are implemented here.

## Previously proposed service shortlist

MapLibre/OpenFreeMap; openrouteservice or GraphHopper; local PostgreSQL/PostGIS;
Ollama or Gemini for development; Cloudflare Pages for frontend; Koyeb for API;
Aiven for cloud PostgreSQL; Sentry and UptimeRobot for monitoring.

These are proposals from the original plan, not verified commitments to current
free tiers. Recheck quotas, commercial terms, Israel coverage, PostGIS support,
and idle/sleep behavior when implementing each integration. An API abstraction
must keep these choices replaceable. There is no service account or deployment
configured by this stage.
