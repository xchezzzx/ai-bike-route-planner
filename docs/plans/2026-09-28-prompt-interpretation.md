# Prompt Interpretation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Interpret EN/HE/RU cycling prompts through Gemini into validated preferences or explicit clarifications, without generating route geometry.

**Architecture:** A single replaceable interpreter returns incomplete structured preferences. Application policy combines them with caller-selected coordinates, validates them, and reports limitations. API translates outcomes; Infrastructure owns the Gemini protocol.

**Tech Stack:** Existing .NET 10, ASP.NET Core, System.Text.Json, HttpClient, xUnit v3/VSTest; no new packages.

**Spec:** [Approved design](../api/prompt-interpretation-design.md).

**Status:** Approved by the user on 2026-09-28; implementation and verification in progress.

**Execution:** Native implementation in this thread, logical local commits, then an independent whole-branch review. Do not push, create a PR, or merge. Update checkboxes only when the corresponding evidence exists.

## Global Constraints

- Existing validation, A-B generation, loop candidates, and GPX contracts stay unchanged.
- No frontend, database, account system, geocoding, Agent Framework, or new model SDK is needed here.
- Domain remains unchanged and knows nothing of LLMs.
- Required exact token en, he, or ru; prompt at most 4000 UTF-16 code units.
- Set an endpoint request-body limit of 64 KiB, including chunked bodies.
- At most 16 issue entries are allowed, deduplicated after parsing.
- One valid HTTP request makes at most one provider call.
- Use a 30-second deadline covering response reading and a 256 KiB response-body limit.
- Caller cancellation must propagate and take precedence over timeout translation.
- No tools, browsing, file inputs, grounding, or automatic retries.
- No live calls until credentials/model are configured intentionally.
- Keep each code commit buildable with its tests.

All paths below are repository-relative; commands run from the repository root.
The solution is `src/backend/CyclingRoutes.slnx`, not `.sln`.

## Review Focus

1. Oversized chunked JSON without Content-Length must be rejected before model work (Task 3).
2. A provider can stall after sending headers; the deadline must cover body reads (Task 2).
3. Duplicate JSON keys can override an earlier value; extracted data must reject them (Task 2).
4. An ambiguous value and a concrete value may coexist; the draft may show it but intent must remain null (Task 1).
5. Unicode prompts and coordinate/target bounds must behave consistently under different current cultures (Tasks 1, 3, 4).

## Shared Verification

Baseline and before every code commit:

```powershell
dotnet test src/backend/CyclingRoutes.slnx --configuration Release --no-restore --maxcpucount:1 --verbosity minimal
git diff --check
```

Baseline from the preceding stage is 275 tests; rerun rather than relying on that
number. Each task also runs a filtered red/green test cycle. Keep the failure
output showing a missing behavior before implementation and the passing output
afterward. Tests need neither a running API nor live keys.

### Task 1: Contracts and Interpretation Policy

**Files:**
- Create `src/backend/CyclingRoutes.Contracts/RoutePlanning/InterpretRouteIntentRequest.cs`.
- Create `src/backend/CyclingRoutes.Contracts/RoutePlanning/InterpretRouteIntentResponse.cs`.
- Create `src/backend/CyclingRoutes.Application/Interpretation/IRouteIntentInterpreter.cs`.
- Create `src/backend/CyclingRoutes.Application/Interpretation/RouteIntentExtraction.cs`.
- Create `src/backend/CyclingRoutes.Application/Interpretation/InterpretationException.cs`.
- Create `src/backend/CyclingRoutes.Application/Interpretation/InterpretationResult.cs`.
- Create `src/backend/CyclingRoutes.Application/Interpretation/InterpretationService.cs`.
- Create `src/backend/CyclingRoutes.Application/Interpretation/ClarificationMessages.cs`.
- Test `src/backend/CyclingRoutes.Tests.Unit/RoutePlanning/InterpretationServiceTests.cs`.

