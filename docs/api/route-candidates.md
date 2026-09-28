# Road loop candidates

POST /api/routes/candidates generates up to three provider-backed road loops and
ranks unique results. This is a bounded search, not a guarantee of an optimal,
distinct-road, safe or target-matching route. The existing A-B endpoint remains
available; see [point-to-point generation](route-generation.md).

## Request

Uses the same strict [intent contract](route-intent-validation.md). Only loop
and road are supported here. Elevation can be balanced (default), minimize or
seekClimbs. No destination is permitted for a loop.

```json
{
  "start": { "latitude": 32.0853, "longitude": 34.7818 },
  "shape": "loop",
  "profile": "road",
  "targetDistanceMeters": 20000,
  "elevation": "balanced"
}
```

Distance and/or targetDurationSeconds are required. A duration-only example:

```json
{
  "start": { "latitude": 32.0853, "longitude": 34.7818 },
  "shape": "loop",
  "profile": "road",
  "targetDurationSeconds": 3600,
  "elevation": "minimize"
}
```

Distance, when supplied, determines requested loop length. Otherwise length is
seconds * 20000 / 3600, with the explicit assumption initial_speed_20_kmh. This
initial search assumption is not a rider fitness estimate. Ranking always uses
the provider's estimated duration. Lengths outside 1000..100000 metres return
422 before network work; they are never clamped. The lower bound is our MVP
policy; the upper bound follows the hosted ORS round-trip limit.

## Response

200 always contains a nonempty candidates array in ranked order.

| Field | Meaning |
| --- | --- |
| requestedLengthMeters | Initial preferred loop length sent to ORS |
| assumptions | initial_speed_20_kmh for duration-only input; otherwise empty |
| attemptedCount | Started provider calls, including failed and duplicate results; 1..3 |
| warnings | Search-level machine-readable codes |
| candidates[].seed | Originating provider seed, not a persistent route ID |
| candidates[].assessment.distanceDeltaMeters | Actual minus target; null if distance not requested |
| candidates[].assessment.durationDeltaSeconds | Actual minus target; null if time not requested |
| candidates[].assessment.targetsMatched | Every supplied target within inclusive 10% tolerance |
| candidates[].assessment.score | 0..1, lower is better; not a probability/confidence |
| candidates[].route | Same object shape as GeneratedRouteResponse: geometry, metrics, attribution, warnings, gpx |

Each candidate carries its own GPX 1.1 string. Save that string as UTF-8 without
another provider request. GPX preserves the returned point order and optional
elevation, with the existing +180 to -180 longitude normalization. No synthetic
closing segment, timestamps or speed are added. Closure is checked by exact
coordinate value, ignoring elevation; at least three distinct positions are
required. The provider may snap the start onto its road graph.

## Search and ranking

The server asks ORS for seeds 1, 2 and 3 sequentially, using the same preferred
length and round_trip.points=3. Exact coordinate sequences and their reversals
are deduplicated, ignoring elevation, keeping the earliest seed. Partial road
overlap and out-and-back sections are allowed. Graph updates can change results.

For each supplied target, calculate abs(actual - target) / target. The mean of
these errors, each capped at 1 for scoring, is targetError. Signed deviations
in the response are not capped. Unknown targets do not participate.

The inclusive 10% comparison allows 1e-15 of relative floating-point roundoff
at the boundary. This does not round route metrics, signed deviations or scores.

- balanced: score = targetError; ascent does not affect the score.
- minimize: score = 0.8 * targetError + 0.2 * ascent / maxKnownAscent.
- seekClimbs: score = 0.8 * targetError + 0.2 * (1 - ascent / maxKnownAscent).

When all known ascents are zero, their elevation penalty is zero. Missing ascent
gets penalty 1 and elevation_data_unavailable for minimize/seekClimbs; it is
never treated as zero ascent. Normalization happens after deduplication.

Matching candidates precede all nonmatching candidates, regardless of score.
Within each group, sort by score then seed. Elevation preference selects among
the sampled candidates; it does not change road access rules or guarantee a
specific ascent. Conflicting time/distance requests are not silently rewritten.

## Warnings and failures

| Warning | Location and meaning |
| --- | --- |
| candidate_search_limited | Always at search level; only a small sample was explored |
| candidate_generation_incomplete | Search stopped because of a provider failure/deadline, but usable results exist |
| routing_* code | Search-level safe failure reason accompanying incomplete results |
| no_candidate_within_tolerance | None of the returned candidates meets every target |
| targets_not_met | Individual route misses at least one target |
| elevation_data_unavailable | Individual route lacks ascent for minimize/seekClimbs ranking |

NoRoute consumes an attempt and continues. Other provider failures stop new
calls. With previous usable results, return 200, rank those results and include
candidate_generation_incomplete plus the public failure code. Three processed
seeds with duplicates or NoRoute are not an interrupted search. Do not retry
automatically to fill the list.

Without any usable candidate, return ProblemDetails with code:

| HTTP | Codes |
| --- | --- |
| 400 | Existing field-validation/binding semantics |
| 415 | Unsupported content type |
| 422 | unsupported_intent, search_distance_out_of_range, route_not_found, routing_limit_exceeded |
| 502 | routing_invalid_response |
| 503 | routing_not_configured, routing_credentials_rejected, routing_rate_limited, routing_unavailable |
| 504 | routing_timeout |

ORS numeric error 2004 becomes routing_limit_exceeded. Never expose upstream
bodies or credentials. Per-call timeout remains 15 seconds and buffered response
size 8 MiB. A linked 45-second overall deadline cooperatively cancels search;
already accepted candidates can be returned with a timeout warning. Caller
cancellation always propagates instead of becoming a 200 or provider timeout.

## Configuration and verification

Use the existing server-side Routing:OpenRouteService:ApiKey in User Secrets.
Do not send it from the client, commit it, or expose this development API
publicly before adding authentication/rate limits. No new service or package
is required; live search uses up to three requests of the existing ORS quota.

Local verification on 2026-09-28: one 20 km Tel Aviv search returned three unique
closed candidates in ranked order: seed 2, 19936.2 m; seed 3, 19876.5 m; seed 1,
20164.7 m. All matched distance tolerance. All three GPX files passed the
official GPX 1.1 XSD. Outputs are ignored under artifacts/routes/, with prefix
tel-aviv-candidates-20260928-181717. Automated tests use no live API key.

Manual road/access/safety checks and Garmin/Wahoo import remain outstanding.
Israel is the initial test region, not an enforced geofence. See the
[approved design and provider sources](route-candidates-design.md).
