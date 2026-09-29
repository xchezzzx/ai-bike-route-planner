# Road Loop Refinement Qualification: 2026-09-29

Contract: `route-search-v1`. Corpus: `route-refinement-v1.json`.
**Outcome: live gate failed. Do not merge or enable by default.**

## Execution and budgets

One four-city comparison used 24 ORS and 4 Gemini calls. The separate synthetic
advisor corpus used 6 Gemini calls. Total: 24 ORS and 10 Gemini, within the agreed
cap, sequential and paced, with no retries or paid fallback. Original requests
are in the corpus; route geometry is always provided by ORS.

Ignored local reports:

- `artifacts/refinement-comparison-20260929T133506-0c219483.json`
- `artifacts/advisor-qualification-20260929T103915-829c3405bfdd41dd8a72b38608e22a0a.json`

The comparison ran against commit `14fc9dc` while independent review completed.
Its top-level `meanTargetError` means the **best candidate's** mean relative
target error, not a set average or necessarily the first ranked candidate.
The corrected harness calls this `bestMeanTargetError`, rejects degenerate
geometry and retains allowlisted routing failure reasons. The earlier report
is preserved unchanged: it does not contain the cause of Haifa's partial
baseline response. Do not infer that missing cause or count this run as proof
of the corrected harness. Application geometry validation was already active.

## Four-city comparison

Each arm attempted three ORS calls; each advised arm called Gemini once.
Error below is absolute relative distance error, except Beersheba (duration).

| Case | Baseline / advised usable routes | Best error baseline / advised | Advised outcome |
| --- | --- | --- | --- |
| Tel Aviv, 20 km, minimize climbs | 3 / 3 | 0.319% / 0.319% | `invalidResponse`, deterministic fallback |
| Haifa, 20 km, minimize climbs | 2 / 3 | 24.919% / 11.019% | Search succeeded; still outside 10% tolerance |
| Jerusalem, 20 km, seek climbs | 3 / 3 | 13.912% / 13.912% | `unavailable`, deterministic fallback |
| Beersheba, 60 min, balanced | 3 / 3 | 57.042% / 58.083% | Search succeeded but target error worsened |

All returned candidates had ascent values. Only Tel Aviv met target tolerance.
Haifa baseline was partial. No observed quota failures or unrun comparison arms.
Fallback retained usable routes, but that is not successful advisor qualification.
Haifa's improvement and Beersheba's regression do not establish a general effect.

## Six advisor contexts

1. Matching targets: `invalidResponse`.
2. Too long: passed; search seed 3, requested length 14285 m, reason distance.
3. Too short: `invalidResponse`.
4. Duration only: `unavailable`.
5. Unknown ascent: `invalidResponse`.
6. Conflicting extreme observations: `invalidResponse`.

Result: 1 passed, 5 failed, 0 unrun. No raw model text, credentials or coordinates
were saved by the advisor harness. `invalidResponse` does not identify whether
the rejection was envelope/schema/proposal validation, truncation, or another
invalid response. `unavailable` likewise does not prove a particular HTTP code.
The cause must be diagnosed, not guessed or resolved by relaxing safety checks.

## Offline qualification and review

Independent whole-branch review found no critical issue and three important
issues: cancellation reporting, ambiguous duplicate request properties, and
degenerate geometry counted by the evaluation harness. All three were corrected
with regression tests. A minor missing routing-failure detail was corrected too.
The cancellation tests cover before-call, pacing and in-call cancellation and
require all six case entries to remain in the persisted report.

Backend: 268 unit and 263 integration tests pass offline. Frontend: 88 tests,
production build and desktop/mobile scenarios cover AI search, fallback,
selected GPX, cancellation, deadline handling and Hebrew RTL. Docker build and
no-key health/503 smoke passed. Provider calls are never part of CI.

## Remaining gate

Preserve this negative evidence. Diagnose rejected responses using only bounded,
allowlisted structural diagnostics; never log model prose or secrets. Then run
a separately authorized bounded qualification, including the corrected harness,
and review its results. Do not automatically rerun the exhausted live budget.
Only merge after live qualification and both CI workflows pass. Track naming,
field/device acceptance and public hosting remain separate subsequent work.
