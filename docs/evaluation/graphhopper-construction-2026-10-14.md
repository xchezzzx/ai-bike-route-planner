# Bounded native construction experiments

## Controls and decision

Two independently reviewed experiments follow the
[frozen baseline](graphhopper-quality-2026-10-14.md) and
[public control-v1](graphhopper-road-control-v1.json). Three public starts, two
seeds each, requested 40 km, assessed against exact 35-45 km. These are native
engine probes, not the adaptive application search or owner road acceptance.

All 18 calls (6 baseline, 6 slope, 6 turn-aware) returned HTTP 200, sequentially,
without retries, ORS, Gemini, new datasets or production profile changes.
Each arm retained 5/6 under current policy. All have partial surface evidence.
Neither experiment meets its predeclared promotion screen. Keep the production
graph/model/provider interface unchanged. This is a negative result for these
specific models on this small corpus, not proof that all native models fail.

## Slope priority

The existing graph already imports signed `average_slope`. Request-level priority
multiplies by 0.3 above 6% uphill, otherwise by 0.6 above 3%; downhill gets no
additional penalty. Base racingbike/elevation/road models and speeds are unchanged.
The sign reverses with traversal direction; a zero grade is not proof of complete
elevation evidence. See [pinned custom-model documentation](https://github.com/graphhopper/graphhopper/blob/11.1/docs/core/custom-models.md).

Predeclared screens: no per-city loss of eligible routes, >=10% median ascent
reduction on matched eligible pairs, no >10% per-pair ascent regression, no >15%
duration regression, no >2 percentage-point near-return fraction increase,
no >5pp unknown-surface increase and no loss of known-height fraction. These are
experiment screens, not new public route constraints or quality guarantees.

| Start | Seed | km | Ascent m | Near-return m | Eligible | Duration change |
| --- | --- | --- | --- | --- | --- | --- |
| Tel Aviv | 1 | 39.18 | 366.96 | 1700 | yes | +1.69% |
| Tel Aviv | 2 | 42.92 | 335.83 | 0 | yes | +0.14% |
| Haifa | 1 | 39.02 | 425.73 | 3620 | yes | -1.23% |
| Haifa | 2 | 40.56 | 1080.62 | 3860 | yes | +1.26% |
| Jerusalem | 1 | 51.32 | 1549.82 | 0 | no | -0.51% |
| Jerusalem | 2 | 38.93 | 977.37 | 980 | yes | -1.00% |

Matched eligible median ascent reduction is only **2.14%**, below 10%. One Haifa
return improves substantially, but Tel Aviv seed 1 and Haifa seed 1 worsen in
absolute near-return length. Eligible-pair surface/height/duration screens pass;
the already-excluded Jerusalem seed 1 increases unknown surface by 7.5pp and
remains excluded. Do not hide that result or count its lower ascent as an eligible
benefit. Request latency was 38-163 ms. No model is adopted and Minimize remains
candidate ranking only until an evidence-backed construction change is approved.

## Separate turn-aware import

Pinned GraphHopper 11.1, identical JAR/PBF/base models, separate graph and copied
cached elevation, loopback port 8991. Import adds `orientation`, bicycle-specific
turn restrictions, finite 60 U-turn weight and custom `turn_penalty`: add 30 above
150 degrees absolute, otherwise add 5 above 60, otherwise 0. No new infinite
penalty or access relaxation is configured. Native virtual-node restrictions
still apply; finite costs are not a universal promise that every U-turn is legal.

Pinned sources verified before import: [configuration prerequisites](https://github.com/graphhopper/graphhopper/blob/11.1/config-example.yml),
[turn-cost provider](https://github.com/graphhopper/graphhopper/blob/11.1/core/src/main/java/com/graphhopper/routing/weighting/DefaultTurnCostProvider.java),
and [turn-penalty example](https://github.com/graphhopper/graphhopper/blob/11.1/core/src/main/resources/com/graphhopper/custom_models/avoid_turns.json).
Same-edge U-turn weight is applied before custom angle penalties; no speed or
provider travel time is fabricated to favor this experiment.

Container limits: 2 GiB, 2 CPUs, 1536 MiB heap, read-only root, dropped capabilities,
no-new-privileges; baseline bind read-only, experiment/cache writable separately.
Import readiness took **85.891 s**, peak cgroup memory **1,169,711,104 bytes**
(about 1.09 GiB), graph files **112,248,101 bytes**; no OOM. Primary cache was not
reimported or overwritten. Experimental config SHA-256
`d9cdd284cfdbb132157dff21974cace8fd633b3d0841a1700aaeeecfc841993c`;
turn model `d32e0800a618c9b18a34e5322a37aaaae5f6a475dfaa4aa6620d958df703adc5`.
Raw configs/imported graph/logs are preserved under ignored artifacts; its own
temporary container was stopped and removed after measurement.

Predeclared promotion requires no per-city eligibility loss, >=20% median
near-return reduction on matched eligible pairs with nonzero baseline returns,
no >2pp per-pair near-return fraction worsening, no >15% ascent/duration increase,
and the same surface/height screens. Request/engine timeouts remain 15/14 seconds.

| Start | Seed | km | Ascent m | Near-return m | Eligible | Duration change |
| --- | --- | --- | --- | --- | --- | --- |
| Tel Aviv | 1 | 41.62 | 398.59 | 1160 | yes | +0.01% |
| Tel Aviv | 2 | 42.86 | 332.70 | 0 | yes | +0.35% |
| Haifa | 1 | 39.03 | 449.46 | 3420 | yes | -1.35% |
| Haifa | 2 | 39.43 | 1091.84 | 5560 | yes | +0.29% |
| Jerusalem | 1 | 52.97 | 1715.80 | 0 | no | +0.16% |
| Jerusalem | 2 | 39.29 | 1000.08 | 980 | yes | -0.08% |

Absolute near-return reductions on the four eligible nonzero-baseline pairs are
0%, 1.72%, 5.44%, 0%: median **0.86%**, below 20%. Other eligible-pair screens pass.
Request latency was 65-518 ms, including cold first request. Long close returns
survive despite turn-aware weighting. Changing junction/reversal cost alone does
not demonstrate the intended logical-ring improvement on these controls.

## Final application smoke

The primary API was restarted through the tracked persistent launcher. One
explicit public Haifa 35-45 km / Minimize request returned HTTP 200, three routing
attempts, two retained candidates, zero exclusions. Candidates were 39.73 km /
449.93 m ascent (score 0.09982) and 39.52 km / 1093.47 m (score 0.22983); the lower
ascent candidate is first. Selected `Haifa-loop-road-40` GPX has 1003 points and
matches the selected API geometry's count, order, coordinates, optional heights
and name exactly. No extra routing call was made for GPX validation.

This adds three application attempts beyond the 18 comparison requests. The
public API does not expose the third attempt's duplicate/no-route distinction;
do not infer it from the retained count. Original request/response and redacted
smoke summary are saved locally. This is automated API/GPX verification, not
browser-owner, cycling-device or real-road acceptance.

## What this does not close

Automated geometry warnings do not establish that every reported passage is
unnecessary; access stems, switchbacks and real road suitability need the owner.
The historical 1400+m request, cycling-device import and real-phone checks remain
open. The next proposed investment is locally generated road-network waypoints
with GraphHopper returning complete connected paths, not GPX fragment editing,
broader road access or a hidden request-budget increase. See the separate design
proposal before implementing that new construction subsystem.

[Road-network waypoint design proposal](../superpowers/specs/2026-10-14-road-network-waypoint-design.md)
records alternatives, boundaries, unchanged request budgets and prerequisite gates.
