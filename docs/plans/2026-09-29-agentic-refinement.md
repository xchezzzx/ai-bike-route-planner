# Bounded AI Route Refinement Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Track each step with its checkbox. Preserve the user's autonomous execution, logical commits, independent review and successful backend/frontend CI before merging.

**Goal:** Let an optional AI advisor improve a road-loop search without inventing geometry, changing the user's intent or exceeding fixed call budgets.

**Architecture:** Application orchestration owns the search and all guards. A separate Gemini adapter receives only typed preferences and observations, then suggests one bounded third search; ORS still supplies every coordinate. Existing deterministic endpoints remain unchanged.

**Tech Stack:** Existing .NET 10, HttpClient, xUnit, React/TypeScript, Vitest and Playwright; no new package, agent framework or paid service.

**Spec:** [Approved design](../api/agentic-refinement-design.md), approved 2026-09-29.

**Status:** Tasks 1-5 implemented; Task 6 offline checks/review fixes complete, live gate failed and merge withheld. User approved autonomous implementation on 2026-09-29. Paths below are repository-relative. Backend commands run at repository root; npm commands run in `src/frontend`.

## Global Constraints

- Opt-in road loops only; deterministic search remains the default. No gravel, A-B refinement, via points, persistence, authentication or deployment changes.
- At most three ORS calls, one advisor call, 90 seconds total; ORS 15 seconds and advisor 30 seconds per call, including response reads/parsing. No retries or background continuation.
- Seeds 1 and 2 use initial length; an advised search uses a fresh seed in 3..16 and finite length within 1000..100000 metres AND 0.5..1.5 of initial length. Reject, never clamp.
- Original targets, ranker and prior usable candidates remain authoritative. NoRoute consumes an attempt; other routing failures stop search. Caller cancellation wins over partial results.
- Send no coordinates, raw prompt, geometry, GPX, history, secrets or arbitrary provider text to the advisor. Reason codes only: distance, duration, elevation, explore, stop.
- EN/RU/HE and RTL, explicit Generate, cancellation, stale-result fencing and exact selected GPX remain functional. CI never calls live providers.
- Live qualification is separate from offline tests. Default-on or improvement claims need positive equal-budget comparison evidence. Field/device acceptance stays manual.

## Review Focus

1. A provider completes as cancellation/deadline fires: no late call or stale UI success (Tasks 2, 5).
2. Valid JSON contains repeated fields, unknown fields, nonfinite numbers or mixed stop/search fields: reject rather than interpret ambiguously (Tasks 1, 3).
3. A third route duplicates an earlier route in reverse order or changes only heights: retain original usable geometry once (Task 2).
4. UI mode changes during a request, or a valid result takes more than 60 seconds: correct timeout and revision fencing without changing ordinary endpoint timeouts (Task 5).
5. A live run is interrupted or quota-limited: report failures/unrun cases, never silently pass or retry (Task 6).

## Task 1: Advisor Contract and Proposal Policy

**Files:** Create under `src/backend/CyclingRoutes.Application/Routing/`: `IRouteSearchAdvisor.cs`, `RouteSearchAdvice.cs`, `RouteSearchProposalPolicy.cs`, `RouteSearchAdvisorException.cs`. Tests: `src/backend/CyclingRoutes.Tests.Unit/RoutePlanning/RouteSearchProposalPolicyTests.cs`.

**Interfaces:** `IRouteSearchAdvisor.AdviseAsync(RouteSearchContext context, CancellationToken cancellationToken) -> Task<RouteSearchAdvice>`. Immutable records in `RouteSearchAdvice.cs`: `RouteSearchPreferences(RouteShape Shape, CyclingProfile Profile, ElevationPreference Elevation, double? TargetDistanceMeters, double? TargetDurationSeconds)`; `RouteSearchObservation(int Seed, double RequestedLengthMeters, RouteSearchOutcome Outcome, double? DistanceMeters, double? DurationSeconds, double? AscentMeters, double? DistanceDeltaMeters, double? DurationDeltaSeconds)`; `RouteSearchContext(RouteSearchPreferences Preferences, double InitialLengthMeters, IReadOnlyList<RouteSearchObservation> Observations)`; `RouteSearchAdvice(RouteSearchAction Action, int? Seed, double? RequestedLengthMeters, RouteSearchReason Reason)`.

