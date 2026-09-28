# Loop Candidates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Return up to three real road loops, ranked against the rider's targets, with GPX and explicit limitations.

**Architecture:** Extend the existing ORS adapter with a loop operation. Application owns bounded search, loop validation, exact deduplication and pure ranking; API owns HTTP mapping. Keep the existing A-B generation flow compatible.

**Tech Stack:** Existing .NET 10, ASP.NET Core, HttpClient, System.Text.Json, LINQ to XML, xUnit and WebApplicationFactory. No new packages or services.

**Spec:** [Approved design](../api/route-candidates-design.md).

**Status:** Approved and implemented locally on 2026-09-28. A repeated independent Application review completed; its findings are addressed with regression tests. Commit/push/PR remain separate user-requested steps.

## Global Constraints

- New POST /api/routes/candidates accepts loop + road only; all three elevation preferences participate in ranking.
- Reuse RouteIntentRequest and its validator. Missing targets remain invalid; do not invent user preferences.
- Requested length uses distance when present, otherwise targetDurationSeconds * 20000 / 3600 with initial_speed_20_kmh.
- Length range is 1000..100000 metres inclusive; reject outside it with 422 search_distance_out_of_range before network work.
- Seeds 1, 2, 3, sequential calls, same length, round_trip.points=3; no retries, adaptive search or replacement calls.
- Preserve per-call 15-second timeout, 8 MiB response limit, authorization header, disabled redirects and existing road restrictions.
- Overall deadline is 45 seconds. Caller cancellation takes precedence over timeout or partial success.
- Closed provider geometry with at least three distinct positions; no fabricated closing segments or elevation.
- Exact sequence/reversed-sequence deduplication ignores elevation and keeps the lowest seed.
- Target tolerance is inclusive 10%; score and warnings follow the spec exactly. ORS duration is not recomputed at 20 km/h.
- No AI, persistence, frontend, accounts, public deployment, paid resources, gravel fallback or new dependencies.
- CI uses fixtures only. Never print/read the configured key or commit live artifacts.

## Review Focus

- Simultaneous caller cancellation and provider failure must not return partial 200; pin in Task 3.
- Positive finite metrics near double.MaxValue must produce finite scores and serializable deltas; pin in Task 2.
- A duplicate carrying different elevations must not replace the earliest candidate or bias normalization; pin in Task 3.
- A first successful candidate followed by quota/invalid-geometry failure must retain its GPX and expose a safe warning; pin in Tasks 3 and 4.
- Refactoring shared provider/HTTP handling must preserve A-B behavior, especially unknown charset, 2D geometry, no key and timeout; pin in Tasks 1 and 4.

## File Map

Paths below are relative to src/backend unless explicitly rooted at docs/.

| Files | Responsibility |
| --- | --- |
| CyclingRoutes.Application/Routing/IRoutingProvider.cs | Add provider loop operation |
| CyclingRoutes.Application/Routing/RoutingException.cs | Append SearchDistanceOutOfRange and LimitExceeded enum cases |
| CyclingRoutes.Application/Routing/RouteCandidate.cs | Candidate, assessment and search-result records |
| CyclingRoutes.Application/Routing/RouteCandidateRanker.cs | Pure scoring and stable ordering |
| CyclingRoutes.Application/Routing/RouteCandidateService.cs | Request policy, call budget, validity, deduplication, deadline, GPX |
| CyclingRoutes.Infrastructure/Routing/OpenRouteServiceProvider.cs | Shared transport/parser and loop payload |
| CyclingRoutes.Contracts/RoutePlanning/RouteCandidatesResponse.cs | New public response records |
| CyclingRoutes.Api/RoutePlanning/RouteCandidatesEndpoints.cs | New route registration, validation and result mapping |
| CyclingRoutes.Api/RoutePlanning/RoutingProblemMapper.cs | Shared failure-to-status/code mapping |
| CyclingRoutes.Api/RoutePlanning/RouteGenerationEndpoints.cs | Reuse error mapper only; preserve A-B success contract |
| CyclingRoutes.Api/Program.cs | Register search service, ranker and TimeProvider; map endpoint |
| CyclingRoutes.Tests.Unit/RoutePlanning/RouteCandidateRankerTests.cs | Pure score tests |
| CyclingRoutes.Tests.Unit/RoutePlanning/RouteCandidateServiceTests.cs | Scripted provider and manual clock tests |
| CyclingRoutes.Tests.Integration/OpenRouteServiceProviderTests.cs | Loop payload and shared adapter regression tests |
| CyclingRoutes.Tests.Integration/RouteCandidatesEndpointTests.cs | Full HTTP-to-adapter fixture tests |
| CyclingRoutes.Tests.Integration/RouteGenerationEndpointTests.cs | Old endpoint error/compatibility regression tests |
| CyclingRoutes.Api/CyclingRoutes.Api.http; docs/api/route-candidates.md; docs/mvp-roadmap.md | Examples, contract and verified stage status |