**Interfaces:**
- `InterpretRouteIntentRequest`: nullable Prompt, Locale strings and Start, Destination CoordinateRequest properties; disallow unmapped JSON properties.
- `InterpretRouteIntentResponse(string Status, RouteIntentRequest Draft, RouteIntentResponse? Intent, IReadOnlyList<ClarificationResponse> Clarifications, IReadOnlyList<string> Limitations, IReadOnlyList<string> Assumptions)`.
- `ClarificationResponse(string Field, string Code, string Message)`.
- `ExtractionIssue(string Field, string Code)`; `RouteIntentExtraction(string? Shape, string? Profile, string? Elevation, double? TargetDistanceMeters, long? TargetDurationSeconds, IReadOnlyList<ExtractionIssue> Issues)`.
- `IRouteIntentInterpreter.InterpretAsync(string prompt, string locale, CancellationToken cancellationToken)` returns `Task<RouteIntentExtraction>`.
- `InterpretationResult(InterpretRouteIntentResponse? Response, IReadOnlyDictionary<string, string[]> Errors)` separates bad request envelopes from successful interpretation outcomes.
- `InterpretationService(IRouteIntentInterpreter interpreter, RouteIntentValidator validator).InterpretAsync(InterpretRouteIntentRequest request, CancellationToken cancellationToken)` returns `Task<InterpretationResult>`.
- `InterpretationFailure`: NotConfigured, CredentialsRejected, RateLimited, Unavailable, Timeout, RequestRejected, InvalidResponse. `InterpretationException(InterpretationFailure failure)` exposes Failure and a generic message without upstream data or an inner exception.
- `ClarificationMessages.Get(string locale, string field, string code)` returns application-owned text.

- [x] **1. Write failing policy tests.** Use an in-file counting fake interpreter recording prompt/locale/token; never infer language by substring in that fake. Pin these assertions in named tests:

```csharp
// CompleteLoop_ReturnsValidatedIntentAndExplicitDefault
Assert.Equal("ready", result.Response!.Status);
Assert.Equal(20000, result.Response.Intent!.TargetDistanceMeters);
Assert.Contains("elevation_balanced", result.Response.Assumptions);
// AmbiguousDistance_PreservesDraftButBlocksIntent
Assert.Equal(20000, result.Response!.Draft.TargetDistanceMeters);
Assert.Null(result.Response.Intent);
Assert.Contains(result.Response.Clarifications, x => x.Code == "ambiguous");
// MissingTargets_ProducesOneQuestion
Assert.Single(result.Response!.Clarifications.Where(x => x.Code == "target_required"));
```

Add cases for 4000/4001 code units, whitespace, missing/invalid locale, invalid
provided coordinates, and zero calls on invalid envelopes. Test missing start,
A-B destination missing/equal to start, loop with destination, negative/zero
targets, TimeSpan bounds, both targets preserved, duration-only and balanced
default. Test each of the three limitation codes at 999/1000/100000/100001 meters,
and duration-only boundaries at 179/180/18000/18001 seconds. Compare the latter
to the existing service's 20000/3600 length policy, not a second speed assumption.

Pin ambiguity suppression, prompt-level issues, unsupported-preference status,
issue deduplication, field/code ordering, locale-specific messages, and caller
cancellation before/after a fake provider response. Run representative numeric
cases under en-US, ru-RU, and he-IL, restoring culture after each test.

- [x] **2. Run red.** `dotnet test src/backend/CyclingRoutes.Tests.Unit --configuration Release --no-restore --maxcpucount:1 --filter FullyQualifiedName~InterpretationServiceTests` must fail for missing new types/behavior, not unrelated infrastructure.
- [x] **3. Implement the declared contracts, fake-independent service, and localized messages.** Preserve caller coordinates; use RouteIntentValidator for domain invariants. Build a validation copy with ambiguous/invalid preferences cleared while retaining the original draft. Clear an elevation assumption when an issue makes elevation unresolved. Collapse missing-target errors and suppress redundant required questions. Unsupported model issues block intent; capability limitations may retain a validated intent. No domain/API dependency or ORS call in this service.
- [x] **4. Run green and full shared verification.** Assert that every known field/code combination used by the service has a nonempty EN/HE/RU template and localized coordinate component labels. Do not translate machine tokens.
- [x] **5. Commit only this task's files.** Message: `feat: add prompt interpretation contracts and clarification policy`.

### Task 2: Strict Gemini HTTP Adapter

**Files:**
- Create `src/backend/CyclingRoutes.Infrastructure/Interpretation/GeminiOptions.cs`.
- Create `src/backend/CyclingRoutes.Infrastructure/Interpretation/GeminiRouteIntentInterpreter.cs`.
- Create `src/backend/CyclingRoutes.Infrastructure/Interpretation/GeminiExtractionContract.cs`.
- Create `src/backend/CyclingRoutes.Infrastructure/Interpretation/GeminiResponseParser.cs`.
- Test `src/backend/CyclingRoutes.Tests.Integration/GeminiRouteIntentInterpreterTests.cs`.

