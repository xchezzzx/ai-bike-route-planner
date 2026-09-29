# Loop candidates and ranking

Status: approved by the user on 2026-09-28; implementation plan also approved.
See [the implementation plan](../plans/2026-09-28-route-candidates.md) for execution evidence.
The existing generation API remains available.

This records the original scalar design. The current [API contract](route-candidates.md)
also supports exact inclusive distance/time ranges, midpoint initialization and
adaptive calibration; those additions supersede the original fixed-length and
scalar-only search/ranking descriptions below. Scalar tolerance remains unchanged.

## Agreed intent

Generate several provider-backed road loops from a start location, compare them
against distance/time/elevation preferences, and return inspectable routes with
GPX and explicit trade-offs. The user approved 20 km/h as the initial length
estimate for duration-only requests. This is not an estimate of the rider's speed.

The recommended approach is native ORS round trips with a bounded number of
different seeds, followed by application-side ranking. Constructing our own
intermediate waypoints offers more control but adds geometry/search complexity;
adaptive length refinement can follow once this first bounded search is measured.

## Scope and compatibility

- Add POST /api/routes/candidates. Do not change the existing /api/routes/generate
  response or its point-to-point behavior.
- Reuse RouteIntentRequest, validation, domain values and GPX serialization.
- This new endpoint accepts shape=loop and profile=road. All three elevation
  preferences are supported for ranking, not guaranteed ascent optimization.
- Other valid shapes/profiles return 422 unsupported_intent before network work.
- No new packages, AI, persistence, account, public deployment, or gravel fallback.
- Manual safety/quality and Garmin/Wahoo checks from stage 4 remain outstanding.

## Search policy

1. Use targetDistanceMeters as the requested loop length when supplied.
2. Otherwise derive requestedLengthMeters = targetDurationSeconds * 20000 / 3600.
   Return the assumption initial_speed_20_kmh; do not alter the user's intent.
3. Support requested lengths from 1000 through 100000 metres inclusive initially.
   The 1 km floor is an application policy, not a claimed ORS minimum. The upper
   bound follows the hosted provider's documented round-trip limit. Out-of-range
   requests return 422 search_distance_out_of_range; never silently clamp them.
4. Make at most three sequential provider calls with seeds 1, 2 and 3, each using
   the same requested length, one start coordinate, and round_trip.points=3.
   A seed changes search direction; it is not a promise of a distinct route.
5. Keep existing road-profile restrictions, elevation=true, instructions=false,
   auth handling, response size limit, and per-call 15-second timeout. Use a
   linked 45-second overall search deadline; caller cancellation takes precedence.
   Do not retry automatically or replace failed/duplicate seeds with extra calls.

Seeds are stable for reproducible requests on the same provider graph. Graph
updates can change results. Time comparisons use ORS estimated duration, not
duration recomputed at 20 km/h. Conflicting time/distance targets remain visible.
This batch does not calibrate speed, adjust length after each result, or invent
intermediate/closing geometry to force a match.

## Candidate validity and duplicates

The existing adapter validates geometry and numeric metrics. Additionally, a
loop must contain at least three distinct coordinate values and its first/last
positions must be equal by domain coordinate value (elevation may differ).
Do not append a synthetic closing segment. Failure of this loop-specific contract
is routing_invalid_response. A snapped start is displayed as returned by ORS.

Remove identical coordinate sequences, including reversed sequences. Ignore
elevation when identifying duplicates and keep the lowest-seed occurrence.
This is exact duplicate removal, not a road-overlap or route-diversity guarantee.
Out-and-back stretches are not excluded by this stage.

## Ranking

Rank the remaining candidates together, after generation and deduplication.

For each supplied target compute the signed deviation actual - requested, and
the relative error abs(deviation) / requested. Only supplied targets participate.
Define targetError as the mean of the relative errors, individually capped at 1
for scoring. All calculations must remain finite even at numeric input limits.
Signed distance/time deviations remain uncapped and are returned for inspection.

A candidate matches the targets when every supplied target has relative error
at most 0.10 (inclusive). This tolerance is an explicit first-MVP policy.
The implementation compensates for double roundoff with a 1e-15 relative
comparison allowance; deviations and scores themselves remain unrounded.

For elevation ranking, let maxAscent be the largest known ascent among the
remaining candidates. When maxAscent > 0:

- minimize: elevationPenalty = ascent / maxAscent.
- seekClimbs: elevationPenalty = 1 - ascent / maxAscent.
- balanced: ignore ascent in scoring.

