# Advisor diagnostics and local v2 correction

**Latest result: v2 advisor corpus passed 6/6; four-city route comparison did not
pass. Merge remains blocked pending an explicit test-release acceptance of the
documented route-quality limitations. Historical attempts are preserved below.**

## Authorized budget and evidence

The user authorized at most six Gemini calls, zero ORS calls, and declined the
larger diagnostic/comparison budget. Exactly six Gemini calls were made against
`gemini-3.1-flash-lite`. Each run stopped after its first error, with no automatic
retries. All calls used the first `matching-targets` case of the v1 corpus;
the other five cases remained unrun. These are six diagnostic calls, NOT a
six-case qualification. No keys, raw model output, coordinates or thought text
were retained. Billing and account configuration were not changed.

All changes in this table were uncommitted experiments based on `3a3ea45`.
The union experiment is not part of the final implementation.

| Call | Schema and output cap | Diagnostic | Local report suffix |
| --- | --- | --- | --- |
| 1 | Original flat v1, 1024 | `invalidProposal` | `150605-d0c9259a82be47bba400f4ec686cf924` |
| 2 | Root stop/search union, 1024 | `outputTokenLimit` | `150851-3c82e3c35dcd426ba0da1289734bd026` |
| 3 | Root stop/search union, 4096 | `invalidAdvice` (older broad diagnostic) | `151020-a01a79e0656c4c00ac94a5800db76286` |
| 4 | Root stop/search union, 4096 | `outputTokenLimit` | `151216-51f1d9222f714005bf367fb8cb71c935` |
| 5 | Flat v1 with contextual bounds/fresh seeds, 4096 | `invalidProposal` | `151648-6455f208faf5411d88aec422636d823b` |
| 6 | Same schema/cap, finer diagnostics | `stopLengthNotNull` | `151841-a8b2258b32484912b27229dbc8490135` |

Reports are ignored local files named
`artifacts/advisor-qualification-20260929T<SUFFIX>.json`. They remain unchanged.
Call 6 proves a parsed stop response had a non-null requested length and null
seed; it does not establish the exact value or all earlier failures' causes.
The original application policy correctly rejected it. The root union and
larger output cap did not independently establish a solution. `MAX_TOKENS` does
not prove a decoder loop or distinguish thought tokens from answer tokens.

## Approved local correction

After these calls, the user explicitly approved simplifying the internal wire
contract locally, with **no further live calls**:

- `route-search-v2` accepts `{ "nextSearch": null }` for stop.
- Search uses one non-null object with required seed, requestedLengthMeters and
  reason; the model does not separately choose an action or a stop reason.
- Both objects reject additional/duplicate fields. A stop plus any extra length
  remains invalid, as does the legacy flat format. There is no silent repair.
- The schema uses fresh seeds and intersected length bounds. Unchanged server
  proposal validation remains authoritative. Public HTTP DTOs, route intent,
  fallback, three-ORS/one-advisor budgets and 30-second advisor deadline remain.
- The 4096-token cap is retained; truncated output is still rejected. It is not
  claimed to solve truncation. No model, temperature, or thinking change was made.
- New allowlisted diagnostics distinguish transport/status, envelope, finish,
  JSON/field/type, seed and length failures. They do not expose raw model prose.
- The harness stops on first error and supports `CYCLING_LIVE_ADVISOR_MAX_CALLS`
  in 1..6. Its cap is per invocation, not a cumulative allowance. All unrun cases
  stay in the report and prevent qualification from passing.
- The active corpus is `route-refinement-v2.json`, with the same six contexts
  and four route requests. Historical v1 data is preserved unchanged.

## Verification and next gate

Regression tests first failed for the new wire shape and then passed. They cover
stop/search mapping, old/mixed/duplicate/unknown fields, null search parameters,
fresh-seed and length bounds, diagnostics, limits, cancellation and fail-fast
partial qualification. Full offline backend verification passed: 289 unit and
349 integration tests. Independent review found no actionable defect; its added
coverage suggestions (legacy/hybrid/extra fields and report metadata) were
included in that final run. Final-SHA CI must pass after the push. A local Release
build initially hit DLL locks held
by the existing dev API; it was not treated as a passing build or used to stop
unrelated processes. Debug verification and isolated CI remain available.
The refinement harness passed all 18 loopback-fixture scenarios, v1 rejection,
and unchanged-corpus checks, with no live provider calls.

At the end of the local correction, the new contract had **zero live calls**.
The subsequent authorized run is recorded below. A complete v2 corpus and
corrected equal-budget four-city comparison are still required before merge.
Restart/rebuild the tested backend to the v2 revision before any later comparison;
an already-running v1 backend is not v2 qualification. Positive route-quality
claims/default-on remain separate from provider-contract acceptance.