**Interfaces:**
- Consume Task 1 interpreter, extraction, issue, and failure types.
- `GeminiOptions` exposes init-only ApiKey and Model strings, default empty.
- `GeminiRouteIntentInterpreter(HttpClient client, GeminiOptions options, TimeProvider timeProvider)` implements IRouteIntentInterpreter.
- Internal `GeminiExtractionContract`: Version constant, system instructions, and JSON schema, colocated so tests can inspect the actual outgoing schema. Version changes whenever prompt/schema semantics change.
- Internal `GeminiResponseParser.Parse(string body)` returns RouteIntentExtraction or throws a safe InterpretationException.

- [x] **1. Write failing adapter tests.** Use a local stub HttpMessageHandler, not the ORS fixture. Test the actual serialized request and response parser through the public interpreter method. Assert exactly one POST to Google's fixed v1beta generateContent endpoint, a header key and no query key, no tools, candidateCount=1, responseMimeType=application/json, responseJsonSchema, maxOutputTokens=4096, separate system/user messages, and no start/destination fields.

Pin these cases: empty key/model, illegal model ID, CR/LF in a key (NotConfigured,
no network); 401/403/429/5xx/transport errors; other non-success; successful STOP
with one text part; missing/multiple candidates or parts; safety blocks;
truncated/fenced JSON; unknown envelope metadata accepted; unknown extraction
properties rejected; duplicate properties at every extraction object level;
missing properties, invalid enums, strings for numbers, fractional/overflowing
duration, non-finite distance, 16/17 issues. Negative targets must reach Application.

Use bounded, streaming test content rather than only StringContent:

```csharp
// OversizedBodyWithoutContentLength_IsInvalidResponse
Assert.Equal(InterpretationFailure.InvalidResponse, error.Failure);
// HeadersThenStalledBody_ObservesDeadline
Assert.Equal(InterpretationFailure.Timeout, error.Failure);
// CallerCancellationTakesPrecedence
Assert.True(callerToken.IsCancellationRequested);
Assert.IsAssignableFrom<OperationCanceledException>(error);
```

Test sizes 262144/262145 bytes with otherwise valid JSON plus whitespace. Test
simultaneous cancellation/deadline and cancellation after body receipt. A local
manual TimeProvider test helper can follow the existing candidate-service tests;
do not sleep for 30 seconds or add a time-testing package.

- [x] **2. Run red.** `dotnet test src/backend/CyclingRoutes.Tests.Integration --configuration Release --no-restore --maxcpucount:1 --filter FullyQualifiedName~GeminiRouteIntentInterpreterTests` must fail on the new behavior.
- [x] **3. Implement adapter and extraction contract.** Use ResponseHeadersRead and an explicitly bounded read (at most limit plus one sentinel byte); enforce both declared Content-Length and actual bytes. Use a linked 30-second CancellationTokenSource with TimeProvider through send and reads. Validate config before constructing the request. Strictly parse extraction objects using structured JSON APIs and detect duplicate property names before deserialization. Reject extra actions/non-text candidate content; map documented safety finish/block reasons only. Treat other incomplete finish reasons as InvalidResponse. Do not store raw error bodies in exceptions or logs.
- [x] **4. Run green and full shared verification.** Verify exceptions contain neither test key nor injected private detail. Verify disposed responses/streams and no retry on failure.
- [x] **5. Commit task files.** Message: `feat: add bounded Gemini structured extraction adapter`.

### Task 3: API Integration and Request Limits

**Files:**
- Create `src/backend/CyclingRoutes.Api/RoutePlanning/InterpretRouteIntentEndpoints.cs`.
- Create `src/backend/CyclingRoutes.Api/RoutePlanning/InterpretRequestReader.cs`.
- Create `src/backend/CyclingRoutes.Api/RoutePlanning/InterpretationProblemMapper.cs`.
- Modify `src/backend/CyclingRoutes.Api/Program.cs` and `CyclingRoutes.Api.http`.
- Test `src/backend/CyclingRoutes.Tests.Integration/InterpretRouteIntentEndpointTests.cs`.

