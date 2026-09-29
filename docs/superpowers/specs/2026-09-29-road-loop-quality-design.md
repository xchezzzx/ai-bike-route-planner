# Road-loop quality assessment and candidate selection

Status: proposed written design; implementation awaits review.
Date: 2026-09-29.

## Intent and scope

The user approved following up the bounded ORS experiment with road-quality
assessment, honest candidate selection and then controlled loop construction.
This specification covers the first two items only. The desired outcome is a
road cyclist seeing a small set of near-target candidates with explicit surface
uncertainty and retracing metrics, rather than three nominally successful routes
whose distance or known surface conflicts with the request.

Keep the modular monolith, React UI, existing ORS provider and free-service
constraints. No database, new service, agent framework, paid account or runtime
dependency. Do not change the user's intent or fabricate coordinates.

This phase improves evaluation and selection, NOT the underlying loop generator.
It can legitimately return no candidates. Controlled road-network waypoint
construction is a subsequent design/prototype, evaluated against this baseline.
Do not promise better route generation from this phase alone.

## Evidence and alternatives

The private local experiment reproduced the UI's three route lengths and tested
seven variations. Fastest did not improve its baseline; shortest and distance
correction produced candidates with known non-road surfaces. Exact repeat counts
also exposed retracing that was absent from U-turn instructions. This is one
start and distance, not a comprehensive ORS benchmark. Do not commit the private
GPX, exact start, response geometries or screenshots into public documentation.

1. Change ORS parameters only: cheap, but the experiment did not validate a
   reliable correction for the reported road-quality problem.
2. Recommended: evaluate provider evidence and geometry, then apply one shared
   selection policy to deterministic and AI-guided loops. This makes subsequent
   construction experiments comparable and exposes uncertainty now.
3. Replace/self-host the engine immediately: potentially more control, but adds
   operational scope before comparative evidence. Defer that decision.

## Existing flow and ownership

- `OpenRouteServiceProvider` currently discards extra road information.
- `RouteCandidateRanker` scores distance/time and elevation only.
- `RouteCandidateService` and `RoutePlanningService` both export every ranked
  candidate. They need the same final selection policy.
- `usePlanner` rejects empty candidate arrays; `App` hides result details unless
  there is a selected candidate. Both need an explicit no-match result state.
- Domain intent and profile enums remain unchanged. Provider IDs stay in
  Infrastructure, not Domain or frontend.

Extend `RoutedPath` with optional typed road evidence, preserving existing
construction sites. Add pure Application quality assessment and selection
components. Reuse the existing ranker for target/elevation scoring; do not build
another routing orchestrator. Centralize common selection, reason codes and
assessment mapping, without refactoring unrelated request/deadline code.

## Provider evidence

Request `extra_info: [surface, waytype]` for road-loop calls. No extra HTTP calls,
no instruction generation, and no suitability field until it has a defined use.
Keep point-to-point request and response behavior unchanged in this phase.

Parse each family's `values` ranges into provider-neutral categories. A range
`[start, end, code]` covers geometry edges start through end-1. Validate integer
indices/codes, 0 <= start < end < point count, ordered nonoverlapping ranges and
correct array shape. Missing/null/empty families mean unavailable information;
gaps and unrecognized nonnegative codes are unknown. Negative codes or malformed
ranges produce the existing InvalidResponse failure, never a false known-paved
assessment. Validate families independently, but malformed provided evidence
invalidates the response as a whole. Do not silently erase adverse evidence.

Measure category metres from the geometry edges using one tested great-circle
distance helper. Ignore provider summary percentages for classification and
thresholds; do not mix provider distance with geometry-derived coverage. Keep
provider distance/duration as the existing target metrics. Category totals must
partition the finite positive geometry length. Zero-length edges contribute zero.

Surface categories (adapter-owned ORS mapping):

- paved: IDs 1, 3, 4 (not a guarantee of asphalt, smoothness or safe access).
- non-road surface: IDs 2, 8, 9, 10, 11, 12, 13, 15, 16, 17, 18.
- other known surface: IDs 5, 6, 7, 14, including rough hard surfaces.
- unknown: 0, missing coverage or future/unrecognized nonnegative IDs.

Retain mappings for historical IDs 5/9/16 if encountered; do not assume every
deployment uses the latest source version. Record the mapping in adapter tests.
Way categories: unknown, stateRoad, road, street, path, track, cycleway, footway,
steps, ferry, construction (IDs 0..10 respectively). These are descriptive only:
Track is not equivalent to unpaved, and StateRoad does not imply bike access.