References: [Gemini JSON schema support](https://ai.google.dev/api/generate-content#v1beta.GenerationConfig),
[thinking and output token limits](https://ai.google.dev/gemini-api/docs/generate-content/thinking).

## First authorized v2 live run: 2026-09-29 15:37 UTC

The user subsequently authorized up to 10 Gemini and 24 ORS calls, sequentially
with no automatic retries, with the advisor corpus preceding route comparison.
The freshly built v2 adapter at `8f3831eadf8abbf90f8ab1ad60a47e16daec1f67` ran
through the integration harness, not the already-running development API.

Local report:
`artifacts/advisor-qualification-20260929T153722-0ced7bd1bfd346489411e834e080bc16.json`.

| Case | Result | Evidence |
| --- | --- | --- |
| matching-targets | passed | Stop with null seed and length; 2964 ms |
| too-long | error | `unavailable`, `httpError`, HTTP 503; 2125 ms |
| too-short | unrun | Stopped at the preceding failure |
| duration-only | unrun | Stopped at the preceding failure |
| unknown-ascent | unrun | Stopped at the preceding failure |
| conflicting-extreme-observations | unrun | Stopped at the preceding failure |

Outcome: **not qualified**. The former contradictory-stop scenario passed once,
but neither the search branch nor the full corpus is live-qualified. HTTP 503
was returned by the provider; this run does not identify its underlying cause.
It is not evidence of malformed v2 output and is not counted as success.

Actual use of this new budget: **2 Gemini, 0 ORS**. Remaining allowance is at most
8 Gemini and 24 ORS; unused allowance is not an instruction to retry automatically.
No comparison, application-code change, service restart, billing change, or merge
was performed. PR #15 remains Draft. Both workflows on code revision `8f3831e`
were green; green CI does not replace the failed/incomplete live gate.

## Second authorized v2 live run: 2026-09-29 19:41 UTC

The user requested another live qualification to merge PRs #15, #16 and #17 for
deployment. Execution retained the previously agreed ceiling of 10 Gemini and
24 ORS calls, sequentially, without automatic retries or paid fallback.

The exact PR #15 head `8eb9c09005271cfda80904a6cd7a4210c910ee52` was rebuilt in
a separate worktree in Release (zero warnings/errors). The existing preview was
not reused or restarted. The advisor test used the freshly built development
factory; comparison used a temporary loopback API process on port 61876, stopped
after completion. Credentials stayed in local user-secrets.

Ignored local reports in the `refinement-qualification` worktree:

- `artifacts/advisor-qualification-20260929T194138-6efdd7499e3d4b2fb7cf5471c17659f1.json`
- `artifacts/refinement-comparison-20260929T224252-62c41c5b.json`
  (filename uses local time; report `createdAtUtc` is 19:42:52 UTC).

### Advisor contract

All six cases passed. Matching targets and unknown ascent returned stop. Too
long, too short, duration-only and conflicting extreme observations returned
valid advice within the unchanged server bounds. The complete corpus, including
both stop and search responses, is now live-qualified for this run. This is not
a guarantee of future model availability or route-quality improvement.

### Four-city comparison

| City | Baseline result / retained | Advised result / retained | Advisor outcome |
| --- | --- | --- | --- |
| Tel Aviv | passed / 3 | passed / 2 | stopped after two ORS calls |
| Haifa | incomplete / 0 | noMatch / 0 | searched successfully |
| Jerusalem | noMatch / 0 | noMatch / 0 | searched successfully |
| Beersheba | noMatch / 0 | noMatch / 0 | searched successfully |

Tel Aviv's best relative target error remained 0.319% in both arms, with one
fewer ORS call in the advised arm. Haifa baseline reported `routing_unavailable`
for one attempt. All other comparison arms reported no routing failure; all
four advisor calls succeeded. No quotas or unrun arms were reported.

Routes in the other three cities were excluded for missing the original target;
some baseline candidates in Haifa and Beersheba also exceeded the non-road-surface
limit. No matching candidate was available there. The full comparison runner
correctly exited 1: `noMatch` and incomplete runs are not successful route-quality
qualification. No thresholds, tests, corpus or runner semantics were relaxed.

Actual total: **10 Gemini (6 corpus + 4 comparison), 23 ORS**, no repeats. The
advised arm saved one ORS call by stopping in Tel Aviv. There is no demonstrated
general route-quality improvement. Gemini's earlier 503 was not reproduced in
this run, which does not establish an ongoing provider recovery guarantee.

The user was asked whether to accept these known limitations for a test release,
keeping refinement opt-in and making no quality-improvement claim. Until that
explicit release decision, all three PRs remain Draft/unmerged. Successful CI
does not convert the failed comparison into a passed gate.