Known zero ascent when maxAscent=0 has penalty 0 for both preferences, since no
observed ascent distinguishes those candidates. Missing ascent has penalty 1
and warning elevation_data_unavailable for minimize/seekClimbs. Never substitute
zero for missing elevation or invent a climb estimate.

Score (lower is better, not a confidence/probability):

- balanced: targetError.
- minimize/seekClimbs: 0.8 * targetError + 0.2 * elevationPenalty.

Order by targetsMatched descending, score ascending, then seed ascending. Thus
an in-tolerance route always precedes an out-of-tolerance route; ascent breaks
trade-offs within those groups. Ranking is relative to the sampled candidates,
not a claim of finding the globally best route or achieving a specific ascent.

Example: targets 20 km / 3600 seconds; A=20 km / 3960 s / 100 m ascent,
B=21 km / 3600 s / 300 m ascent. Both match. Balanced prefers B; minimize
prefers A (scores about 0.106667 vs 0.22); seekClimbs prefers B.

## Response contract

200 response fields:

| JSON path | Type and meaning |
| --- | --- |
| requestedLengthMeters | number; initial length sent to ORS |
| assumptions | string array; initial_speed_20_kmh for duration-only input |
| attemptedCount | integer; provider calls started, from 1 through 3 |
| warnings | string array; response-level codes described below |
| candidates | nonempty array in ranked order |
| candidates[].seed | integer; originating seed |
| candidates[].assessment.distanceDeltaMeters | nullable number; actual minus requested distance |
| candidates[].assessment.durationDeltaSeconds | nullable number; actual minus requested time |
| candidates[].assessment.targetsMatched | boolean; every supplied target within the 10% tolerance |
| candidates[].assessment.score | finite number from 0 through 1; lower is better |
| candidates[].route | existing GeneratedRouteResponse object, including geometry, metrics, attribution, warnings and gpx |

Unrequested deviation fields are null. Each route's GPX uses exactly that candidate's geometry with
the existing GPX longitude normalization. Candidates do not get persistent IDs.

Always include candidate_search_limited at response level. Candidate route
warnings include targets_not_met when outside tolerance and, where relevant,
elevation_data_unavailable. Do not reuse targets_not_optimized from the A-B API:
this endpoint does rank candidates, with the limitations described above.
If none match, still return the best available candidates and response warning
no_candidate_within_tolerance. Approximate candidates are not proof of feasibility
or infeasibility of the requested targets.

## Partial failures and cancellation

- Route-not-found for a seed consumes one attempt; continue with the next seed.
- A malformed response, invalid loop, credentials failure, provider quota limit,
  transport failure or timeout stops further provider calls.
- If usable candidates were already received, rank and return them with
  candidate_generation_incomplete and the existing public routing failure code
  in response warnings. Do not return raw provider errors or secrets.
- If none were received, use the existing routing failure status/code. Three
  route-not-found results return 422 route_not_found, never an empty success.
- ORS error code 2004 maps to 422 routing_limit_exceeded; preserve existing
  mappings for other errors. Candidate search applies the same partial-result
  rule to this provider-limit failure.
- Expiry of the overall deadline uses routing_timeout and the same partial rule.
- Caller cancellation propagates even if partial candidates exist. Never turn
  RequestAborted into a 200 response or a provider timeout.

## Ownership and verification

Application owns bounded orchestration, loop policy, deduplication and pure
ranking. IRoutingProvider gains a road-loop operation. Infrastructure translates
that operation to ORS JSON and reuses response parsing and failure handling.
Contracts owns new response DTOs; API translates results and errors. Avoid a
general search framework, mediator, background queue or new domain dependencies.

Tests must cover target-only/both targets, the 20 km/h assumption, limits before
network calls, exact tolerance boundaries, missing ascent, score finiteness,
ordering/ties, closed geometry, reverse duplicates, the three-call budget,
partial results, no-result errors, deadline vs caller cancellation, and unchanged
A-B behavior. HTTP fixtures must inspect round-trip payload and [lon, lat] order.
Live verification uses one bounded loop search with the locally configured key,
records deviations, and validates each GPX. It does not replace manual road checks.

## Sources

- [ORS round-trip options](https://giscience.github.io/openrouteservice/api-reference/endpoints/directions/routing-options)
- [Hosted API limits](https://openrouteservice.org/restrictions/)
- [Provider error codes](https://giscience.github.io/openrouteservice/api-reference/error-codes)
- [ORS round_trip JSON field](https://github.com/GIScience/openrouteservice/blob/main/ors-api/src/main/java/org/heigit/ors/api/requests/routing/RouteRequestOptions.java)

The documented length is a preference; the 100 km provider limit is not a
guarantee that every requested loop exists. The provider's request model names
the JSON option round_trip, with an underscore.
