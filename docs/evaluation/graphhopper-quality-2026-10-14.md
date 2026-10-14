# GraphHopper quality baseline and eligible ranking

## Scope

No ORS or Gemini calls, no cloud, no changed graph/profile, no attempt/deadline
increase. Six sequential native GraphHopper requests used the public starts in
[control v1](graphhopper-road-control-v1.json), two fixed seeds each, requested
40 km, assessed against an exact 35-45 km range. This is not the adaptive
three-attempt application search and not a field-safety assessment.

GraphHopper 11.1 source `ebb578f4e94db72e82f2f8d3bc69417758e16e1c`;
graph identity `da4d12ea274e7e6b7d6443bb72acad7119400dc4ace9c5cc43ec64446f283545`;
regional OSM input SHA-256
`f98a8b9ad7fb3df19c846d3a562d9394abc5657173faeddcb033a53f4aa701f3`.
Config hash `96efc56f225af83a7ddd990baa238afd3caec0ba0c68c08ea6f4f3691a10cce8`,
road model hash `94989d7dfa4196dcf68936e0edb08da579445925734ed9612b26f32894edebb1`.

Raw requests/responses, full offline reports and private references remain in
ignored `artifacts/post-dot-20261014`. No default production logging was added.
The capture script borrowed starts from the older control file; its original
Jerusalem case ID says distance-20, but the actual saved native request was 40 km.
The new control file makes the executed units explicit.

## Native baseline

| Start | Seed | Distance km | Provider ascent m | Approx. near return m | Policy eligible | Request ms |
| --- | --- | --- | --- | --- | --- | --- |
| Tel Aviv | 1 | 41.63 | 406.75 | 1160 | yes | 316 |
| Tel Aviv | 2 | 42.89 | 341.62 | 0 | yes | 124 |
| Haifa | 1 | 39.73 | 449.93 | 3480 | yes | 111 |
| Haifa | 2 | 39.52 | 1093.47 | 5880 | yes | 87 |
| Jerusalem | 1 | 51.56 | 1671.61 | 0 | no: targets_not_met | 85 |
| Jerusalem | 2 | 39.27 | 998.72 | 980 | yes | 65 |

6/6 engine calls succeeded; 5/6 independently assessed routes satisfy current
distance/road policy. Every start has at least one eligible control. No pair is
an exact duplicate or reverse: provider distances already differ within each pair.
All six have partial surface evidence: about 16-70% of geometry has unknown
surface. No explicitly non-road surface, steps, ferry or construction was found;
unknown surface is not proof of pavement or suitability. Paths/tracks/footways
remain visible through existing warnings. Both Haifa controls trigger the soft
near-return warning; legitimate access stems/hairpins still need owner assessment.
These metrics do not prove that every maneuver is unnecessary.

The user's historical 1400+m request has not been recovered. This report does
not claim to reproduce that exact request. The current public controls do
reproduce high ascent and sustained close returns without relying on that claim.

## Private reference controls

Existing owner-labelled good/bad GPXs were replayed offline, without new routing:
good has 1182 points, 46.078 km geometry and 0 m approximate near return; bad has
1158 points, 45.482 km geometry and 5960 m approximate near return. Files and
coordinates remain local. GPX alone lacks trustworthy provider distance/time,
surface and access evidence, so policy eligibility remains unknown, not true.

## Ranking correction

An excluded high-ascent candidate previously changed the denominator used to
compare eligible candidates. A deterministic counterexample uses a 40 km target:
40 km / 500 m ascent versus 41 km / 100 m ascent. With no excluded outlier,
Minimize scores are 0.20 and 0.06. Adding an excluded 200 km / 10000 m candidate
changed them to 0.01 and 0.022, reversing the preferred eligible route.

The selector now reranks only eligible candidates using the existing ranker,
then adds their unchanged geometric penalty. Weights stay 80/20, targets retain
exact ranges/scalar 10% tolerance, and missing ascent retains its penalty/warning.
Excluded candidates keep original full-search diagnostic assessments and reasons;
their scores are not comparable with eligible-only normalized scores. Full-set
target-match bookkeeping remains independent of surface/waytype eligibility.

Verification: six Minimize/SeekClimbs counterexamples covering target, surface and
waytype exclusion failed before the fix and pass afterward. An additional empty
eligible-set test preserves matched-target diagnostics with unknown ascent.
Full backend suite: 459 unit plus 639 integration tests pass. No geometry is
changed and no new route is found by this correction.

## Next experiment

Use the same starts/seeds/lengths/graph to test request-level slope priority for
Minimize. Preserve base models and access/surface gates, change no provider speed,
and compare eligibility, ascent, repeat/near-return geometry, unknown data and
latency. Adopt only with a demonstrated benefit and no material regression;
otherwise retain baseline and publish the negative result. Real-road/device/phone
acceptance and historical-request reproduction remain open.
