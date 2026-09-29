# Road-loop Quality Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Return zero to three near-target road-loop candidates with explicit surface evidence, retracing metrics and explainable exclusions.

**Architecture:** Extend the existing ORS adapter with provider-neutral evidence. Use pure Application assessment and one shared selector for both search services, then carry the result through API contracts and the existing React result view. Retain existing budgets and loop construction.

**Tech Stack:** .NET 10, C#, xUnit v3, React/TypeScript, Vitest, Playwright, PowerShell; no new runtime dependencies.

**Spec:** [Approved road-loop quality design](../specs/2026-09-29-road-loop-quality-design.md).

**Status:** Approved by the user and implemented inline. Tasks 1-5 complete; Task 6 local replay, Release, Docker and browser gates pass. Independent review found two validation gaps, corrected with regressions. Delivery/CI verification is in progress; PR #15's separate failed live gate is not waived.

## Global Constraints

- Preserve inclusive +/-10% target tolerance for every specified distance/time target.
- Exclude a candidate when non-road-surface metres exceed the greater of 100 m and 0.5% of geometry length.
- Unknown surface does not reject a route and does not count as paved.
- Return zero to three retained candidates; do not fill slots with rejected routes.
- No new services, dependencies, billing, database or model/schema changes.
- Three ORS calls maximum; one advisor call maximum in refinement; preserve deadlines, partial failures and cancellation precedence.
- Keep A-B behavior, EN/RU/HE, RTL, map attribution and exact selected GPX download.
- No live provider calls in CI. The previous 10-call spike budget is exhausted.
- No private track/precise start/secret in source control. Use synthetic fixtures.
- No production deployment or engine migration. Draft PR #15 retains its separate failed live-qualification gate.

## Review Focus

1. Sparse surface ranges plus future codes must not become 100% paved: adapter tests in Task 1.
2. A long shared stem and an internal retrace must not receive the same penalty: geometry tests in Task 2.
3. An in-tolerance but excluded candidate must not cause premature AI early stop: service tests in Task 3.
4. A previous map overlay/download must disappear when the next search has no matches: component/browser tests in Task 5.
5. A valid empty search or partial upstream failure must never become a passing qualification: harness tests in Task 4.

## Files and Interfaces

Paths below are relative to the repository root. Application routing files reside
in `src/backend/CyclingRoutes.Application/Routing/`, Infrastructure routing files
in `src/backend/CyclingRoutes.Infrastructure/Routing/`. Do not reorganize other modules.

- `RoadEvidence.cs` (new, Application): `SurfaceBreakdown(double PavedMeters, double NonRoadMeters, double OtherKnownMeters, double UnknownMeters)`; `WayBreakdown(double UnknownMeters, double StateRoadMeters, double RoadMeters, double StreetMeters, double PathMeters, double TrackMeters, double CyclewayMeters, double FootwayMeters, double StepsMeters, double FerryMeters, double ConstructionMeters)`; `RoadEvidence(double GeometryLengthMeters, bool SurfaceSupplied, bool WaytypeSupplied, SurfaceBreakdown Surface, WayBreakdown Ways)`.
- `RouteGeometryMetrics.cs` (new, Application): `static double DistanceMeters(GeoCoordinate a, GeoCoordinate b)` and `static double[] EdgeLengths(IReadOnlyList<RoutePoint> points, CancellationToken cancellationToken)`. Earth radius 6371008.8 m; clamp the haversine intermediate into [0,1].
- `RoutedPath.cs`: append `RoadEvidence? Evidence = null` to `RoutedPath` for source compatibility. Do not alter `GeneratedRoute`/GPX semantics.
- `OpenRouteServiceEvidenceParser.cs` (new, Infrastructure): `static RoadEvidence Parse(JsonElement properties, IReadOnlyList<RoutePoint> points, CancellationToken cancellationToken)`. Use typed totals, no public provider-ID enums.
- `RoadQualityAssessment.cs` (new, Application): enum `SurfaceEvidenceState { Unavailable, Partial, Complete }`; record `RoadQualityAssessment(string PolicyVersion, double GeometryLengthMeters, SurfaceEvidenceState SurfaceEvidenceState, bool WaytypeSupplied, SurfaceBreakdown Surface, WayBreakdown Ways, double RepeatedMeters, double SharedStemMeters, double RemainingRepeatedMeters)`.
- `RoadQualityAssessor.cs` (new, Application): `RoadQualityAssessment Assess(RoutedPath path, CancellationToken cancellationToken)`.
- `RoadCandidateSelector.cs` (new, Application): constructor `(RouteCandidateRanker ranker, RoadQualityAssessor assessor)`; `RouteSelectionResult Select(RouteIntent intent, IReadOnlyList<RouteCandidate> candidates, CancellationToken cancellationToken)`.
- `RouteCandidate.cs`: Task 2 appends optional quality to `RouteCandidateAssessment` and adds `ExcludedRouteCandidate(int Seed, double DistanceMeters, double EstimatedDurationSeconds, RouteCandidateAssessment Assessment, IReadOnlyList<string> Reasons)` and `RouteSelectionResult(IReadOnlyList<RankedRouteCandidate> Retained, IReadOnlyList<ExcludedRouteCandidate> Excluded, bool AnyTargetsMatched)`. Task 3 appends non-null `ExcludedCandidates` to search result and updates every caller atomically, keeping Task 2 independently buildable.
- `RoadQualityResponse.cs` (new, Contracts/RoutePlanning): API-owned counterparts of quality/breakdown records, string evidence state; never expose Application types in DTOs.
- `RouteCandidatesResponse.cs`: assessment gains quality; search gains `ExcludedCandidates`; add `ExcludedRouteCandidateResponse` mirroring its Application counterpart without geometry/GPX.
- `src/frontend/src/routeQuality.ts` (new): runtime validation shared by normal and AI result handling, including exclusions. `src/frontend/src/RouteQuality.tsx` (new): selected quality metrics and excluded-route details using existing styling/i18n.

