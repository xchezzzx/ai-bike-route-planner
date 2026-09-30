# Bounded Road-loop Comparison

Date: 2026-09-30. Production code: main
`97138b28739965435c221641903d5730a9d4f9c5` (merged PR #23).

## Budget and Setup

The user explicitly authorized up to 12 sequential ORS calls, zero Gemini,
no retries after errors and no paid services. Actual usage: **9 ORS, 0 Gemini**.
The run stopped on an ORS HTTP 500; three unused calls were not repurposed.
No production search switch, provider retry, or cloud deployment was introduced.

Cases: `tel-aviv-range-35-45` and `haifa-range-35-45` from the committed
public-city control matrix. Both request a balanced road loop of 35-45 km.
These controls are not the user's private Nesher start.

Both arms use the production RouteCandidateService, selector, correction,
deduplication, naming fallback, three-call maximum and 45-second deadline.
An isolated local wrapper leaves the baseline's seeds 1/2/3 unchanged or maps
all three provider requests to seed 1. Original target bounds stay immutable.
Report candidate slots identify acquisition attempts, not the remapped seed.
The fixed arm is a deliberately simple all-fixed-seed prototype, not a hybrid
that switches to alternate seeds after finding a suitable route.

Transport allowance: 15 seconds, excluding deliberate pacing. Both arms have
the same five-second within-arm pacing; inter-arm delay occurs before starting
the production deadline. Redirects/retries are disabled. Response capture is
bounded to 16 MiB, including unknown-length bodies. Any provider failure or
returned incomplete search stops subsequent arms.

The probe source, raw requests/responses, manifests and GPX remain in ignored
local artifacts; no key, precise track geometry or private paths are committed.
Probe source SHA-256:
`c1c0bd15adf5b22547c79a90d7a39bd2da6084973ed98f95efd7411df5753ac8`.
It is experimental tooling, not part of the API or a new permanent live runner.

## Results

| Control / arm | Calls | Retained | Provider distances, km | Outcome |
| --- | ---: | ---: | --- | --- |
| Tel Aviv / changing seed | 3 | 3 | 41.678, 40.976, 42.570 | Eligible under road-v1 |
| Tel Aviv / fixed seed | 3 | 1 | 41.678 three times | Two exact duplicates removed |
| Haifa / changing seed | 3 | 0 | 47.871, 45.951, failed | Incomplete, HTTP 500 |
| Haifa / fixed seed | 0 | Not measured | Not run | Stopped by failure gate |

Tel Aviv needed no length correction because all returned distances matched
the range; this pair therefore does **not** exercise successful fixed-seed
calibration of an initially out-of-range loop. Keeping seed 1 lost diversity.
All retained candidates still carry unknown-surface warnings. Remaining exact
retracing is approximately 713.5 m (seed 1), 320.0 m (seed 2), and 2.9 m
(seed 3). Numeric eligibility is not a manual road-choice/safety approval.

Haifa requested lengths 40,000, 33,423.088 and 29,094.547 m, with seeds 1/2/3.
The first returned 47,871.1 m and failed both the distance range and surface
limit: approximately 299.1 m known non-road surface. The second returned
45,951 m and failed the exact upper bound by 951 m; its known non-road total
was zero, but much surface evidence remains unknown. The third received HTTP
500 and was mapped to routing Unavailable. It is not a no-match quality pass.

## Verification and Decision

Before live execution, synthetic runs verified 12-call mechanics, actual seed
mapping, geometry deduplication and identical scaled latency allowances. Valid
then open geometry stopped after two sends; a 429 stopped after two; an oversized
unknown-length response stopped after one. Those tests made no external calls.
Independent source review found pacing, incomplete-result stop and capture-size
issues; fixes and synthetic checks were completed before the live invocation.
The reviewer confirmed no remaining pre-live blockers by source inspection.

**Decision: keep the current production strategy.** This small, partly failed
experiment does not establish a fixed-seed improvement or complete real-road
quality acceptance. Do not mark the fixed-seed backlog item complete. Completing
the Haifa comparison requires a new explicit go-ahead after the provider error;
this run will not automatically resume or retry. Manual GPX road-choice review,
additional control cases and the conditional waypoint prototype remain pending.
