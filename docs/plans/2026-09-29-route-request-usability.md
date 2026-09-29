# Route Request Usability

User authorized the roadmap sequence and autonomous logical commits, PRs and
merges after successful CI. This first delivery fixes the observed request flow;
agentic refinement, geographic naming and public deployment remain later stages.

## Scope and decisions

- A-B requires distinct endpoints and a profile, but no distance/time target.
  Loops still require a target. Explicit invalid targets remain errors.
- Extraction contract v3 distinguishes references to map-selected endpoints from
  named places, addresses and literal coordinates. Never suppress model location
  issues merely because coordinates exist: a real conflicting name must still
  require clarification. No geocoding or invented coordinates in the LLM.
- Manual selectors expose only supported choices as selectable options. Existing
  unsupported preferences are not silently rewritten when route shape changes.
- Show the preparation state beside Generate using localized status text.
- Preserve backend-only keys, local binding, quotas and no automatic retries.

## Tasks

- [x] RED/GREEN: targetless A-B in domain, validator, interpretation, endpoints,
  frontend request builder and canonical response validation; loop regression.
- [x] Extraction contract update, multilingual selected-point and targetless A-B
  evaluation cases, named-location regression, corpus/runner version consistency.
- [x] Frontend capability restrictions and visible generation state; component
  and desktop/mobile browser coverage.
- [x] Full backend/frontend/evaluator verification and bounded synthetic live
  qualification. Record real provider failures separately from semantic failures.
- [ ] Independent whole-branch review, fixes, logical commits, PR/all checks,
  merge and verify main. Preserve the requested automatic track-name backlog.

## Evidence

Initial observed UI: a complete A-B road request with selected endpoints and six
hours was blocked by location clarifications; manual A-B exposed an unsupported
minimize-climbs choice. Existing domain/validator required a target for all shapes.

Targetless A-B: five backend and two frontend failures observed before the fix;
404 backend and 70 frontend tests passed afterward. Contract/corpus v3 and
targetless-warning tests then failed (six cases); updated contract plus seven
synthetic corpus entries passed with 407 backend tests. Two new UI tests failed
before capability/status changes; all 72 frontend tests passed after the fix.
The offline evaluator harness passed with 34 fixtures; this is not live model
qualification. Production frontend build and all 14 desktop/mobile browser
tests passed, including targetless A-B, disabled options and Hebrew RTL.

A real ORS request using synthetic Tel Aviv endpoints returned a 6.2 km A-B
route with neither distance nor duration requested. The browser rendered the
map and route, exposed GPX download, and did not show targets_not_optimized.
Screenshot: artifacts/targetless-route-20260929.png in the primary checkout
(ignored; not a CI fixture).

First live v3 run (operator-declared gemini-3.1-flash-lite): 27/34 passed,
four ai_unavailable errors, three semantic mismatches. The English selected-map
points/six-hour case passed. Two Russian cases misread abstract A/B labels as
places; the negative-distance case returned invalid_value instead of application
must_be_positive. Report: artifacts/prompt-evaluation-20260929T112105-5b21fd07.json.

Follow-up contract instructions explicitly distinguish Russian A/B labels from
places and leave signed-target validation to the application. A regression
assertion failed before the change; all 407 backend tests (219 unit and 188
integration) passed afterward. All 72 frontend tests passed again. The second
paced full live run completed with 32/34 passes: en-missing returned
ai_unavailable, and ru-point-to-point-no-target still falsely requested start
and destination selection. All other cases, including the Russian six-hour
request, negative targets and the named-destination conflict, passed. Report:
artifacts/prompt-evaluation-20260929T113106-3459e791.json (ignored), code ebca699.
Exit code 1: full live interpretation qualification remains OPEN. The prompt
already explicitly covers the failing Russian phrase; the application must not
silently discard location issues to manufacture a passing result. No automatic
retries or weakened corpus expectations were introduced.

Provider prerequisites rechecked on 2026-09-29:
[model capabilities](https://ai.google.dev/gemini-api/docs/models/gemini-3.1-flash-lite)
include structured output;
[pricing](https://ai.google.dev/gemini-api/docs/pricing#gemini-3.1-flash-lite)
lists a free standard text tier. Account/model configuration was not changed.
Only synthetic prompts were sent; no keys or raw upstream bodies were inspected.

Independent whole-branch review of 8115159..ebca699 and the pending roadmap/plan
found no actionable code defects. Reviewer separately checked the offline
corpus and diff whitespace, without provider calls or secret access. This
does not override the recorded live semantic failure. PR delivery is for the
experimental local MVP, not production interpretation qualification.
