# Remaining MVP implementation

The user authorized autonomous implementation, logical commits, PRs and merges
after green CI on 2026-09-29. This plan uses that authorization; paid services,
new live Gemini/ORS qualification and real-device acceptance are not implied.

## Delivery sequence

- [ ] Distance/time ranges: domain, strict API, selection/calibration, prompt
  interpretation, manual UI, tests and documentation; separate PR.
- [x] Explicit browser location as start: accuracy confirmation, stale callback
  fencing, translated errors, browser tests; separate PR.
- [ ] Geographic track names: licensed local settlement data, deterministic
  naming, consistent API/GPX/UI/download; separate PR.
- [ ] Protected deployment: same-origin container, tester access, bounded API
  consumption, secret configuration, CI-controlled deployment and runbook.
- [ ] Public environment verification, only when hosting access is available.
- [ ] User manual road-quality and device checks (not an automated claim).

Geolocation was independently ready first and merged as PR #18 after green PR CI.
The ranges branch is integrated on that main revision; remaining PRs are delivered
sequentially to avoid a stacked queue.

## Range contract

Add optional `targetDistanceRangeMeters: { min, max }` and
`targetDurationRangeSeconds: { min, max }` to request, intent and extraction
drafts. Omission/null preserves current clients. Distance bounds are finite
positive numbers; duration bounds are positive whole seconds within supported
TimeSpan limits. Both endpoints are required, `min <= max`; nested objects are
closed and duplicate fields are rejected. Scalar and range for the same metric
are mutually exclusive. Loops require at least one scalar or range target.

Legacy scalar targets retain inclusive 10% tolerance. Explicit ranges have
inclusive exact bounds without an extra 10%. Every supplied metric must match.
For ranges the reported delta is zero inside the interval and signed distance
to the nearest boundary outside; relative ranking error is normalized by the
range midpoint. Scalar ranking remains unchanged. Initial loop length uses
the distance midpoint, otherwise the duration midpoint and existing assumed
speed; existing provider limits and hard road exclusions remain unchanged.
Calibration uses each range midpoint as its correction aim, but retains the
current length when all supplied constraints already match. This is search
heuristics, not a rewrite of the original request or a feasibility guarantee.

The advisor receives explicit ranges. Extraction must not collapse a stated
interval to a midpoint. Change its contract version and add offline EN/RU/HE
fixtures; mark new live qualification as pending without making live calls.

Each manual metric offers Exact / Range mode with translated labels and stable
responsive numeric fields. Switching clears stale prepared results through the
existing input invalidation flow. Only the active mode is serialized. Prepared
preferences display both bounds with existing units and locale formatting.

## Validation and integration

Use focused failing regression tests before implementation, full backend and
frontend suites, deterministic desktop/mobile browser tests, and independent
review. No test invokes external route/AI providers without a separate explicit
budget. Each completed PR is attached to the chat and merged using squash only
after required CI passes; verify main CI afterward. Preserve the active local
preview and ignored diagnostic evidence in existing worktrees.

## Range delivery verification

Implementation and independent review are complete locally. 359 unit and 438
integration backend tests passed, along with both offline evaluator harnesses
(37 interpretation fixtures). Frontend: 162 tests, production build and 72
desktop/mobile browser cases passed. New range UI was inspected in desktop RU
and mobile HE screenshots. The range request tests were observed failing before
implementation. Review found no remaining actionable defects in the final diff.
Extraction is now v4 and advisor input/prompt v3; prior live qualification does
not cover these revisions. No live provider calls were made. CI/merge pending.

After integrating geolocation from main, all 176 frontend tests, the production
build and all 86 desktop/mobile browser cases passed. One earlier parallel local
test run timed out under load; the complete single-worker rerun passed without
changing any test deadlines. Backend totals remain 359 unit + 438 integration.