Enums: action Stop/Search; reason Distance/Duration/Elevation/Explore/Stop; outcome Accepted/Duplicate/NoRoute/Failed. `RouteSearchAdvisorException.Failure` uses new `RouteSearchAdvisorFailure` enum: NotConfigured/Authentication/Quota/Unavailable/Timeout/InvalidResponse. Proposal policy: `bool IsValid(RouteSearchAdvice advice, RouteSearchContext context)`, accepting only defined enums, strict stop fields (null seed/length, Stop reason) or strict search fields (non-Stop reason, fresh bounded seed/length). Lists are copied at the service boundary, not shared mutable work lists.

- [x] Table tests cover inclusive bounds, reused seeds, mixed fields and nonfinite length (16 cases).
- [x] RED: focused unit run failed for the missing RouteSearchContext contract before implementation.
- [x] GREEN: full unit suite passed 235/235. Commit `feat: define bounded route search advice contract`.

## Task 2: Bounded Application Orchestration

**Files:** Create `src/backend/CyclingRoutes.Application/Routing/RoutePlanningService.cs`, `RoutePlanningResult.cs`, `RoadLoopGeometry.cs`. Modify `RouteCandidateService.cs` only to delegate its existing geometry validation/deduplication to the new helper. Tests: new `src/backend/CyclingRoutes.Tests.Unit/RoutePlanning/RoutePlanningServiceTests.cs`; existing `RouteCandidateServiceTests.cs` must remain green.

**Interfaces:** `RoutePlanningService(IRoutingProvider provider, IRouteSearchAdvisor advisor, RouteCandidateRanker ranker, TimeProvider timeProvider)` exposes `Task<RoutePlanningResult> PlanAsync(RouteIntent intent, CancellationToken cancellationToken)`. `RoutePlanningResult(RouteCandidateSearchResult Search, int AdvisorCallCount, RouteAdvisorStatus AdvisorStatus, RouteSearchAdvisorFailure? AdvisorFailure, IReadOnlyList<RoutePlanningAttempt> Attempts)`; status NotNeeded/SkippedNoCandidates/SkippedRoutingFailure/Searched/Stopped/Failed. `RoutePlanningAttempt(int Seed, double RequestedLengthMeters, RouteSearchOutcome Outcome, RouteSearchReason Reason, RoutingFailure? Failure)`. Existing Search.AttemptedCount is the ORS call count, not proposal count. Geometry helper exposes `void Validate(RoutedPath path)` and `bool SameGeometry(RoutedPath left, RoutedPath right)` with unchanged baseline behavior.

- [x] Write fake-provider/advisor tests for the approved flow: two initial seeds; early return only if a candidate matches all targets AND intent elevation is Balanced; otherwise one advice and at most one extra ORS call. With zero usable initial routes, skip advisor and try seed 3 at initial length. Stop means no third call; advice failure/timeout/invalid proposal means seed-3 fallback if time remains.
- [x] Add tests asserting original target values and ranked prior candidates survive advice, forward/reverse/elevation-only duplicates are excluded, trace contains only executed calls (including NoRoute/failures), and initial attempts/fallback carry Explore reason. No-candidate result throws the existing RoutingException; partial results preserve existing warnings/failure policy.
- [x] Add deterministic TimeProvider tests for 90-second overall/30-second advisor deadlines, expiry during body/decision handling, cancellation before/after each dependency and before return, simultaneous caller/deadline cancellation, and non-NoRoute provider failure preventing later advice/calls. Count calls immediately before invocation. All dependencies receive linked tokens; no detached continuation.
- [x] RED: `dotnet test src/backend/CyclingRoutes.Tests.Unit --filter RoutePlanningServiceTests`. Implement orchestration, proposal policy enforcement and shared geometry extraction; keep baseline search's 45-second budget unchanged. Use initial distance or existing 20 km/h time conversion and existing GPX writer/ranker; advisor receives observation copies only.
- [x] GREEN: `dotnet test src/backend/CyclingRoutes.Tests.Unit` passes including baseline regressions. Commit `feat: orchestrate bounded AI road loop refinement`.