## Verification Commands

Run from repository root. For a focused test cycle use:

```powershell
dotnet test src/backend/CyclingRoutes.slnx --configuration Release --filter FullyQualifiedName~CLASS_NAME --verbosity minimal
```

Replace CLASS_NAME with the exact class named in each task. A red test must fail for the intended missing behavior, not an unrelated environment error; record red and green evidence in the ledger. Establish the baseline before Task 1 with the full test command from Task 5. Do not treat previously reported 167 tests as a fresh result.

### Task 1: Provider-Backed Road Loops

**Files:** IRoutingProvider.cs, RoutingException.cs, OpenRouteServiceProvider.cs, OpenRouteServiceProviderTests.cs from the file map.

**Interfaces:** Add `Task<RoutedPath> GetRoadLoopAsync(GeoCoordinate start, double requestedLengthMeters, int seed, CancellationToken cancellationToken)` to IRoutingProvider. Keep GetRoadRouteAsync unchanged. Append RoutingFailure.SearchDistanceOutOfRange and RoutingFailure.LimitExceeded without renumbering existing cases.

- [x] Write `RoadLoop_SendsSingleCoordinateAndRoundTripOptions`: inspect POST to the existing cycling-road GeoJSON endpoint, one [longitude, latitude] coordinate, length=20000, points=3, seed=2, elevation=true, instructions=false, units=m, avoid_features=ferries/fords/steps, Authorization header and no key in URL.
- [x] Run OpenRouteServiceProviderTests and record the intended red result.
- [x] Implement GetRoadLoopAsync; extract only the common send/parse path needed by both operations. Parse numeric ORS error 2004 as LimitExceeded on non-success responses; preserve status precedence for 401/403/429/5xx and existing 2009/2010 mapping.
- [x] Add `RoadLoop_ParsesClosed2DAnd3DGeometry`, `ParameterLimit_Maps2004`, and loop cases for no key, unknown charset, malformed body, no route, quota, transport timeout and caller cancellation. Successful 2D response has null optional elevation/ascent/descent, not zeros. Loop-specific closure enforcement belongs to Task 3, not the adapter.
- [x] Run OpenRouteServiceProviderTests, then the full suite; verify existing A-B transport tests stay green. Update any repository test doubles implementing IRoutingProvider without weakening their existing assertions.

**Deliverable:** Both provider operations work with controlled HTTP responses; no new HTTP endpoint yet.

### Task 2: Pure Candidate Ranking

**Files:** Create RouteCandidate.cs, RouteCandidateRanker.cs and RouteCandidateRankerTests.cs.

**Interfaces:** Define the following records in CyclingRoutes.Application.Routing:

```csharp
RouteCandidate(int Seed, RoutedPath Path)
RouteCandidateAssessment(double? DistanceDeltaMeters, double? DurationDeltaSeconds, bool TargetsMatched, double Score)
RankedRouteCandidate(RouteCandidate Candidate, RouteCandidateAssessment Assessment, IReadOnlyList<string> Warnings)
GeneratedRouteCandidate(int Seed, RouteCandidateAssessment Assessment, GeneratedRoute Route)
RouteCandidateSearchResult(double RequestedLengthMeters, IReadOnlyList<string> Assumptions, int AttemptedCount, IReadOnlyList<string> Warnings, IReadOnlyList<GeneratedRouteCandidate> Candidates, RoutingFailure? IncompleteFailure)
```

Ranker exposes `IReadOnlyList<RankedRouteCandidate> Rank(RouteIntent intent, IReadOnlyList<RouteCandidate> candidates)`. Input is already unique with valid provider metrics; ranker neither calls providers nor creates GPX.

