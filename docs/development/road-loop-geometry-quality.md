# Road loop geometry checks

## Scope and limitations

The objective is to rank logical road rings above long close return passages,
without removing necessary access stems, small roundabouts or short hairpins.
This is an incremental geometric heuristic, not evidence of road identity,
an instructed U-turn, legal access, asphalt or safety. Long legitimate hairpins
and necessary parallel carriageways can still trigger it. No geometry is cut
or moved. Distance, time, surface and access gates are unchanged.

The production search still acquires at most three candidates. No ORS/Gemini
calls, cloud services, new frontend controls or engine configuration changes
were introduced for this stage.

## Implementation

`LoopGeometryMetrics.NearReturnMeters` resamples geometry every 20 m using
geodesic progress and a local planar projection. It looks for opposite headings
(within 30 degrees of a reversal), at most 25 m apart and separated by at least
500 m of riding progress. A matched run must cover at least 300 m; gaps up to
40 m may connect a run, but the gaps themselves are not counted. Only the later
passage contributes distance. Same-direction nearby lines do not count.

Headings span 40 m before and after each sample. Matches use earlier segments,
not just sampled points, to avoid dependence on sampling phase. Runs follow
one earlier passage with bounded progress changes, and end when the current
heading differs over 60 degrees from the previous matched heading. Gradual
curvature does not itself end the run.
Monotone spatial alignment from the start and end exempts a close shared
departure/return stem, allowing 160 m lookahead and up to 100 m of unmatched
short detours; an extra
100 m guard reduces junction effects. Short geometry reversals alone are not
penalized. There is no universal maneuver-count penalty.

The spatial hash limits comparisons. The check returns null (not zero) for
open geometry with closure gap over 50 m, routes over 400 km, over 200,000 input
points, positions beyond 2 degrees of the projection origin, origins beyond
70 degrees latitude, degenerate geometry or over 1,000,000 comparisons.
Cancellation propagates. A null signal leaves the existing exact-repeat score
in place; it does not reject an otherwise eligible route.

For loops, selection adds `0.2 * max(exactRemainingRepeatedMeters,
nearReturnMeters ?? 0) / geometryLengthMeters` to the existing target/elevation
score. Taking the maximum avoids adding both measures for the same passage.
The exact `road-v1` response fields remain exact. The new `road_near_return`
warning appears above a 5% approximate fraction and is localized in EN/RU/HE.
Point-to-point selection does not apply this approximate loop check.

## Private controls and offline replay

The user's two GPXs were copied and SHA-256 verified under ignored
`artifacts/route-quality-20261005/controls`. Do not commit private coordinates,
GPX files or raw requests/responses. Synthetic controls are in unit tests.

| Private control | Geometry length | Exact internal repeats | Approximate near returns |
| --- | ---: | ---: | ---: |
| Good | 46.08 km | 0 m | 0 m |
| Bad | 45.48 km | 183 m | 5,960 m |

These files have no original intent, seed, provider duration or trustworthy
surface evidence. Their distinction validates this geometry signal, not
overall route quality or production eligibility.

The offline evaluator uses the production provider parser through a saved
response handler. It never sends HTTP traffic. Reports omit coordinates,
track names and input paths; existing reports cannot be overwritten.

```powershell
dotnet build src/backend/CyclingRoutes.Evaluation -c Release
dotnet src/backend/CyclingRoutes.Evaluation/bin/Release/net10.0/CyclingRoutes.Evaluation.dll gpx INPUT.gpx REPORT.json
dotnet src/backend/CyclingRoutes.Evaluation/bin/Release/net10.0/CyclingRoutes.Evaluation.dll graphhopper RESPONSE.json REPORT.json 35000 45000
```

GPX reports leave eligibility/duration unknown. The new nullable
`nearReturnMeters` is an offline diagnostic, not an additional public API field.
GraphHopper replay evaluates a saved closed loop against the requested metre
range and the same road selection gates as live routing.

## Local engine experiment, 2026-10-05

GraphHopper 11.1, existing regional graph and `road` profile; flexible routing,
elevation enabled. Three starts (Tel Hanan, Tel Aviv, Jerusalem), seeds 1 and 2,
fixed requested search distance 40 km, evaluation range 35-45 km. Each seed/start
compares baseline, 3 or 4 round-trip points, and initial headings 0 or 180.
One separate turn-penalty capability probe: 31 local HTTP calls total.
The earlier malformed-coordinate serializer run is retained separately and
excluded from the results; it did not compute routes.

| Variant | Returned routes / 6 | Within range and road gates / 6 |
| --- | ---: | ---: |
| Baseline | 6 | 2 |
| 3 points | 6 | 2 |
| 4 points | 5 | 0 |
| Heading 0 | 2 | 2 |
| Heading 180 | 6 | 3 |

Heading 0 failed point lookup in Tel Hanan and Tel Aviv; 4 points failed once
in Tel Aviv. No automatic retries. Heading 180 improved range fit in Jerusalem
but had more close returns than the baseline clean Tel Aviv candidate.
The initial diagnostic for the two Tel Hanan baseline seeds showed 0 and
5,880 m of approximate returns (before continuity/sampling refinements);
more points worsened distance fit there. No global parameter winner was proven.
These are single-request probes, not the production three-attempt calibrated
search, and not a broad manual assessment of road quality.

The turn-penalty probe was rejected with an unsupported `orientation` expression.
The current imported graph/profile cannot use it as configured. Upstream 11.1
also requires turn-cost configuration; do not add ineffective penalty values
to outgoing requests and claim they fix routing:

- [11.1 weighting factory and turn-penalty prerequisites](https://github.com/graphhopper/graphhopper/blob/11.1/core/src/main/java/com/graphhopper/routing/DefaultWeightingFactory.java)
- [11.1 round-trip point generation and previous-edge penalty](https://github.com/graphhopper/graphhopper/blob/11.1/core/src/main/java/com/graphhopper/routing/RoundTripRouting.java)
- [11.1 custom model documentation](https://github.com/graphhopper/graphhopper/blob/11.1/docs/core/custom-models.md)

Baseline GraphHopper request parameters are therefore preserved. Local request,
response, report, GPX and timing evidence is under ignored
`artifacts/route-quality-20261005/engine-v2`.

## Verification and next stage

Tests cover a clean ring, sustained parallel return with no exact repeated edge,
common departure stem, short serpentine, roundabout/crossing, same-direction
passages, distant parallel roads, sparse/dense sampling, reverse traversal,
cancellation, excessive length and a dense adversarial work-budget case.
Selection tests pin soft retention, ordering, unchanged exact diagnostics and
unchanged point-to-point behavior. Offline replay tests preserve privacy,
unknown GPX eligibility and actual GraphHopper surface/target gates.

Manual testing should compare all retained variants, not only rank 1. Look for
necessary access stems, legitimate long hairpins, surface mismatch, unnecessary
returns and missed candidates. A warning does not make a route unsuitable.

Next construction-stage work needs an isolated turn-aware profile/import and
matched-seed comparisons before adoption. If tuning still fails, design a
waypoint-driven loop generator that uses GraphHopper for connected legs; do
not remove arbitrary return fragments from exported GPX. This larger generator
is not implemented or approved by this incremental change.