## Task 3: Separate Gemini Advisor Adapter

**Files:** Create under `src/backend/CyclingRoutes.Infrastructure/Routing/`: `GeminiRouteSearchAdvisor.cs`, `GeminiRouteSearchContract.cs`, `GeminiRouteSearchParser.cs`. Reuse `Interpretation/GeminiOptions.cs` without changing interpretation behavior. Tests: `src/backend/CyclingRoutes.Tests.Integration/GeminiRouteSearchAdvisorTests.cs` (stub HttpMessageHandler, no live calls).

**Interfaces:** `GeminiRouteSearchAdvisor(HttpClient client, GeminiOptions options, TimeProvider timeProvider) : IRouteSearchAdvisor`. Internal parser `RouteSearchAdvice Parse(string responseJson)` rejects malformed provider envelopes and strict advice objects. Contract version `route-search-v1`; advice wire keys `action`, `seed`, `requestedLengthMeters`, `reason`, all required, stop uses nulls. String enums are lowercase. Only a single completed, unblocked candidate is accepted.

- [x] Write serialized-request privacy/schema tests: preferences/observations only, no coordinate/raw prompt/GPX/tool fields, fixed Google endpoint, key only in header, one candidate, JSON schema, bounded output (1024 tokens), version pinned in system instruction. No model prose displayed or logged.
- [x] Write parser tests for stop/search, duplicate/extra fields, invalid enums/types, missing values, mixed fields, huge/nonfinite numbers, blocked/truncated/multiple candidates and foreign model text. Write HTTP tests for auth/quota/503, malformed success/error bodies, 256 KiB response cap (known and streaming), slow headers/body and caller cancellation.
- [x] RED: `dotnet test src/backend/CyclingRoutes.Tests.Integration --filter GeminiRouteSearchAdvisorTests`. Implement using existing interpreter transport patterns: 30-second linked timeout including reads, ResponseHeadersRead, no redirects/retries, sanitized failure enum. Do not reuse the extraction prompt or refactor its working transport in this task.
- [x] GREEN: run the same tests plus `dotnet test src/backend/CyclingRoutes.Tests.Integration --filter GeminiRouteIntentInterpreterTests`. Commit `feat: add guarded Gemini route search advisor`.

## Task 4: Opt-In Planning API

**Files:** Create `src/backend/CyclingRoutes.Contracts/RoutePlanning/RoutePlanResponse.cs`, `src/backend/CyclingRoutes.Api/RoutePlanning/RoutePlanEndpoints.cs`, `RoutePlanRequestReader.cs`, `RouteCandidateResponseMapper.cs`. Modify `RouteCandidatesEndpoints.cs` only to delegate response mapping; register adapter/service/endpoint in `src/backend/CyclingRoutes.Api/Program.cs`. Tests: `src/backend/CyclingRoutes.Tests.Integration/RoutePlanEndpointTests.cs`. Document in new `docs/api/route-plan.md`.

**Interfaces:** POST `/api/routes/plan` consumes existing `RouteIntentRequest`. `RoutePlanResponse(RouteCandidatesResponse Search, int AdvisorCallCount, string AdvisorStatus, string? AdvisorFailure, IReadOnlyList<RoutePlanAttemptResponse> Attempts)`; attempt response mirrors Task 2's attempt with application-owned lowercase/camelCase codes, never provider/model prose. `RoutePlanRequestReader.ReadAsync(HttpRequest request, CancellationToken cancellationToken)` returns `(RouteIntentRequest? Request, int? ErrorStatus)` in a named record. Share candidate mapping through `RouteCandidateResponseMapper.ToResponse(GeneratedRouteCandidate candidate)`; use existing RoutingProblemMapper for no-result failures.