- [x] Write `Rank_UsesApprovedDistanceTimeAscentExample`: targets 20000 m / 3600 s, A(seed=1)=20000/3960/100, B(seed=2)=21000/3600/300; both TargetsMatched=true; balanced order [2,1], minimize [1,2], seekClimbs [2,1]. Minimize scores are 0.1066666667 and 0.22 within numerical tolerance.
- [x] Run RouteCandidateRankerTests and record the intended red result.
- [x] Implement signed deltas, capped relative target error, known-ascent normalization and score. Avoid overflow in the mean by averaging already capped errors; no unbounded sum of raw errors. Order matched first, then score, then seed. Return targets_not_met and elevation_data_unavailable exactly as specified.
- [x] Add named theories for distance-only/time-only/both targets, exact 10% boundary and just outside, missing/null deltas, missing vs zero ascent, all-zero ascent, matched-before-unmatched regardless of ascent, and seed ties. Use finite double.MaxValue metrics to assert finite Score in [0,1] and finite signed deltas; include a tiny positive duration target via domain construction.
- [x] Run RouteCandidateRankerTests; verify all cases green and no routing/HTTP dependencies entered the ranker.

**Deliverable:** Deterministic, independently tested ranking with explainable deviations.

### Task 3: Bounded Search and Partial Results

**Files:** Create RouteCandidateService.cs and RouteCandidateServiceTests.cs; use Task 2 records and ranker.

**Interfaces:** `RouteCandidateService(IRoutingProvider provider, RouteCandidateRanker ranker, TimeProvider timeProvider)` exposes `Task<RouteCandidateSearchResult> GenerateAsync(RouteIntent intent, CancellationToken cancellationToken)`. IncompleteFailure is an application enum, not an HTTP status or an upstream message; API maps it in Task 4.

- [x] Write `DurationOnly_RequestsThreeTwentyKilometreLoops`: target 3600 s, scripted provider returns three distinct valid loops; assert lengths [20000,20000,20000], seeds [1,2,3], maximum concurrent calls=1, AttemptedCount=3 and assumption initial_speed_20_kmh. Provider durations must survive unchanged.
- [x] Run RouteCandidateServiceTests and record red.
- [x] Implement supported-intent and length checks before calling the provider. Use TimeProvider with a 45-second timeout CancellationTokenSource plus the caller-linked token. Dispose both sources. Check caller cancellation before calls, when handling failures and before returning. Distinguish caller cancellation from deadline expiry without a real 45-second test sleep.
- [x] Implement loop validation, forward/reverse sequence deduplication using domain position equality, and bounded iteration. Continue after NoRoute; stop for other RoutingFailure values. If no candidate survives, throw the stopping failure, or NoRoute when all attempts exhausted. With partial candidates expose candidate_generation_incomplete plus IncompleteFailure; do not translate caller cancellation into a result.
- [x] Rank unique survivors, serialize each GPX through existing GpxWriter, add candidate_search_limited always and no_candidate_within_tolerance only when none match. Failed/duplicate attempts still count; do not retry to fill the result list.
- [x] Add `LengthPolicy_RejectsBeforeProvider` for 999/100001 and duration-derived limits; 1000/100000 accepted; distance wins when both supplied. Add `UnsupportedIntent_MakesNoCalls` for gravel and pointToPoint; every valid elevation preference accepted.
- [x] Add `InvalidLoop_StopsWithoutSyntheticClosure` for open geometry and fewer than three distinct positions. Add `DuplicateOrReverse_KeepsFirstSeedIgnoringElevation` and assert retained ascent affects ranking; partial overlap and out-and-back sections remain allowed.
- [x] Add `NoRoute_ConsumesAttemptAndContinues`, `AllNoRoutes_ThrowsNoRoute`, and theories for first-call vs after-success credentials/quota/transport/timeout/invalid response/limit failures. Assert exact call counts, retained route/GPX, enum failure and warning codes. A success followed only by NoRoute results is not an incomplete search: all seeds were processed.
- [x] Add deterministic `Deadline_ReturnsPartialOrThrowsTimeout` and `CallerCancellation_AlwaysPropagates` tests, including simultaneous caller cancellation with provider failure and with deadline expiry. Use a small test-local TimeProvider/ITimer implementation plus task signals, not wall-clock sleeps or a new package.
- [x] Run RouteCandidateServiceTests and RouteCandidateRankerTests; verify no extra requests, resource leaks or malformed partial successes.