**Interfaces:**
- `MapInterpretRouteIntentEndpoints(this IEndpointRouteBuilder endpoints)` returns RouteHandlerBuilder for POST /api/route-intents/interpret.
- Internal `InterpretRequestReader.ReadAsync(HttpRequest request, CancellationToken cancellationToken)` returns `Task<InterpretRequestReadResult>`.
- Internal `InterpretRequestReadResult(InterpretRouteIntentRequest? Request, int? ErrorStatus)`; exactly one side is populated. Error statuses are 400, 413, 415.
- `InterpretationProblemMapper.ToProblem(InterpretationFailure failure)` returns ProblemHttpResult with the spec's HTTP status and stable code extension.

- [x] **1. Write failing endpoint tests.** Use WebApplicationFactory in both Development and Production, replacing the interpreter or its HTTP handler. Cover ready, needsClarification, unsupported, every failure mapping, missing config, startup health, strict unknown fields/numeric strings, wrong content type, invalid envelope before provider, and caller cancellation. Assert no ORS calls and no geometry/gpx fields. Complete responses must serialize coordinates, enums, units, clarifications, limitations, and assumptions as specified.

```csharp
// ChunkedBodyOverLimit_Returns413BeforeInterpreter
Assert.Equal(413, (int)response.StatusCode);
Assert.Equal(0, interpreterCalls);
// MissingCoordinates_IsClarificationNotBadRequest
Assert.Equal(200, (int)response.StatusCode);
Assert.Equal("needsClarification", body.Status);
Assert.Null(body.Intent);
```

Construct a body exactly 65536 bytes with a valid short prompt plus JSON whitespace
and one at 65537; test with and without Content-Length. Test 4000 UTF-16 units as
literal UTF-8 and escaped JSON; neither representation changes prompt validation.
Assert byte and character limits are independent. Test an otherwise valid unknown
property/numeric-string request so a missing required field cannot mask a regression.
Pin OpenAPI request-body/response metadata for the manually read endpoint.

- [x] **2. Run red.** `dotnet test src/backend/CyclingRoutes.Tests.Integration --configuration Release --no-restore --maxcpucount:1 --filter FullyQualifiedName~InterpretRouteIntentEndpointTests` must fail, initially with endpoint absent.
- [x] **3. Implement reader, endpoint, DI, and safe problems.** Manually read the bounded body before JSON deserialization instead of an endpoint filter that runs after model binding. Accept JSON media types including +json, require UTF-8 (or no charset), reject unsupported charset with 415, and parse with configured strict HTTP JSON options. RequestAborted is never translated into an AI timeout. Use explicit Accepts/Produces metadata. Register options from Ai:Gemini:ApiKey/Model, typed client with redirects disabled, TimeProvider.System already registered, and transient interpretation service. Adapter owns its deadline; use an infinite HttpClient timeout to avoid competing timers. Never validate missing configuration at startup.
- [ ] **4. Run green, full shared verification, and container smoke.** Build the existing Dockerfile. Run an isolated loopback-bound container without keys; check health 200, valid interpretation request 503 ai_not_configured, malformed request 400. Remove only this smoke container afterward; do not leave a configured API exposed. Add synthetic EN/HE/RU examples to the existing .http file without keys.
- [x] **5. Commit task files.** Message: `feat: expose prompt interpretation API with strict request limits`.

### Task 4: Evaluation Corpus, Runbook, and Delivery Review

**Files:**
- Create `docs/evaluation/prompt-interpretation-v1.json`.
- Create `tools/evaluate-prompts.ps1`.
- Create `tools/tests/evaluate-prompts.tests.ps1` (plain PowerShell assertions and a loopback HTTP stub; no Pester dependency).
- Create `docs/api/prompt-interpretation.md`.
- Create `src/backend/CyclingRoutes.Tests.Integration/PromptEvaluationCorpusTests.cs`.
- Modify Integration `.csproj` to copy the corpus as test content using a relative link.
- Modify `README.md`, `docs/mvp-roadmap.md`, and this plan's execution evidence.

**Interfaces:**
- Corpus object: version, contractVersion, and cases array. Each case: id, core boolean, request (Task 1 DTO), expectedStatus, expectedFields (explicit JSON field/value pairs for draft or intent), expectedClarifications (field/code pairs), expectedLimitations, expectedAssumptions. Use exact expected arrays, not substring matching. ExpectedFields paths permit only known draft/intent fields, never arbitrary evaluation expressions. Pin contractVersion against the version sent in the actual adapter system instructions in an adapter test.
- Script parameters: `-BaseUrl`, `-ModelId`, `-RunLive`, `-OutputPath`, optional `-CorpusPath` defaulting to the checked-in corpus; without RunLive it validates the corpus and makes zero HTTP calls. Live mode calls the API endpoint, not Google directly. ModelId is operator-declared API configuration and is labeled as such in reports, not falsely presented as provider-verified metadata.
- Reports contain corpus and prompt/schema version, declared model ID, case comparisons, latency, and sanitized errors; nonzero exit on any mismatch, incomplete run, or missing report. Write reports under ignored artifacts by default. Never read or display User Secrets.

