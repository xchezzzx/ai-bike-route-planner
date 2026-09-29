# Stage 6b: Bounded AI Route Refinement

Status: design and execution approved by the user on 2026-09-29.
The [implementation plan](../plans/2026-09-29-agentic-refinement.md) tracks delivery;
implementation is on the feature branch, with live qualification and merge pending.
The user wants AI involvement in route generation, not only prompt parsing,
within the existing .NET modular monolith, React UI and free-service constraints.

## Scope and alternatives

First slice: opt-in refinement of road loops using observed provider metrics.
Point-to-point routing, gravel, via points, stops, geographic exclusions,
traffic/safety claims, persistence and authentication are outside this slice.
This is not full geographic reasoning: the current routing tool accepts a start,
length and seed, not arbitrary constraints. Surface that limit explicitly.

- Deterministic three-seed search is the current baseline: cheap and testable,
  but AI never influences route construction.
- Recommended: one structured advisor decision between bounded routing calls.
  Reuse HttpClient and existing application/provider interfaces, without another
  framework, service or package; keep call budgets explicit and testable.
- A general agent framework/tool loop is a later option when multiple tools or
  conversation state justify it. Server-side guards remain necessary regardless.

Success requires measured improvement on a fixed route-quality set at the same
routing-call budget, or a documented negative result. Calling an LLM alone is
not evidence of improved routes.

## Flow and budgets

1. Validate canonical RouteIntent with existing domain/capability rules. Do not
   repeat prompt interpretation inside planning.
2. Request loops for seeds 1 and 2 with the current initial length policy.
   Validate geometry and deduplicate as in the existing service.
3. If targets already match and elevation is balanced, return ranked candidates
   without an advisor call. Otherwise send one summary to the advisor.
4. Advisor returns stop or one proposal: a fresh seed in 3..16 and finite length
   within 1000..100000 metres AND 0.5..1.5 of initial length. Reject invalid
   proposals; never clamp them or rewrite the user's requested targets.
5. Execute at most one additional ORS call. Rank all accepted unique candidates
   using the original intent and existing RouteCandidateRanker. Keep prior
   usable routes; the model cannot delete them or override deterministic ranking.

Hard limits per HTTP request: three ORS calls, one advisor call, 90 seconds total.
Keep 15-second ORS and 30-second Gemini per-call limits. An overall linked deadline
covers all calls and parsing. No automatic retries, background continuation,
arbitrary URLs or model-selected tools. Caller cancellation wins over partial
results. Check cancellation before/after calls and before returning; no new call
may start after the deadline.

NoRoute consumes an ORS attempt. Other routing failures stop new calls and use
the current partial-result/failure policy. With no initial usable candidates,
skip the advisor and use the third deterministic attempt. Advisor failure,
timeout or invalid proposal falls back to seed 3 at the original length if time
remains, with an explicit advisor failure code; this is not a model retry.
Advisor stop means no third call. Never fabricate geometry when no route exists.

## Boundaries and contracts

- Domain stays unchanged and unaware of AI.
- Application: RoutePlanningService owns budgets, proposal validation, search,
  deduplication, ranking and trace. IRouteSearchAdvisor consumes immutable
  original preferences and attempt summaries, returning one bounded proposal.
- Infrastructure: separate versioned Gemini advisor contract/parser/adapter,
  strict schema, size/deadline limits and sanitized failures. Reuse transport
  patterns, not the interpretation system instruction.
- API: proposed POST /api/routes/plan with the existing intent DTO. Existing
  /generate and /candidates contracts remain unchanged. Reuse strict request
  size, JSON, cancellation and ProblemDetails conventions before provider work.
- Response: keep candidate route/metrics/GPX shapes; add initial requested
  length, routing/advisor call counts, advisor status, warnings and bounded
  attempt trace (seed, requested length, outcome, application-owned reason code).
  A proposed search must not count as an executed provider call.
- Frontend: opt-in AI refinement for supported road loops; deterministic search
  remains the default. Preserve explicit Generate, cancellation, stale-response
  handling, EN/RU/HE, RTL and exact selected-candidate GPX download.

Advisor input contains profile/elevation/shape, targets, attempted seeds/lengths,
observed distance/duration, nullable ascent and target deviations. It excludes
precise coordinates, raw prompts, geometry/GPX, secrets, user history and arbitrary
provider text. Only the backend passes coordinates to ORS. Reason codes are fixed
(distance, duration, elevation, explore, stop); display messages are application
owned, not model prose or claims about legal access, fitness, safety or surfaces.

## Verification gates

- TDD: proposal parsing/bounds, stop, no observations, duplicates, no route,
  invalid advisor response, all failure/fallback paths, partial results,
  timeout/cancellation races, immutable intent, call counts and accurate trace.
- Inspect actual serialized advisor requests for absence of coordinates,
  prompts/secrets, network tools and retries; pin schema and version.
- API request-limit and old-endpoint regressions; UI opt-in, fallback, stale
  results, RTL and selected GPX tests.
- A bounded synthetic live advisor corpus covering stop/search and adversarial
  inputs, separate from the interpretation corpus and fake-provider tests.
- Compare deterministic and advised search on identical Israeli starts/targets,
  at most three ORS calls each. Record target error, usable/unique candidates,
  ascent availability, quota, latency and failures. Unavailable runs are not
  passes. Do not run live providers in CI or enable billing.
- Ship opt-in only after offline tests, independent review, both CI workflows
  and bounded live checks. Default-on or quality-improvement claims require
  positive comparison evidence, not this design document.
- Field/access checks and Garmin/Wahoo import remain human acceptance. Public
  deployment and account changes are outside this spec.

Next: finish bounded live qualification, independent review and CI gates.

## Approved provider-contract correction

On 2026-09-29 the user approved a local-only correction after a bounded diagnostic
run identified `stop` with a non-null requested length. The internal Gemini
contract became `route-search-v2`: one required `nextSearch` field, either null
(stop) or a closed object containing required seed, requestedLengthMeters and
reason (search). This removes contradictory action/search fields. Mapping to
application advice and the public HTTP contract remain unchanged, as do all
proposal bounds, deadlines, no-retry rules and fallback behavior.

Qualification uses `route-refinement-v2.json`; the v1 corpus and negative reports
remain historical evidence. The user authorized only six additional Gemini
calls and no ORS calls; all six were used on v1 diagnosis. No live calls are
authorized for v2 in this change. The v2 corpus and corrected four-city comparison
remain required before merge; offline success cannot replace them.

Subsequent approval allowed up to 10 Gemini and 24 ORS calls. The first v2 run
used two Gemini calls: stop passed, then search received HTTP 503 and execution
stopped. No ORS comparison or automatic retry followed. See the diagnostic
record for the remaining allowance; qualification and merge remain pending.

## Explicit range revision

The distance/time range extension uses `route-search-v3` for the advisor input
and prompt. The v2 `nextSearch` output shape, policy, budgets and safety exclusions
are unchanged. Current corpus metadata and reports identify v3 while keeping
the dataset filename/version `route-refinement-v2.json`/2. The previous v2
qualification is historical evidence only. Both advisor v3 and interpretation
`prompt-interpretation-v4` require new live qualification; none was run for this
extension. See the [current API runbook](route-plan.md).