- [x] Write tests for success, stop, skipped advisor, failure+fallback and partial results; assert candidate metrics/GPX unchanged, accurate counters/trace, bounded enum codes and no secrets/model text. Unsupported shape/profile fails before network.
- [x] Write request tests: 64 KiB limit including chunked bodies, 413 oversize, 415 unsupported media/charset, 400 malformed/null/invalid types, strict numeric handling, domain validation, and aborted request before provider work. Mirror interpretation reader conventions without changing old readers/endpoints.
- [x] RED: `dotnet test src/backend/CyclingRoutes.Tests.Integration --filter RoutePlanEndpointTests`. Implement reader/endpoint/mapping/DI. Reuse existing Gemini settings; HttpClient infinite outer timeout with adapter deadline, redirects disabled. Do not enable new paid settings.
- [x] GREEN: `dotnet test src/backend/CyclingRoutes.slnx`; all old endpoint/provider tests pass. Document actual JSON examples, budgets, failures and privacy. Commit `feat: expose opt-in route planning endpoint`.

## Task 5: Minimal Trilingual Refinement UI

**Files:** Modify `src/frontend/src/types.ts`, `usePlanner.ts`, `App.tsx`, `i18n.ts`; `styles.css` only if required for existing layout. Tests: `src/frontend/tests/App.test.tsx`, `api.test.ts`, `fixtures.ts`, `src/frontend/e2e/workflow.spec.ts`.

**Interfaces:** New TS `RoutePlan` mirrors Task 4 response; `usePlanner` returns `refine: boolean`, `setRefine(value: boolean)`, `planning: RoutePlan | null`. Default false. Changing refine aborts/fences active work and clears results but preserves a valid prepared intent. Only ready road loops may request `/api/routes/plan`; other modes ignore refinement and clear planning metadata. Store `response.search` through existing Candidates pipeline and retain trace separately.

- [x] Write component tests: unchecked default still calls /candidates, checked supported loop calls /plan once only on Generate, A-B never calls /plan, fallback and advisor-stop have localized status, unknown status/failure/trace is rejected, selected GPX bytes equal the chosen candidate's GPX.
- [x] Add fake-timer/network tests: `/plan` uses existing request helper with 100000 ms timeout; valid 65-second result succeeds, 100-second deadline aborts, ordinary requests still use 60000 ms. Mode/toggle/coordinate changes and cancellation prevent stale results independently of transport abort.
- [x] RED: `npm test -- --run tests/App.test.tsx tests/api.test.ts`. Add labeled checkbox, localized concise status/warnings and bounded attempt list in existing unframed results. No explanatory marketing copy, safety claims or model-generated prose. Keep EN/RU/HE and RTL, and do not alter route/map selection behavior.
- [x] GREEN: `npm test`, `npm run build`, `npm run test:e2e`. Add desktop/mobile cases for advised search, deterministic fallback, Hebrew RTL and GPX download. Inspect screenshots for nonblank map and no overlapping controls. Commit `feat: add optional AI refinement to planner UI`.

## Task 6: Bounded Qualification and Delivery

**Files:** Create `docs/evaluation/route-refinement-v1.json`, `tools/evaluate-refinement.ps1`, `tools/tests/evaluate-refinement.tests.ps1`, `src/backend/CyclingRoutes.Tests.Integration/RouteRefinementQualificationTests.cs`. Update `.github/workflows/backend-ci.yml` with offline harness tests only, `docs/api/route-plan.md`, `docs/mvp-roadmap.md`, README and this execution checklist. Generated reports go to ignored `artifacts/`.