Use typed per-category totals in Application, not provider JSON objects. Include
whether each family was supplied. All aggregate quantities must be finite and
nonnegative. Geometry calculation is linear in points/ranges, observes caller
cancellation, and makes no network calls.

## Geometry quality

For a valid closed loop, identify undirected edges by exact latitude/longitude
endpoints, ignoring elevation. Count each traversal after the first as repeated
distance. Use the same distance helper as evidence aggregation. Do not round
coordinates or merge nearby parallel roads.

Report separately the exact contiguous shared stem from the start: compare each
prefix edge with the corresponding reversed suffix edge until the first mismatch,
never beyond half the edge sequence. Count one return traversal's length as shared
stem metres. Cap it at total repeated metres. Remaining repeated metres are
`max(0, totalRepeated - sharedStem)`.

This stem is only an observed shared approach, not proof it is necessary. All
metrics are lower bounds sensitive to geometry sampling. Do not label every
repeat an unnecessary maneuver. Do not infer this from U-turn instruction counts.
Do not reject solely for repeated edges in v1; flag and rank instead. Existing
closed-loop validation and exact forward/reverse route deduplication remain.

## Selection policy v1

All thresholds below are product heuristics for review, not road-safety standards.
Keep them as named, tested policy constants, not new user controls.

1. Preserve inclusive +/-10% target tolerance for every specified distance/time
   target. A candidate outside it is excluded from the returned selectable set.
2. Exclude a candidate when non-road-surface metres exceed the greater of 100 m
   and 0.5% of geometry length. Equality is retained with a warning; any positive
   non-road coverage is always disclosed. This allows tiny mapped transitions
   without claiming an entirely asphalt route.
3. Exclude positive mapped steps, ferry or construction coverage. Retain the
   existing request avoid flags. This is evidence-based filtering, not a complete
   legal-access or traffic-safety check.
4. Unknown surface does not reject a route and does not count as paved. Other
   known surface, path, track and footway coverage produce warnings, not an
   automatic claim of suitability or a blanket ban on useful cycle connections.
5. For retained candidates, rank by existing target/elevation score plus
   `0.2 * remainingRepeatedMetres / geometryLengthMetres`, then seed for stable
   ties. The returned score must be the score actually used for ordering. A
   shared start stem contributes no extra score penalty but stays visible.
6. Warn on remaining repeats over 5% of geometry length. Always expose total,
   shared and remaining repeat metrics, including below this warning threshold.
7. Return zero to three retained candidates. Never widen tolerance, add a bad
   fallback, relax known surface rules or make extra calls to fill three slots.

Surface evidence state is `unavailable` when no surface ranges were supplied,
`partial` when any coverage is unknown, otherwise `complete`. Completeness is
not suitability. Rejection reasons are explicit and can coexist:
`targets_not_met`, `road_surface_limit_exceeded`, `road_waytype_excluded`.
Warnings cover positive non-road/other surface, uncertain surface, path/track/
footway presence, and repeated geometry. Use shared fixed codes with EN/RU/HE
translations rather than provider prose or LLM text.

## Search integration and AI boundary

Apply assessment/selection to both `/api/routes/candidates` and `/api/routes/plan`.
Keep the deterministic three seeds, deadlines, cancellation precedence and
provider-failure behavior. Filtering happens after valid route acquisition and
deduplication; rejected routes still consume attempts.

For refinement, the early-stop predicate becomes: balanced elevation and at
least one candidate retained by this policy. Keep all valid route observations
available for the existing advisor, even if final selection excludes them. Do
not add geometry, coordinates or new quality fields to the Gemini schema in this
phase. The advisor therefore remains metric-only and cannot fix surface directly.
It cannot override final deterministic rejection. A stop proposal can result in
zero retained candidates. Retain the three-ORS/one-Gemini limits and fallback.

Existing attempt outcome `accepted` continues to mean valid unique geometry
acquired, not that the candidate passed final selection. UI wording must reflect
that distinction; report final exclusions separately. Do not rewrite history to
make rejected quality look like a failed provider call.

## API and UI contract

Keep `targetsMatched` strictly about distance/time. Add `quality` to candidate
assessment with policy version `road-v1`, surface evidence state, geometry length,
surface category metres, way-category metres, and repeat/shared/remaining metres.
Do not expose provider IDs or call the object a safety certification.

