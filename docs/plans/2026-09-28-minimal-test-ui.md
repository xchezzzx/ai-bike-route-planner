# Minimal Route Testing UI Implementation Plan

> Execute using subagent-driven-development for the independent UI task and
> requesting-code-review before merge. User selected autonomous execution.

**Goal:** Test the real prompt-to-route-to-GPX workflow in a local three-language browser UI.
**Architecture:** One React screen calling existing ASP.NET endpoints through a
loopback Vite proxy; MapLibre supplies visualization, never route generation.
**Tech stack:** React, TypeScript, Vite, MapLibre, lucide-react, Vitest, Playwright.
**Spec:** [Minimal testing UI](../frontend/minimal-test-ui-design.md).

## Global constraints

Preserve backend contracts. No keys in frontend, no fake fallback, no public
deployment or billing. No LLM-generated geometry. Default map near Tel Aviv.
UI EN/RU/HE with RTL. Changes invalidate pending/stale results. All branches use
oleg/ prefix; logical commits, PR, all checks green, squash merge without bypass.

## Review focus

1. A late AI or route response must not restore results after input/locale changes.
2. Unknown ascent and missing targets must not become zero; UI units convert once.
3. Map load failure must leave coordinate fields and GPX download usable.
4. Unsupported/ambiguous preferences must not reach routing implicitly.
5. All result warnings, attribution and Hebrew controls must remain visible on mobile.

### Task 1: Finish backend delivery

- [x] Independent final review of prompt interpretation and evaluation scripts.
- [x] Add offline runner and no-key Docker smoke to Backend CI; run local suites.
- [x] Create/attach PR, verify all CI checks, squash merge, verify main CI.
- [x] Keep live model qualification marked incomplete with actual results.

### Task 2: Build local frontend

**Files:** src/frontend/package.json and lockfile, index.html, tsconfig.json,
vite.config.ts, playwright.config.ts; src/main.tsx, App.tsx, styles.css,
api.ts, types.ts, i18n.ts, RouteMap.tsx and focused tests/e2e.
**Interfaces:** Api functions accept AbortSignal and return contract DTOs from
existing C# contracts; frontend request builder converts km/min to m/sec and
omits blank optional fields. RouteMap consumes selected coordinates, selected
GeneratedRouteResponse geometry, selection mode and onSelect callback.

- [x] Write failing unit/component tests for request conversion, validation,
  errors, cancellation/stale responses, unsupported status and RTL; record RED.
- [x] Implement typed client, locale dictionary and one-screen workflow from spec.
- [x] Implement real map markers/routes, camera fit, map failure status and GPX download.
- [x] Add Playwright API-stub workflows: prompt-confirm-loop-download, manual
  A-B, provider error, input change while pending and mobile Hebrew.
- [x] Run npm test, npm run build, npm run test:e2e; inspect desktop/mobile
  screenshots and real map tiles, fixing clipping/overflow/blank map.
- [x] Commit frontend as a coherent tested deliverable.

### Task 3: CI, local launcher and delivery

**Files:** .github/workflows/frontend-ci.yml, .gitignore, tools/start-local.ps1,
README.md, docs/frontend/minimal-test-ui-design.md, this plan.

- [x] Add node_modules/dist/test output ignores and always-triggered Frontend CI.
- [x] Add local loopback launcher/run instructions, preserving User Secrets and
  binding no configured provider to public interfaces. Avoid duplicate servers.
- [x] Independent whole-branch review; fix and retest material findings.
- [x] Create/attach PR, await every applicable check, squash merge without bypass.
- [x] Verify main CI, start local UI/API, return URL and explain remaining limits.

## Progress

Planning recorded under user's autonomous authorization. No separate spec
approval is implied. Backend PR #9 merged as 69c9557 after successful PR CI
(run 36469358014). Fresh local backend suite: 400 tests, no skips; evaluator
harness: 27 fixtures with pacing/error regression checks. Main CI run
36469573056 passed, including evaluator and Docker smoke.
Live Gemini qualification is still open, with provider 503/timeouts recorded;
it is not silently treated as complete by the frontend plan.
Frontend implementation passed 68 unit/component tests and 12 Chromium browser
tests against the production build after rebasing onto current main. Browser
fixtures cover desktop/mobile, prompt confirmation, manual A-B, stale responses,
provider failures, Hebrew RTL, real canvas interaction and selected GPX download.
Real OpenFreeMap tiles were inspected on desktop/mobile, including Hebrew RTL;
the desktop canvas had 2,429 distinct sampled pixel colors, not a blank canvas.
A live manual Tel Aviv search returned three ORS loops. Selecting the second
downloaded its exact GPX: 19,876.5 m and 456 geometry points. Desktop/mobile
screenshots had no horizontal overflow or page errors. This verifies integration,
not physical road safety, device compatibility or complete model qualification.
The local launcher passed start/reuse/stop, occupied-port fallback, cancellation
cleanup and concurrent-lock checks. Whole-branch review found one P2:
fractional-minute conversion rejected valid input due to floating-point roundoff.
Five new valid-input tests reproduced the failure; the fix and three additional
fractional-second rejection tests passed. Independent re-review closed the P2.

Frontend PR [#10](https://github.com/xchezzzx/ai-bike-route-planner/pull/10)
merged as 302625c after Backend CI run 36472679409 and Frontend CI run
36472679158 passed on final head 9c010f9. Both main workflows passed afterward:
Backend 36472917956 and Frontend 36472918307. No protection bypass was used.
Local API/UI were started from the primary checkout; URL http://127.0.0.1:5173
was returned to the user. Fresh real-tile browser checks passed on main, including
Hebrew RTL, nonblank canvas, no horizontal overflow and no page errors.

## Remaining project work

This local frontend delivery plan is complete. It does not redefine the broader
MVP roadmap or close the original backend plan's outstanding live qualification.
Full Gemini corpus qualification remains blocked by provider 503/timeouts; do
not replace it with offline results or enable billing to bypass the blocker.
Manual route suitability and device checks, agentic route refinement, gravel,
and public staging/production deployment remain separate work. Public deployment
requires a fresh free-tier/account review and abuse protection; local development
servers must not be exposed as a substitute. Persistence remains use-case driven.
