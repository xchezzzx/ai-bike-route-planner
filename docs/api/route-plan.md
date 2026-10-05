# Optional Road Loop Refinement

POST `/api/routes/plan` accepts the same canonical intent as `/api/routes/candidates`.
It is opt-in; `/generate` and interpretation remain unchanged. Both loop-search
endpoints share the bounded [adaptive length calibration](route-candidates.md).

```json
{"start":{"latitude":32.0853,"longitude":34.7818},"shape":"loop","profile":"road","targetDistanceMeters":20000,"elevation":"minimize"}
```

Only road loops are supported. A loop needs a positive distance/time scalar or
explicit range under the [intent contract](route-intent-validation.md). Initial
length uses distance (scalar or range midpoint), otherwise duration (scalar or
range midpoint) at 20 km/h, and must be 1000..100000 metres.
Body limit: 64 KiB including chunked bodies, UTF-8 JSON only. Malformed/domain input
returns 400, oversize 413, unsupported media 415, unsupported planning intent 422.
Repeated JSON properties, including nested coordinates and case-insensitive
aliases, are rejected with 400 before any provider call.

The response is `{ search, advisorCallCount, advisorStatus, advisorFailure, attempts }`.
`search` uses the existing [candidate response](route-candidates.md), including
ranked geometry, metrics, warnings and exact GPX. `search.attemptedCount` counts
actual ORS calls. Each attempt has `seed`, `requestedLengthMeters`, `outcome`,
`reason` and nullable `failure`. A proposal is not an executed attempt.

- Status: `notNeeded`, `skippedNoCandidates`, `skippedRoutingFailure`, `searched`, `stopped`, `failed`.
- Advisor failure: null or `notConfigured`, `authentication`, `quota`, `unavailable`, `timeout`, `invalidResponse`.
- Outcome: `accepted`, `duplicate`, `noRoute`, `failed`.
- Reason: `distance`, `duration`, `elevation`, `explore`, `stop`; actual initial/fallback calls use `explore`.
- Attempt failure: null or the existing application-owned routing problem code.

Seeds 1 and 2 precede advice. A retained road-v1 candidate with balanced elevation skips AI.
No usable initial candidates skip AI and try seed 3. Otherwise one advisor can
stop or propose a fresh seed 3..16 and length within both 1000..100000 metres
and 0.5..1.5 of initial. Invalid proposals are rejected, never clamped. Failure
falls back to seed 3 at the last calibrated length if time remains (`advisor_fallback`).
The second deterministic attempt is also calibrated from the first valid result.
Original targets and advisor proposal validation are unchanged; the trace records
actual requested lengths and the advisor observes those same lengths.

Limits: 3 ORS calls, 1 advisor call, 90 seconds overall, 15 seconds per ORS call,
30 seconds per advisor call. No retries. Caller cancellation takes precedence.
NoRoute consumes a call; other ORS errors stop search and retain prior usable
results with existing partial-result warnings. No usable geometry returns the
existing routing ProblemDetails. This never fabricates a route or changes targets.

Gemini uses the existing `Ai:Gemini:ApiKey` and `Ai:Gemini:Model` settings and a
separate `route-search-v3` input/prompt contract. Its closed `nextSearch` output
shape is unchanged from v2. The dataset filename/version remain
`route-refinement-v2.json`/2; its `contractVersion` and evaluator reports now
identify v3. It receives only preferences,
attempted seeds/lengths, distance/duration/ascent and deviations. Explicit
`targetDistanceRangeMeters` and `targetDurationRangeSeconds` preferences retain
both bounds; they are never replaced by a scalar midpoint. Range deviations
are zero inside and signed to the nearest bound outside. Every explicit range
must match exactly and inclusively; legacy scalars retain 10% tolerance. Coordinates,
raw prompts, GPX, history and keys are excluded from its input. ORS receives the
start coordinate. No free model prose is returned. Structured-output reference:
[Google API documentation](https://ai.google.dev/gemini-api/docs/structured-output).

The provider response is `{ "nextSearch": null }` for stop, or
`{ "nextSearch": { "seed": 3, "requestedLengthMeters": 15000, "reason": "distance" } }`
for search. There is no separate action flag to contradict the search fields.
Both object levels are closed; all search fields are required and non-null.
The schema includes context-specific length bounds and unused seeds. The adapter
maps this into the existing application advice; server policy still validates
every proposal. Legacy flat responses are rejected, not silently repaired.
The output cap is 4096 tokens, with the same 30-second deadline and 256 KiB body
limit. Truncated responses remain failures. Qualification diagnostics contain
only fixed codes and numeric HTTP status, never raw model output or secrets.

The EN/RU/HE UI exposes an unchecked-by-default AI refinement checkbox for ready
road loops and a bounded application-owned attempt trace. `/plan` gets a
100-second client deadline; existing requests retain their 60-second deadline.
Changing inputs or the mode cancels/fences stale responses.

Not a safety/access/traffic guarantee; not support for gravel, stops, exclusions
or geographic reasoning. The first
[live qualification](../evaluation/route-refinement-2026-09-29.md) failed. The latest
[v2 qualification](../evaluation/route-refinement-diagnostics-2026-09-29.md) passed
all six advisor cases, but the four-city comparison remained negative: only Tel
Aviv retained target-matching routes. Adaptive calibration was subsequently tested
offline, not requalified live. Explicit range preferences and the updated advisor
instructions are also offline-tested only. Both `route-search-v3` and extraction
`prompt-interpretation-v4` live requalification are pending; the historical six
v2 advisor passes do not qualify the modified v3 prompt or its range behavior.
The user explicitly accepted merge after green CI
with these known limitations and later manual testing. Keep the feature opt-in
and experimental; do not claim proven route-quality improvement.

## Road quality integration (road-v1)

The nested search now uses the same quality/selection contract as
[/candidates](route-candidates.md). An in-tolerance but surface-excluded route no
longer triggers balanced-elevation early stop. The advisor schema stays metric-only;
it cannot override deterministic exclusions or fabricate geometry. `accepted` in
the attempt trace means valid unique geometry acquired, not a final selectable route.
A stop decision can leave `search.candidates` empty with explained exclusions.
The three-routing/one-advisor budgets and provider-failure handling are unchanged.
The selector also uses the same soft loop near-return penalty and warning as
`/candidates`; exact `road-v1` diagnostics and the metric-only advisor schema stay unchanged.
The evaluator reports `noMatch` for a valid empty selection and `incomplete` when
an upstream failure also occurred. Both retain nonzero qualification exit status;
empty-set best target error is null. No extra calls or automatic retries are added.