Add `excludedCandidates` to search results: one bounded item per unique rejected
candidate with seed, provider distance/duration, target assessment, quality and
rejection codes. No GPX or geometry in these items. `candidates` remains the
selectable list. Include `no_candidate_meets_requirements` when that list is empty
and `candidates_excluded` when exclusions exist. Retain limited-search and partial
failure warnings. `no_candidate_within_tolerance` describes whether ANY acquired
unique candidate met distance/time, independently of surface rejection.

If valid routes were acquired but none retained, return HTTP 200 with an empty
selectable array and exclusions. If none were acquired, preserve existing
NoRoute/provider ProblemDetails. A partial search with zero retained routes must
still expose the upstream failure warning alongside exclusions, not imply that
all possible routes have been tested. Caller cancellation still wins.

Frontend validates both arrays and new numeric/enumerated fields consistently
for ordinary and AI search; malformed data is not a no-match state. Enforce
unique/disjoint seeds, maximum three combined candidates/exclusions and finite
bounded totals. A-B keeps its existing unassessed wrapper and GPX workflow.

Show "Distance/time within tolerance" separately from surface coverage and
retracing. With no selectable route, show the completed search, attempt count,
localized exclusion reasons and any partial failure/AI trace; no download button
or stale route overlay. Keep start/destination and current map view. For nonempty
results preserve selection, exact selected GPX download, map colors and attribution.
Use the existing unframed result layout, a compact metrics list and a details
section for exclusions. No new filters or explanatory marketing copy. EN/RU/HE,
RTL, responsive layout and keyboard access remain required.

This intentionally changes selection semantics and adds response fields. Deploy
backend/frontend together; older UI clients reject empty arrays. Update contract
docs and fixtures in the same delivery. Do not silently declare wire compatibility.

## Verification and delivery

Implement test-first, in logical commits: provider evidence; pure assessment and
selection; services/API; UI and browser coverage. Do not update snapshots merely
to hide old assumptions about always returning three candidates.

- Adapter: emitted extras, mapping, missing/unknown IDs, gaps, zero edges,
  malformed/overlapping/out-of-range ranges, and unchanged auth/failure behavior.
- Geometry: triangle, out-and-back, shared stem plus loop, internal retracing,
  third traversal, reversed orientation, differing elevation, zero-length edges,
  no mutation, finite results and cancellation.
- Policy: every inclusive threshold, one/both targets, geometry/provider length
  differences, known non-road vs unknown, way-category caveats, deterministic
  ordering and no additional requests.
- Both services/API: zero/one/three retained, mixed reasons, duplicates, partial
  provider failure, empty after advisor stop, advisor failure fallback, early
  stop condition, immutable intent, accurate attempt counts and old A-B behavior.
- UI: unknown evidence, partial coverage, retained warning, empty final result,
  exclusions and AI trace, malformed responses, stale response fencing, cancel,
  selection/download, desktop/mobile EN/RU/HE and RTL. Capture Playwright images
  and inspect for clipped text and stale/blank map behavior.
- Existing unit/integration/frontend/build/browser/Docker gates remain required.
  No live ORS/Gemini in CI. Reuse synthetic fixtures, not private user geometry.
- Replay the existing private research corpus offline against the new assessor
  and record retained/excluded counts. This validates policy, not new routes.
  Any later live comparison gets a separately declared request cap; the prior
  10-request spike budget is already exhausted.

Do not merge draft PR #15 on the strength of this design or offline policy tests.
Its failed live AI qualification is an independent outstanding gate. A clean
delivery decision must account for both changes if they share a branch; no
production deployment or engine migration is authorized by this specification.

## Next phase

After this assessment layer is verified, design a bounded waypoint-construction
prototype using actual road-network data and feedback on distance. Compare it
against the same baseline at equal call budgets on multiple Israeli starts and
20/40/80 km targets. Choose data acquisition and an engine only with evidence of
coverage, access limitations, licensing and current free-service terms. Keep LLM
coordinate invention, automatic safety claims and unlimited seed search excluded.

## Sources

ORS definitions checked on 2026-09-29:
[surface](https://giscience.github.io/openrouteservice/api-reference/endpoints/directions/extra-info/surface),
[waytype](https://giscience.github.io/openrouteservice/api-reference/endpoints/directions/extra-info/waytype).

The private experiment report remains in ignored
`artifacts/road-quality-spike/REPORT.md`; it is not required to build or test.
