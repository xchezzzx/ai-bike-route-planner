# Road-loop Quality: Implementation Evidence

Date: 2026-09-29. Scope: approved `road-v1` evidence, assessment, selection and
UI contract. This implementation filters candidates; it does not change ORS
loop construction or demonstrate improved real-world road suitability.

## Behavior

- Both search modes return zero to three selectable candidates. Every retained
  route meets all requested distance/time targets within inclusive +/-10%.
- Non-road surface exceeding max(100 m, 0.5% of geometric length) excludes a
  candidate. Positive steps, ferry or construction coverage also excludes it.
- Missing/future surface codes stay unknown. Unknown is not paved and does not
  itself reject a candidate. Category lengths use geometry edges, not provider
  summary percentages. Sparse ranges cannot silently become complete coverage.
- Exact undirected repeated edges are measured separately from the shared
  departure/return stem. Only remaining repeats contribute to the added score.
- Exclusions carry fixed reasons and metrics, no geometry or downloadable GPX.
  Empty results clear the previous track/download while retaining map position.
- AI early stop requires a retained candidate; original intent, deadlines,
  cancellation, three ORS calls and one advisor call remain unchanged.
- No-match, partial-failure and malformed responses cannot pass qualification.
  Both evaluator arms validate quality totals, states, target deltas and reasons.

## Offline Replay

Ten previously saved ORS responses were replayed through the production adapter
with a stub HttpMessageHandler, then the production assessor/selector for their
original 40 km target. Zero external provider calls. The rows are alternatives
from a prior experiment, not one search exceeding the three-call limit.

| Input ID | Provider km | Retained | Rejection reasons |
| --- | ---: | :---: | --- |
| baseline-1 | 62.32 | No | Target |
| baseline-2 | 69.65 | No | Target, surface |
| baseline-3 | 43.79 | Yes | None |
| calibrated-2 | 43.17 | No | Surface |
| calibrated-3 | 43.63 | No | Surface |
| fastest-1 | 62.32 | No | Target |
| points-2 | 48.78 | No | Target |
| points-4 | 74.07 | No | Target, surface |
| points-5 | 84.83 | No | Target |
| shortest-1 | 48.10 | No | Target, surface |

For `baseline-3`, approximately 12.47 km is mapped paved, 42 m non-road and
31.28 km unknown; remaining exact retracing is about 581 m. It is eligible under
this policy, not certified as entirely paved or free from awkward maneuvers.
`calibrated-2` has approximately 2,061 m known non-road surface despite meeting
distance tolerance. Independent PowerShell edge/range sums for both cases match
production geometry/surface totals within 0.000001 m.

Raw responses, per-category/per-geometry-length replay JSON, screenshots and
the replay utility remain ignored under `artifacts/road-quality-replay` and
`artifacts/road-quality-spike`; private coordinates and the user's GPX are not
committed. Prior provider-summary totals are not used as exact numeric oracles.

## Verification

- Release restore/build: passed, zero build warnings/errors after correcting a
  new test's cancellation-token analyzer warning.
- Backend: 289 unit + 317 integration tests passed, with CYCLING_LIVE_ADVISOR
  removed from the test process. Earlier Debug regression runs also passed.
- Frontend: 110 unit/component tests passed; production build passed. Existing
  MapLibre bundle-size advisory remains, not a new build failure.
- Playwright: 28 desktop/mobile cases passed, including EN/RU/HE, RTL, nonblank
  canvas pixels, old-overlay removal, no overflow and selected GPX byte equality.
  Quality and empty-state screenshots were inspected across desktop/mobile.
  Map/API responses are fixtures, not a fresh live provider qualification.
- Both PowerShell evaluator harnesses passed. Refinement includes 18 scenarios
  covering malformed quality, inconsistent exclusions, empty/partial results,
  quota, disconnect, pacing and no retries.
- Docker image built; no-key smoke passed `/health=Healthy`, interpretation
  `503 ai_not_configured`, malformed JSON `400`. Only test-owned containers stopped.
- Independent review found missing evaluator quality validation and an array
  accepted as a frontend evidence-state enum. Both reproduced with failing
  tests, corrected, and covered by the passing suites.

## Delivery and Remaining Work

Local gates pass. Logical commits are prepared for existing draft PR #15; fresh
remote CI is checked during delivery. The separate earlier live AI qualification
failed and is not waived by this work. No merge or public deployment is permitted
until that gate and both CI workflows pass.

The next construction step is a separate road-network waypoint prototype, with
comparison against saved examples. Exact edge equality misses approximate
retracing and says nothing about turn complexity, legal access or current road
conditions. Garmin/Wahoo acceptance and field validation remain unperformed.
No extra provider quota, paid service, dependency, database or model schema was
introduced. Track naming and public hosting remain later roadmap items.
