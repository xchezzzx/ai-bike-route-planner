# Optional Road Loop Refinement

POST `/api/routes/plan` accepts the same canonical intent as `/api/routes/candidates`.
It is opt-in; existing `/generate`, `/candidates` and interpretation remain unchanged.

```json
{"start":{"latitude":32.0853,"longitude":34.7818},"shape":"loop","profile":"road","targetDistanceMeters":20000,"elevation":"minimize"}
```

Only road loops are supported. A loop needs a positive distance and/or duration;
initial length is distance or time at 20 km/h and must be 1000..100000 metres.
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

Seeds 1 and 2 precede advice. Matching targets with balanced elevation skip AI.
No usable initial candidates skip AI and try seed 3. Otherwise one advisor can
stop or propose a fresh seed 3..16 and length within both 1000..100000 metres
and 0.5..1.5 of initial. Invalid proposals are rejected, never clamped. Failure
falls back to seed 3 at original length if time remains (`advisor_fallback`).

Limits: 3 ORS calls, 1 advisor call, 90 seconds overall, 15 seconds per ORS call,
30 seconds per advisor call. No retries. Caller cancellation takes precedence.
NoRoute consumes a call; other ORS errors stop search and retain prior usable
results with existing partial-result warnings. No usable geometry returns the
existing routing ProblemDetails. This never fabricates a route or changes targets.

Gemini uses the existing `Ai:Gemini:ApiKey` and `Ai:Gemini:Model` settings and a
separate `route-search-v1` structured contract. It receives only preferences,
attempted seeds/lengths, distance/duration/ascent and deviations. Coordinates,
raw prompts, GPX, history and keys are excluded from its input. ORS receives the
start coordinate. No free model prose is returned. Structured-output reference:
[Google API documentation](https://ai.google.dev/gemini-api/docs/structured-output).

The EN/RU/HE UI exposes an unchecked-by-default AI refinement checkbox for ready
road loops and a bounded application-owned attempt trace. `/plan` gets a
100-second client deadline; existing requests retain their 60-second deadline.
Changing inputs or the mode cancels/fences stale responses.

Not a safety/access/traffic guarantee; not support for gravel, stops, exclusions
or geographic reasoning. Offline tests and review fixes pass, but the first
[live qualification](../evaluation/route-refinement-2026-09-29.md) failed. Keep
the PR unmerged and the feature experimental; do not claim quality improvement.
# Road quality integration (road-v1)

The nested search now uses the same quality/selection contract as
[/candidates](route-candidates.md). An in-tolerance but surface-excluded route no
longer triggers balanced-elevation early stop. The advisor schema stays metric-only;
it cannot override deterministic exclusions or fabricate geometry. `accepted` in
the attempt trace means valid unique geometry acquired, not a final selectable route.
A stop decision can leave `search.candidates` empty with explained exclusions.
The three-routing/one-advisor budgets and provider-failure handling are unchanged.

