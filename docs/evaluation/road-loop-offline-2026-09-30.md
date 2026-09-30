# Road-loop Offline Baseline

Date: 2026-09-30. No new ORS or Gemini calls. This is diagnostic evidence,
not improved construction, a live qualification, or a safety certification.

## Reference GPX

The supplied RouteCycle GPX was analyzed locally without changing or committing
the original. Only aggregate evidence is recorded:

- 1,732 points, one continuous segment, exact endpoint closure.
- Geometric length: 40,323.991025 m.
- Exact repeated edges: 35.309275 m, all in the shared departure/return stem.
- Remaining exact repeats: zero. Approximate retracing and manoeuvre complexity
  are not measured; zero does not certify an ideal road loop.
- Surface and waytype evidence: unavailable. GPX geometry alone cannot verify
  the user's assessment that the track is asphalt. Duration is unknown.
- Independent PowerShell XML/haversine sum: 40,323.991025141 m, agreeing with
  the production geometry helper within 0.000001 m.

## Saved ORS Replay

Replayed the ten private responses from the prior road-quality spike through
the current production adapter and selector, with 36-44 km inclusive bounds
(equivalent to the original scalar 40 km +/-10%). These are **alternatives
from multiple older experiments**, not one ten-call search or a new comparison
between current and fixed-seed calibration. Historical IDs do not imply that
those strategies have been implemented in production.

| Saved ID | Provider km | Eligible | Remaining exact repeats, m | Unknown surface % | Reasons |
| --- | ---: | :---: | ---: | ---: | --- |
| baseline-1 | 62.320 | No | 1813.6 | 66.6 | Target |
| baseline-2 | 69.651 | No | 2847.5 | 64.3 | Target, surface |
| baseline-3 | 43.791 | Yes | 581.1 | 71.4 | None |
| calibrated-2 | 43.166 | No | 0.0 | 52.0 | Surface |
| calibrated-3 | 43.635 | No | 855.5 | 63.2 | Surface |
| fastest-1 | 62.320 | No | 1813.6 | 66.6 | Target |
| points-2 | 48.778 | No | 292.0 | 80.8 | Target |
| points-4 | 74.073 | No | 0.0 | 58.8 | Target, surface |
| points-5 | 84.831 | No | 1604.0 | 57.8 | Target |
| shortest-1 | 48.096 | No | 3.0 | 77.4 | Target, surface |

One of ten saved alternatives is eligible, reproducing the earlier baseline.
Its high unknown-surface share and remaining repeats remain visible. The
reference is not directly comparable for surface eligibility because it lacks
provider evidence. Private reports and raw geometry remain in ignored artifacts.

## Implementation Checks

Initial tests reproduced missing evaluation types and the old empty-result copy.
Focused offline tests subsequently passed. CLI tests caught an unhandled
InvalidDataException for an input/output path collision; it was corrected.
Final suite results and PR checks are recorded in the execution plan.

No search strategy, provider-call limit, target tolerance or road-surface policy
was changed. The control matrix and offline utility prepare the next comparison;
they do not remove its separately budgeted live/manual acceptance gate.