## Task 1: Acquire and Validate Road Evidence

**Files:** Create Application `RoadEvidence.cs`, `RouteGeometryMetrics.cs`; Infrastructure `OpenRouteServiceEvidenceParser.cs`. Modify `RoutedPath.cs`, `OpenRouteServiceProvider.cs`. Tests: new Unit/RoutePlanning/RouteGeometryMetricsTests.cs and Integration/OpenRouteServiceEvidenceTests.cs; existing Integration/OpenRouteServiceProviderTests.cs.

**Consumes:** Existing RoutePoint/GeoCoordinate, provider transport and xUnit stub-handler conventions.
**Produces:** Evidence records, geometry distance helper and parser signatures specified above. Only loop requests ask for/parse extras; A-B is unchanged.

- [x] Write `DistanceMeters_UsesMetresAndIgnoresElevation`: assert (0,0) to (1,0) is 111190..111200 m, coincident points 0, symmetry; edge cancellation throws. Extend loop payload assertion: `Assert.Equal(new[] { "surface", "waytype" }, requestedExtras); Assert.False(instructions);` and A-B assertion that `extra_info` is absent.
- [x] Run `dotnet test src/backend/CyclingRoutes.slnx --filter "FullyQualifiedName~RouteGeometryMetricsTests|FullyQualifiedName~OpenRouteService"`; record expected RED for missing new behavior, not an unrelated environment error.
- [x] Implement the helper, records and parser. Range validation and every surface/waytype ID follow the spec. Sum per-edge distances; fill gaps as unknown; ignore summary percentages. Pass cancellation into parsing and check per range/point. Missing entire extras/families/values and null/empty values yield unknown; provided wrong JSON types are invalid responses.
- [x] Add `Evidence_PartitionsGeometryDespiteDifferentProviderDistance`, `Evidence_UnknownAndGapsStayUnknown`, `Evidence_AllMappedCodes`, `Evidence_RejectsMalformedRanges`, `Evidence_ObservesCancellation`: assert finite totals sum to geometric length (1e-8 relative tolerance), future code becomes unknown, malformed arrays/negative/fractional/out-of-order/overlap/out-of-range indices fail, supplied empty family is unavailable. Include zero-length edges and one missing family with the other valid.
- [x] Rerun the focused command and full backend tests; all must pass. Keep existing charset/auth/quota/failure/cancellation regressions. Commit only this task's files: `feat: retain validated road evidence from ORS`.

## Task 2: Assess Quality and Select Candidates

**Files:** Create Application `RoadQualityAssessment.cs`, `RoadQualityAssessor.cs`, `RoadCandidateSelector.cs`. Modify `RouteCandidate.cs`. Tests: new Unit/RoutePlanning/RoadQualityAssessorTests.cs, RoadCandidateSelectorTests.cs; preserve RouteCandidateRankerTests.cs.