**Deliverable:** A complete application use case independent of HTTP.

### Task 4: Public HTTP Contract

**Files:** Create RouteCandidatesResponse.cs, RouteCandidatesEndpoints.cs, RoutingProblemMapper.cs and RouteCandidatesEndpointTests.cs. Modify Program.cs and RouteGenerationEndpoints.cs; extend RouteGenerationEndpointTests.cs.

**Interfaces:** Response records in CyclingRoutes.Contracts.RoutePlanning:

```csharp
RouteCandidateAssessmentResponse(double? DistanceDeltaMeters, double? DurationDeltaSeconds, bool TargetsMatched, double Score)
RouteCandidateResponse(int Seed, RouteCandidateAssessmentResponse Assessment, GeneratedRouteResponse Route)
RouteCandidatesResponse(double RequestedLengthMeters, IReadOnlyList<string> Assumptions, int AttemptedCount, IReadOnlyList<string> Warnings, IReadOnlyList<RouteCandidateResponse> Candidates)
```

API registration is `RouteHandlerBuilder MapRouteCandidatesEndpoints(this IEndpointRouteBuilder endpoints)`. Internal shared mapper exposes `(int Status, string Code) Describe(RoutingFailure failure)` and `ProblemHttpResult ToProblem(RoutingFailure failure)`; preserve the existing title/codes/statuses, add 422 search_distance_out_of_range and 422 routing_limit_exceeded. Map IncompleteFailure to its code in response warnings, never expose the enum as another JSON field.

- [x] Write `Candidates_ReturnRankedRoutesWithMatchingGpx` using WebApplicationFactory and a sequence HTTP handler through the real ORS adapter; assert ranked seeds, field names/types, nullable deviations, warnings, attribution and exact geometry-to-GPX point correspondence.
- [x] Run RouteCandidatesEndpointTests; expect 404 before registration.
- [x] Implement DTOs, shared failure mapper and endpoint with the existing validator/ProblemDetails pattern. Register transient RouteCandidateService, singleton stateless RouteCandidateRanker and TimeProvider.System. Advertise 200/400/415/422/502/503/504 responses. Reuse GeneratedRouteResponse without changing it.
- [x] Add validation cases for malformed JSON, wrong numeric types, missing targets and unknown members; assert 400 and zero provider calls. Add unsupported intent/length 422, no key 503, all-no-route 422, first timeout 504, invalid geometry 502 and 2004 limit 422. Test Development and Production handling where request parsing differs.
- [x] Add `PartialFailure_ReturnsSafeWarningAndOriginalGpx`: success then 429 or invalid loop; response is 200, has candidate_generation_incomplete and safe routing code, never provider body/key; no third provider request. Add all-outside-tolerance and duration-only contract cases.
- [x] Extend A-B regression coverage for the shared mapper, including HTTP 504 and 2004; existing successful response and targets_not_optimized warning remain unchanged. Run RouteCandidatesEndpointTests, RouteGenerationEndpointTests and the full suite.

**Deliverable:** New endpoint exercised end-to-end with controlled provider data; no live quota required by CI.

### Task 5: Documentation, Smoke Verification and Review

**Files:** CyclingRoutes.Api.http, new docs/api/route-candidates.md, docs/mvp-roadmap.md and this plan's ledger. Live outputs only under ignored artifacts/routes/.

- [x] Document request/response examples, units, errors, ranking weights, limits, warning meanings and partial-result semantics. Add distance-only and duration-only HTTP samples. Mark stage 5 implemented only after verification, preserving outstanding manual route/device checks from stage 4.
- [x] Run `dotnet restore src/backend/CyclingRoutes.slnx`, then `dotnet build src/backend/CyclingRoutes.slnx --configuration Release --no-restore` and `dotnet test src/backend/CyclingRoutes.slnx --configuration Release --no-build --verbosity minimal`. Record counts and warnings; every command must exit zero. Also run `git diff --check` and inspect the full diff.
- [x] Build `docker build -f src/backend/CyclingRoutes.Api/Dockerfile -t cycling-routes-api:candidates-smoke src/backend`. Start a temporary non-root Production container on an available localhost port without credentials: assert /health=200 Healthy, valid candidates request=503 routing_not_configured, unsupported request=422. Stop/remove only the temporary container.
- [x] With the user's existing User Secrets, start a temporary hidden local Development API process and send exactly one 20000 m road-loop request from (32.0853,34.7818), balanced. At most three real provider calls. Do not automatically repeat failures. Save response and candidate GPX files under ignored artifacts/routes/; report actual metrics, deviations, closure and deduplication. Validate every GPX against the existing official GPX 1.1 XSD. Stop the owned process in finally; do not read/print the key.
- [x] If live calls fail from quota/config/network, record the exact safe failure and leave live qualification open. Passing fixtures are not proof of live provider quality or route safety. Manual map/road and Garmin/Wahoo checks remain user acceptance work.
- [x] Review the entire change for spec coverage, unintended A-B changes, cancellation races, number boundaries and secret leakage; obtain independent review when a reviewer tool is available. Fix findings with regression tests, then rerun affected tests and the full suite. Explain the implemented boundaries and test results in Russian. Do not commit, push, open a PR or merge until requested.

