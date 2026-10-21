# Geometric waypoint comparison, 2026-10-21

## Decision

**Do not adopt the tested waypoint configuration.** Under identical application
search limits, native construction retained six candidates in three of four
scenarios; geometric waypoints retained one in one scenario. Both coverage and
candidate-count promotion gates fail. Production code, graph, profile, access
rules, ranking, calibration and default construction are unchanged.

The owner authorized this local experiment and conditional autonomous adoption
only if quality improved. This negative result does not authorize implementing
the separate road-network catalog proposal, and does not prove that every
possible waypoint strategy fails.

## Frozen protocol

The throwaway local harness was compiled and independently reviewed before use.
Two measurement defects were reproduced/corrected: stopping after a service-level
invalid response and refusing promotion of incomplete searches. The analyzer's
incomplete-search regression was observed RED, then GREEN; the reviewer also ran
11 in-memory analyzer checks. No regional route requests were made during that
review or its synthetic tests.

The protocol was frozen at 08:16:40 UTC before the run; its SHA-256 is
`1fc491f155d50546bed16c4a39f6403cb8cef2342aaf5ca2ff9eb6a7e69f31b1`.
It includes the harness/comparison hashes and private input identity. Source,
protocol, requests, raw responses, results, GPX-containing service responses and
offline audits remain ignored under `artifacts/waypoint-20261021/`. They contain
the private start/geometry and must not be published. The
[public protocol](graphhopper-geometric-waypoint-v1.json) omits private coordinates.

- Baseline: main `13f55f6c3afced83792771fb6d229ca293943938`.
- Engine: GraphHopper 11.1, source
  `ebb578f4e94db72e82f2f8d3bc69417758e16e1c`, existing graph identity
  `da4d12ea274e7e6b7d6443bb72acad7119400dc4ace9c5cc43ec64446f283545`.
- Same existing `road` profile/PBF and elevation cache; no import, new dataset,
  custom model, cloud service, ORS call or Gemini call.
- Actual production `RouteCandidateService`, `LoopSearchLength`,
  `GraphHopperProvider`, response parser, selector and GPX writer are referenced
  by the disposable harness. Native sends the unmodified production payload;
  the waypoint arm changes only its outgoing local engine request and applies
  the declared anchor-snap guard.
- Manual/Road/Balanced, inclusive distance ranges, seeds 1-3, existing adaptive
  length correction, at most three sequential HTTP routing calls per search,
  45-second application deadline, 15-second provider deadline. No retries,
  `/nearest`, `/isochrone`, hidden fallback or post-result parameter tuning.
- Three public starts/ranges from the frozen public controls: Tel Aviv, Haifa,
  Jerusalem, each 35-45 km. A fourth local-only start comes from the owner's
  good GPX, with a declared 41-51 km test range. This is not a reconstruction of
  the historical 1400+m complaint or a claim to recover its original request.

## What the waypoint arm changes

For each requested search length L, use two spherical-projection anchors at
radius L / (3 * 1.35) from the exact requested start. Bearings are (30,90),
(150,210), (270,330) degrees for seeds 1,2,3. The complete ordered route is
start, A, B, start; GraphHopper supplies its entire connected geometry in one
HTTP call. No GPX fragment is cut, reversed or stitched, and no LLM coordinates
or curated road/settlement catalog are used.

Use `algorithm=astarbi`, `pass_through=true`, with the existing road profile,
instructions, elevation and surface/road-class/environment details. Reject the
attempt locally if either intermediate anchor snaps farther than 1000 m; count
that request and record the raw HTTP result separately, without another lookup.