**Consumes:** Task 1 evidence and geometry helper; existing `RouteCandidateRanker.Rank` remains the distance/time/elevation baseline.
**Produces:** `Assess`, `Select`, quality/selection/exclusion records. Existing ranker tests using empty geometry remain valid because the old ranker itself does not start assessing geometry.

- [x] Write `Assess_SeparatesSharedStemFromInternalRepeat`: construct a synthetic A-B-C-D-B-A loop and then a loop with an internal out-and-back; assert `remaining = repeated - shared`, shared counts one return traversal, internal repeat contributes to remaining. Add triangle (all zero), reversal invariance, third traversal, elevation-only change, repeated zero edge and cancellation.
- [x] Write selector boundary tests using valid synthetic geometry and evidence: at a 40,000 m geometry length, 200 m non-road retained with warning, 200.01 rejected; at 10,000 m, 100 retained and 100.01 rejected. All-unknown surface is retained if target matches and quality state is unavailable/partial as appropriate. Positive steps/ferry/construction rejects, track alone does not.
- [x] Run `dotnet test src/backend/CyclingRoutes.Tests.Unit --filter "FullyQualifiedName~RoadQuality|FullyQualifiedName~RoadCandidateSelector"`; record RED.
- [x] Implement exact undirected-edge tracking and prefix/reversed-suffix stem detection in O(n). No coordinate rounding, repeated range slicing or O(n^2) search. Enforce finite positive geometric length and finite/nonnegative evidence totals matching it; reject inconsistent supplied evidence as InvalidResponse. Missing Evidence is all-unknown. Do not mutate paths or intent.
- [x] Implement selection: base ranking on all valid unique acquired candidates; add `0.2 * remaining / geometryLength` to the reported score and sort retained by that score then seed. Keep all rejection reasons, aggregate `AnyTargetsMatched` before exclusion, and retain zero to three routes. Do not normalize ascent again after exclusion.
- [x] Pin fixed warning codes: `road_surface_unknown`, `road_surface_non_road`, `road_surface_other`, `road_path_present`, `road_track_present`, `road_footway_present`, `road_retracing`. Generate them from positive coverage (unknown includes unavailable); retracing warning only when remaining/length >0.05. Preserve ranker warnings. Exclusion codes are exactly the three from the spec.
- [x] Add distance/time +/-10% boundary tests, all targets required, different provider/geometry length, equality at 5% retracing, score/tie ordering and no mutation; compare synthetic identical candidates differing only in internal repeats. Run focused and full unit suites GREEN. Commit `feat: assess and select road-loop candidates`.

## Task 3: Integrate Both Search Services and API

**Files:** Modify Application RouteCandidate.cs, RouteCandidateService.cs, RoutePlanningService.cs; Api/Program.cs; Contracts/RoutePlanning/RouteCandidatesResponse.cs; create Contracts/RoutePlanning/RoadQualityResponse.cs; modify Api/RoutePlanning/RouteCandidateResponseMapper.cs, RouteCandidatesEndpoints.cs, RoutePlanEndpoints.cs. Update docs/api/route-candidates.md and route-plan.md. Tests: both service unit suites and both endpoint integration suites.

**Consumes:** Task 2 selector. Both service constructors replace ranker with `RoadCandidateSelector selector`; register selector/assessor in DI. Keep ranker registered for selector.
**Produces:** Shared selection semantics and serialized quality/exclusions for both endpoints. Existing generated route payload/GPX is unchanged.