**Deliverable:** Verified local implementation, accurate documentation and a concise learning-oriented explanation of adapter, ranker, orchestrator and endpoint responsibilities.

## Execution Ledger

- Spec approved 2026-09-28. Current branch: oleg/route-candidates.
- Baseline: 98 unit + 69 integration tests passed before implementation.
- Task 1: missing GetRoadLoopAsync reproduced before implementation; provider fixtures now cover payload, coordinate order, 2D/3D parsing, failures and caller cancellation.
- Task 2: missing ranking types reproduced before implementation; 18 ranker cases cover scoring, tolerance boundaries, missing ascent, ties and extreme finite metrics.
- Task 3: missing service reproduced before implementation; 43 service cases cover bounded calls, length policy, deduplication, invalid loops, partial results and deterministic deadline/caller cancellation.
- Task 4: new endpoint first failed with 404; provider-limit mapping first failed with 500 instead of 422. Both are fixed. Existing A-B behavior is covered alongside the new HTTP contract.
- Final verification: restore succeeds; Release build has zero warnings/errors; full no-build suite passes 159 unit + 102 integration = 261 tests. git diff --check is clean. No new dependencies.
- Docker build succeeds. Temporary Production container (UID 1654): health=200 Healthy; candidates without key=503 routing_not_configured; gravel=422 unsupported_intent. Container removed afterward.
- Docker smoke initially failed in the verification harness: PowerShell exposed application/problem+json as byte[], requiring UTF-8 decoding before ConvertFrom-Json. The API's 503 response was correct; no product fix was required.
- Exactly one live 20 km Tel Aviv search: three calls and three unique closed loops, ranked seed 2 (19936.2 m), seed 3 (19876.5 m), seed 1 (20164.7 m). All within distance tolerance; all three GPX files pass the official 1.1 XSD. Temporary API stopped. Artifacts remain ignored under artifacts/routes/tel-aviv-candidates-20260928-181717*.
- Independent read-only review could not finish: the reviewer service hit its usage limit. A full local self-review found no blocking issue, but is not an independent review. No claim of remote CI success for these uncommitted changes.
- Ruling: retain the prepared feature checkout and ignored execution ledger until the separate commit/PR step; do not create another worktree or commit automatically.
- Remaining acceptance: manual Israeli road/access/safety inspection and Garmin/Wahoo import; public authentication/rate limiting before deployment. These are not established by green unit tests or valid GPX XML.

### Review follow-up

- The repeated independent Application review completed. It confirmed an inclusive-tolerance floating-point defect and a loop-validation test gap; the parent review also found masked strict-JSON test cases.
- Red: 4 distance/time boundary cases plus an HTTP regression failed at target 10002 and actual 11002.2/9001.8. Matching now uses capped relative error plus a 1e-15 rounding allowance; metrics, signed deltas and scores remain unrounded. Values 0.00000001 outside either boundary still fail matching.
- Loop tests now separately cover an open four-point path and a closed [A,B,A,B,A] path. Strict-JSON tests start from otherwise valid input and change only the unknown field or numeric string, in both environments.
- Mutation verification: temporarily disabling closure/distinctness checks made both new geometry cases fail. Temporarily permitting unknown members/numeric strings made all four strict-JSON cases fail with 200 instead of 400. All temporary mutations were restored.
- Post-fix verification: 168 unit + 107 integration = 275 tests. No additional live ORS request was needed for these fixes.