Pinned source review matters here:
[native round trips](https://github.com/graphhopper/graphhopper/blob/11.1/core/src/main/java/com/graphhopper/routing/RoundTripRouting.java)
already use generated points and previous-edge avoidance. This experiment is
therefore a different anchor/path strategy, not the first use of waypoints.
[Via-routing](https://github.com/graphhopper/graphhopper/blob/11.1/core/src/main/java/com/graphhopper/routing/ViaRouting.java)
handles each leg separately; pass-through penalizes an incoming edge, rather
than guaranteeing that all future returns are impossible. Native previous-edge
avoidance is not automatically carried into normal via-routing.

[Flexible path calculation](https://github.com/graphhopper/graphhopper/blob/11.1/core/src/main/java/com/graphhopper/routing/FlexiblePathCalculator.java)
creates per-leg algorithms. The waypoint request divides its budget between the
three legs: `timeout_ms=4666`, `max_visited_nodes=333333`. It does not send three
full-budget routing requests concealed as one. Existing application/provider
cancellation remains in force. No timeout was observed in this run; an induced
engine-timeout/cancellation stress test is not claimed.

## Predeclared promotion screens

No scenario that had an eligible route may lose all eligible routes, and total
eligible candidate count must not decline. Require at least three matched
selected pairs with nonzero baseline near-return and at least two improvements,
with >=20% median near-return reduction. For matched selected routes, guard
against >2 percentage-point increases in near-return, exact-repeat or shared-stem
fractions; >5pp additional unknown surface; any loss of known-height fraction;
>15% ascent/duration increase; and >5pp relative distance-error increase.
Incomplete comparisons, non-NoRoute failures and budget violations prevent
promotion. These are experiment screens, not new public route constraints.

## Results

| Scenario | Range km | Native retained | Waypoint retained | Native selected km / ascent m / near-return m | Waypoint selected |
| --- | --- | --- | --- | --- | --- |
| Tel Aviv | 35-45 | 2 | 1 | 42.89 / 341.62 / 0 | 42.12 / 436.85 / 940 |
| Haifa | 35-45 | 2 | 0 | 39.73 / 449.93 / 3480 | none |
| Jerusalem | 35-45 | 2 | 0 | 36.85 / 906.06 / 0 | none |
| Private reference start | 41-51 | 0 | 0 | none | none |

All eight searches completed their three attempts: **24 local routing calls**,
zero ORS/Gemini. Raw HTTP accounting is 20 successful responses and four 400
NoRoute outcomes; one successful raw response is additionally rejected by the
local 1000 m anchor-snap guard. Nineteen parsed paths entered the application;
seven were policy-eligible across both arms, twelve failed the target range.
There were no forward/reverse exact duplicates, transient errors or retries.

Every attempt below is included, not just retained routes. Search lengths are
adaptive inputs, not overwritten user targets. `N` denotes native; `W` waypoints.
`R` is retained, `T` excluded by target range, `P` upstream point-not-found,
`N0` native ordinary no-valid-point, and `S` local anchor-snap rejection.

| Scenario | Arm | Attempt | Search km | Returned km | Outcome |
| --- | --- | --- | --- | --- | --- |
| Tel Aviv | N | 1 | 40.000 | 41.632 | R |
| Tel Aviv | N | 2 | 40.000 | 42.886 | R |
| Tel Aviv | N | 3 | 40.000 | 30.866 | T |
| Tel Aviv | W | 1 | 40.000 | 42.121 | R |
| Tel Aviv | W | 2 | 40.000 | 47.275 | T |
| Tel Aviv | W | 3 | 33.844 | - | P |
| Haifa | N | 1 | 40.000 | 39.732 | R |
| Haifa | N | 2 | 40.000 | 39.521 | R |
| Haifa | N | 3 | 40.000 | - | N0 |
| Haifa | W | 1 | 40.000 | - | P |
| Haifa | W | 2 | 40.000 | 53.899 | T |
| Haifa | W | 3 | 29.685 | - | P |
| Jerusalem | N | 1 | 40.000 | 51.561 | T |
| Jerusalem | N | 2 | 31.031 | 36.847 | R |
| Jerusalem | N | 3 | 31.031 | 36.961 | R |
| Jerusalem | W | 1 | 40.000 | 54.341 | T |
| Jerusalem | W | 2 | 29.443 | 33.065 | T |
| Jerusalem | W | 3 | 35.619 | 51.891 | T |
| Private reference start | N | 1 | 46.000 | 54.960 | T |
| Private reference start | N | 2 | 38.501 | 38.625 | T |
| Private reference start | N | 3 | 45.852 | 38.768 | T |
| Private reference start | W | 1 | 46.000 | 52.591 | T |
| Private reference start | W | 2 | 40.235 | 57.873 | T |
| Private reference start | W | 3 | 31.981 | withheld | S |

For the single common successful scenario, waypoint near-return increases from
0 to 940 m (+2.23pp of geometry), and ascent increases 27.88%. Unknown surface
improves from 33.35% to 19.52%, but that does not offset lost coverage/returns.
There are **zero** matched selected pairs with nonzero baseline near-return;
the median-reduction screen is unavailable, not a measured 0% improvement.
Both strategies miss the private reference's range, so neither is claimed to
reproduce the owner's good ring. Searches took 85-684 ms locally, without a
load test or a speed-improvement claim for the failure-heavy waypoint arm.

## Verification and limits

- Fresh production baseline: Release build, 459 unit + 639 integration tests PASS.
- All 20 successful raw responses replayed through the existing offline evaluator
  with no network calls: hashes, closure, policy outcomes and available near-return
  measurements match the capture. The locally snap-rejected response is audited
  as a raw path separately, not silently treated as an upstream failure.
- Nineteen parsed paths round-trip through the real GPX writer with identical
  point count, order, coordinates and optional heights. Original private GPX and
  frozen protocol hashes are unchanged.
- No new product feature or dependency is promoted. This is not browser-owner,
  road-safety, cycling-device, real-phone, AI-contract or cloud acceptance.

The next useful decision is whether to investigate network-aware anchor/corridor
selection, including how to avoid repeated corridors across legs. Blindly adding
more geometric seeds, relaxing targets/access, or just copying the isolated
successful Tel Aviv outcome would not satisfy this experiment. The separate
[catalog proposal](../superpowers/specs/2026-10-14-road-network-waypoint-design.md)
remains unapproved; this report does not prove that a catalog alone would fix
route construction.