- [x] Add tests `Search_ReturnsOnlyRetainedAndExplainsExclusions`, `Search_AllExcludedReturnsEmptySuccess`, `Search_ExcludedThenTimeoutPreservesBothReasons` to both services; assert attempted counts include excluded/duplicate/NoRoute calls, and no fourth routing call.
- [x] Add `Plan_InToleranceButExcludedDoesNotEarlyStop`, `Plan_StopMayReturnNoMatches`, `Plan_AdvisorFailureStillUsesBoundedFallback`; assert max one advisor, max three routing calls, original intent unchanged and advisor input schema unchanged. Run `dotnet test src/backend/CyclingRoutes.slnx --filter "FullyQualifiedName~RouteCandidateService|FullyQualifiedName~RoutePlanningService"` and capture RED.
- [x] Implement final selection in both services. Generate GPX only for retained candidates. Keep existing provider-failure and no-acquired-route exceptions; use selector retained count for AI early stop, without removing raw observations. Keep accepted attempt outcome meaning acquisition, not final suitability. Collect standard warning codes once from selection, adding no-match and exclusion codes as specified.
- [x] Implement API DTO mapping, including bounded excluded summaries and string enums, and endpoint responses. Add tests asserting HTTP 200 plus empty candidates/exclusions, absence of GPX/geometry in exclusions, combined count <=3, simultaneous incomplete/provider warnings, and NoRoute/503 behavior when nothing was acquired. Update old tests that intentionally expected out-of-tolerance candidates into explicit exclusion assertions rather than altering synthetic metrics to hide behavior.
- [x] Run both endpoint suites and full backend solution GREEN; verify `/generate` regressions and request-size/cancellation tests still pass. Update contract docs with deployment incompatibility for old clients. Commit `feat: expose quality-filtered road-loop search results`.

## Task 4: Preserve Honest Evaluation Outcomes

**Files:** Modify tools/evaluate-refinement.ps1, tools/tests/evaluate-refinement.tests.ps1; docs/api/route-plan.md evaluation instructions. Keep prior live reports immutable.

**Consumes:** Task 3 search response shape.
**Produces:** Evaluation results with `status=noMatch` for valid completed empty searches; `incomplete` takes priority when upstream/advisor failure occurred. Neither status counts as a qualification pass.

- [x] Add mocked local-server scenarios: zero retained with valid exclusions, zero retained with a partial upstream failure, malformed exclusions, duplicated candidate/exclusion seed, and retained with unknown evidence. Assert noMatch vs incomplete vs invalid distinctions, original request cap/pacing and nonzero script exit for every non-passed result.
- [x] Run `pwsh -NoProfile -File tools/tests/evaluate-refinement.tests.ps1`; capture RED from the existing min-one-candidate assumption.
- [x] Validate candidates plus exclusions, allowing empty only with a valid bounded excluded set/no-match warning; never interpret malformed zero-result data as noMatch. Add excluded count/reason summaries without coordinates/GPX. Leave empty-set best/worst target metrics null, not zero; keep matched false. Do not change advisor corpus version or erase original failure reports.
- [x] Rerun harness GREEN and `pwsh -NoProfile -File tools/tests/evaluate-prompts.tests.ps1` for shared-environment regression; no external provider calls. Commit `fix: distinguish no-match routes in qualification reports`.

## Task 5: Show Quality, Exclusions and Empty Results

**Files:** Create src/frontend/src/routeQuality.ts, RouteQuality.tsx, tests/routeQuality.test.ts. Modify types.ts, usePlanner.ts, routePlan.ts, App.tsx, i18n.ts, styles.css; tests/fixtures.ts, App.test.tsx; e2e/workflow.spec.ts.

**Consumes:** Task 3 contracts. All labels and warnings use the fixed code dictionary in EN/RU/HE.
**Produces:** Shared runtime validator `validRoadCandidates(value: unknown): value is Candidates`, reusable quality/exclusion view; A-B still uses its existing route validation and unassessed wrapper, not road-loop validation.

- [x] Add parser tests for zero/one/three retained, invalid/NaN/negative metrics, unknown states, missing required quality, inconsistent totals, repeated/shared bounds, duplicated/disjoint seed violations, combined count >3 and malformed reasons. Use a 1e-8 relative tolerance for floating totals, not target tolerance. Run `npm test -- tests/routeQuality.test.ts` in src/frontend and record RED.
- [x] Implement response types/validator. Require `road-v1`, finite positive geometry length, category sums matching it, `0 <= shared <= repeated <= geometryLength` and `remaining = repeated-shared` within numeric tolerance. Retained must have targetsMatched=true and no exclusion reasons; excluded reasons must be recognized and nonempty. Valid empty search requires exclusions and no-match warning. Preserve generic warning fallback and existing route validation.
- [x] Add component tests for selected quality metrics, all-unknown evidence, retained short non-road coverage, all-excluded with upstream warning/AI trace, cancelled/stale responses, and successful result followed by empty result. Assert no download or stale route data after empty, but visible attempt count and exclusion reasons. Run focused App tests RED.
- [x] Implement a result shell independent of `chosen`; selected-only metrics/download live inside its nonempty branch. Put exclusions in a details section with seed/distance/reason, no selectable geometry. Change target text to distance/time-specific copy and accepted attempt text to acquired geometry; never say road safety verified. Keep current map viewport on empty data and preserve start/destination markers. Add an explicit regression to RouteMap behavior if empty updates currently reset the viewport.
- [x] Add desktop/mobile EN/RU/HE Playwright cases for one retained/two excluded and all excluded, including RTL, no text overflow, nonblank map pixels, stale overlay removal and selected GPX byte equality. Reuse mocked API/map fixtures; no live providers. Capture and inspect screenshots for both states.
- [x] Run `npm test`, `npm run build`, `npm run test:e2e` in src/frontend, all GREEN. Commit `feat: show road quality and no-match search results`.