- [x] **1. Write failing corpus tests.** Require unique IDs, exactly six core cases per locale (18 total), all three locales, valid envelopes, explicit expectations, and no unknown expectation paths. Cover core complete loop, duration-only, missing preferences, ambiguity, unsupported stop, and injection for each locale. Add A-B/gravel/named location/mixed units/zero/negative/conflicting target cases beyond the core set. Expectations must satisfy domain invariants when declaring ready. These tests validate fixtures, not model understanding.
- [x] **2. Run red.** `dotnet test src/backend/CyclingRoutes.Tests.Integration --configuration Release --no-restore --maxcpucount:1 --filter FullyQualifiedName~PromptEvaluationCorpusTests` must fail while corpus/content are absent.
- [x] **3. Author corpus, runner, and runbook.** Use real EN/HE/RU text (Unicode is required here). Make live execution sequential, one request per case, no retries; stop on quota or credentials failure and mark remaining cases unrun. Require a loopback BaseUrl in this MVP runner to avoid sending synthetic data or calls to arbitrary servers by mistake. Do not count unrun/error cases as passes. Runbook explains selecting a currently free structured-output model, configuring secrets without committing them, privacy, no billing automation, static fake-test limitations, and the resubmit-full-prompt clarification workflow.
- [x] **4. Run green and offline runner checks.** `pwsh -NoProfile -File tools/evaluate-prompts.ps1` must report a valid corpus without requiring any server/key. `pwsh -NoProfile -File tools/tests/evaluate-prompts.tests.ps1` must exit 0 after checking invalid corpus/expectations, ready/mismatch reports, auth/quota early stop, and exit codes through its loopback HTTP stub. The harness cleans up its own listener and temporary reports in finally. Run full shared verification. Reconcile README/roadmap with actual delivered behavior, keeping stage 6b and manual road/device checks open.
- [ ] **5. Perform live qualification only when configured intentionally.** Verify current official model/pricing documentation, then run the explicit opt-in corpus using synthetic prompts only. If credentials/model are unavailable, leave this checkbox incomplete and report live qualification as blocked on configuration, not passed. Do not install a model, create an account, or enable billing to satisfy this step.
- [ ] **6. Request an independent whole-branch review.** Read the requesting-code-review skill; reviewer checks the complete diff against spec and this plan, including privacy, status semantics, request/response bounds, cancellation, fixture honesty, and existing route regressions. Implement confirmed fixes with focused regression tests and rerun the full suite. If reviewer tooling is unavailable, state that limitation and perform an explicit self-review without claiming independence.
- [ ] **7. Commit task files and any separate review fixes.** Message: `test: add multilingual interpretation evaluation and runbook`. Report exact commit hashes, fresh test counts, container evidence, remaining live qualification, and clean git status. No push/PR/merge.

## Execution Evidence

- Baseline: 275 tests passed before implementation.
- Task 1: RED missing new types; GREEN 315 tests; commit e9a511a.
- Task 2: RED missing adapter; GREEN 361 tests; commit c13fb27.
- Task 3: RED 28 endpoint tests (endpoint absent); GREEN 389 tests; commit 851b5d2.
- Task 3 container: compilation passed with zero warnings/errors, Docker daemon
  disconnected during publish (EOF). The engine pipe remains unavailable on
  retry. Container runtime smoke is not verified; step 4 remains open for that reason.
- Task 4: RED missing corpus and missing runner; GREEN 390 .NET tests plus
  PowerShell runner tests with a loopback stub. Stub results are not Gemini results.
- Real local Kestrel smoke passed without keys: health 200, interpretation 503,
  malformed JSON 400, oversized JSON 413. The temporary process was stopped.
  This verifies host behavior but does not replace the missing Docker smoke.
- No live model evaluation: credentials/model have not been intentionally configured
  for this task. No accounts, billing changes, public deployment, push or PR.
- Independent review requested; findings and final verification will be recorded below.
