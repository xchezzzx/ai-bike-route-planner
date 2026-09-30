# Road-loop Evaluation and Honest Empty Results

Scope: offline preparation for the approved road-loop quality backlog.
No provider calls, cloud deployment, new package, or production search change.

## Steps

- [x] Add failing tests for offline GPX / ORS assessment and empty-result copy.
- [x] Add an offline console utility reusing the production adapter and assessor.
- [x] Add a versioned public-city control matrix and comparison protocol.
- [x] Explain that no candidate was found in this bounded search, not that no
      suitable route exists; preserve attempt count and exclusion details.
- [x] Assess the supplied private GPX locally; commit only aggregate evidence.
- [x] Run regression suites and independent review.
- [ ] Commit, open PR and merge after green CI.

## Evaluation Rules

- Saved ORS responses use the real parser and road-v1 selector. The HTTP handler
  only returns supplied bytes; it cannot contact a provider.
- GPX geometry alone cannot prove surface, legal access, riding duration or
  safety. Missing evidence stays unknown; multi-segment tracks are rejected
  rather than silently connecting discontinuities.
- Reports contain a SHA-256 input identity and aggregate metrics, never track
  names, coordinates, original paths or timestamps from the input.
- Exact edge retracing is diagnostic, not a measure of all awkward maneuvers.
- Control cases include scalar/range distance and duration. Surface and target
  rules remain unchanged. Fixed-seed calibration is a hypothesis awaiting
  a separately budgeted live comparison, not an approved production change.

## Remaining Gate

No offline result proves better route construction. Next: obtain an explicit
provider budget, compare current and fixed-seed search with identical constraints
and at most three ORS calls per arm, inspect GPX manually, then decide whether
to change construction or prototype road-network waypoints.

## Verification

- Release build: zero warnings/errors. Backend: 392 unit and 536 integration
  tests passed, including 17 offline evaluator cases and eight control requests.
- Frontend: 199 unit/component tests; production build; all 98 Playwright tests
  passed. Six empty-result checks cover desktop/mobile EN/RU/HE, RTL,
  no horizontal overflow, nonblank canvas and old-route clearing. Screenshots
  inspected for Russian desktop and Hebrew mobile. Existing MapLibre chunk-size
  advisory and Playwright color-environment notices remain.
- Refinement PowerShell harness passed without provider calls.
- Private GPX and ten saved ORS responses analyzed offline; only aggregate
  evidence is committed. Original GPX SHA-256 unchanged.
- Independent review found incompatible saved units, partial final-file writes
  and discarded retained warnings. Regression tests and fixes added; reviewer
  confirmed all three closed by source inspection.