## Task 6: Replay, Review and Delivery Gates

**Files:** Add docs/evaluation/road-quality-2026-09-29.md with aggregate synthetic/private replay conclusions only; update docs/mvp-roadmap.md and this plan incrementally. Keep any replay harness/output under ignored artifacts unless it uses only synthetic public fixtures.

**Consumes:** Tasks 1-5, existing local raw ORS corpus if still available.
**Produces:** Verified implementation record distinguishing automated behavior, offline replay, unperformed live checks and outstanding PR15 qualification.

- [x] Offline replay: load the existing saved responses through the production ORS adapter using a stub HttpMessageHandler, then assessor/selector with their original target. Do not invoke probe.ps1 or an external HTTP endpoint. Record input IDs, geometric/provider lengths, category totals and rejection reasons. Independently sum ranges/edges for one retained and one excluded case; compare with the production result. Do not require exact equality with prior provider-summary-based research totals.
- [x] Run `dotnet restore src/backend/CyclingRoutes.slnx`, `dotnet build src/backend/CyclingRoutes.slnx --configuration Release --no-restore`, `dotnet test src/backend/CyclingRoutes.slnx --configuration Release --no-build --verbosity normal`; both PowerShell evaluator harnesses; full frontend test/build/browser suite. Ensure CYCLING_LIVE_ADVISOR is unset in the test process. Record actual counts/errors, not historic counts.
- [x] Docker smoke: build with the existing Dockerfile, launch an ephemeral loopback port with no provider keys, verify /health=Healthy, interpretation missing-key=503 and malformed JSON=400, then stop only that created container. Follow existing CI recipe; do not stop user containers or existing development servers.
- [x] Perform full diff/related-code review against every spec section, including provider parsing, metrics, empty result and CI fixture selection. Fix findings with regression tests and rerun affected suites. Obtain independent review when available; otherwise label the review as self-review.
- [ ] Commit verified delivery docs. Push logical commits only after local gates pass and update/attach the existing draft PR15 rather than creating a duplicate PR for the same branch. Inspect fresh CI status; do not merge while its live AI qualification remains failed. Do not enable billing or use green offline quality tests to bypass that gate.
- [x] Preserve the current dev server until verification finishes. If restarting the owned worktree session is necessary, use tools/start-local.ps1 and its manifest, preserve secrets, choose free ports and provide the resulting URL. Do not reuse stale-process behavior as evidence of new code.
- [ ] Report what changed, tests run and remaining construction/live/device gaps. The next phase is a separate road-network waypoint prototype, not an implicit promise that this filter made route generation better.

## Self-review Record

Spec sections map to tasks: provider evidence 1; geometry/policy 2; search/API 3;
evaluation failure semantics 4; UI 5; qualification/privacy/delivery 6. All five
review-focus cases have owners and explicit tests. No product code or provider
calls were included in writing this plan. Implementation evidence is recorded in
[the execution report](../../evaluation/road-quality-2026-09-29.md).

## Implementation Rulings

- Intermediate backend checks used Debug because the running Release API held DLLs open. Final Release restore/build/test passed after stopping only the owned launcher session.
- The component is named `RoadQualityPanel.tsx`, avoiding a Windows case-insensitive collision between `RouteQuality.tsx` and `routeQuality.ts`.
- Threshold tests use computed synthetic geometry lengths instead of an artificial exact 40,000 m geometry; both proportional threshold and absolute floor have inclusive boundary tests.
- Existing exact-overlay clearing needed no production map change. Browser tests verify it after a successful result followed by no matches.