**Interfaces:** Corpus has separate `advisorCases` (typed synthetic Task 1 contexts and allowed actions/invariants) and `routeCases` (explicit Israeli intent DTOs). `tools/evaluate-refinement.ps1 [-RunLive] [-BaseUrl <loopback-url>] [-CaseLimit <1..4>]` defaults to offline validation; live comparison sequentially calls /candidates and /plan on identical route cases, at least 5 seconds between requests, no retries. Advisor tests validate corpus offline by default. Only when process environment `CYCLING_LIVE_ADVISOR=1`, `dotnet test src/backend/CyclingRoutes.Tests.Integration --filter RouteRefinementQualificationTests` additionally resolves the real advisor from the development API factory and runs the six cases with 5-second pacing, using existing local configuration; clear the opt-in environment variable afterward. No public debug endpoint or key contents printed/saved. CI must explicitly leave this variable unset.

- [x] Define 6 bounded synthetic advisor cases: matching targets, too long, too short, duration-only, nonbalanced elevation with missing ascent, and conflicting extreme observations. Adversarial/malformed model responses are covered by deterministic adapter rejection tests; live tests check schema/bounds, not a supposedly unique best seed. Privacy assertions are at the serialized transport boundary.
- [x] Define 4 reproducible route cases (Tel Aviv, Haifa, Jerusalem, Beersheba); all starts returned routes in the live comparison. Harness tests cover pass, partial/duplicate/degenerate routes, quota, malformed responses, disconnect and unrun reporting. Advisor harness covers cancellation before/pacing/during calls. RED then GREEN verified; failures/unrun remain visible.
- [x] Report original intent, actual calls, latency, relative target errors, usable/unique counts, ascent and sanitized failures. Rename aggregate metric to `bestMeanTargetError` after review; preserve historic report semantics explicitly. Live total was exactly 24 ORS and 10 Gemini, no retries.
- [x] Verify whole branch: 268 unit + 263 integration tests, existing/new offline harnesses, 88 frontend tests, production build, 22 browser tests and `git diff --check`. Independent review found three important issues and one minor; all corrected with regressions. Docker build/no-key smoke passed.
- [x] Attempt bounded live qualification and record [negative evidence](../evaluation/route-refinement-2026-09-29.md): advisor 1/6 passed; comparison had fallback and one worse result. Qualification did NOT pass. No automatic rerun or default-on claim.
- [ ] Open/attach PR, wait for BOTH backend/frontend CI on final SHA, then merge under existing user authorization after live gate/review pass. Verify main CI, sync local main, restart only affected local services and inspect real UI. Record commit/PR and verification evidence in this plan.

## Handoff and Subsequent Work

Self-review: design flow, limits, privacy, parser, API, UI, evaluation and merge gates each map to Tasks 1-6; old APIs/baseline remain independently tested. The five Review Focus items have explicit regression steps. User approved the implementation plan on 2026-09-29.

Execution evidence so far: baseline 219 unit + 188 integration tests. Tasks 1-4
were committed separately; latest backend suite has 268 unit + 256 integration
tests before review; final backend suite has 268 unit + 263 integration tests.
Task 5 passed 88 frontend tests, production build and 22 desktop/mobile
browser cases including refined/fallback search, selected GPX and Hebrew RTL.
Task 6 offline harness passes success, partial/duplicate routes, quota, malformed
responses, disconnect, degenerate geometry, unrun reporting and five-second pacing.
Independent review findings are resolved. Live qualification failed; retain an
unmerged PR even if CI is green. Task 6 delivery remains incomplete.

Review rulings: response guards remain in `routePlan.ts`; test-only advisor
execution was extracted to exercise cancellation persistence; the aggregate
error metric was clarified without changing ranking. No review findings deferred.

After this slice: automatic settlement/profile/distance track naming, route-quality/device acceptance, then public-deployment prerequisites, in that order. They remain separate deliveries, not implied by completing this plan. The advisor may fail to improve quality; document that outcome instead of adding unapproved tools or widening budgets.
