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

- [ ] Write failing unit/component tests for request conversion, validation,
  errors, cancellation/stale responses, unsupported status and RTL; record RED.
- [ ] Implement typed client, locale dictionary and one-screen workflow from spec.
- [ ] Implement real map markers/routes, camera fit, map failure status and GPX download.
- [ ] Add Playwright API-stub workflows: prompt-confirm-loop-download, manual
  A-B, provider error, input change while pending and mobile Hebrew.
- [ ] Run npm test, npm run build, npm run test:e2e; inspect desktop/mobile
  screenshots and real map tiles, fixing clipping/overflow/blank map.
- [ ] Commit frontend as a coherent tested deliverable.

### Task 3: CI, local launcher and delivery

**Files:** .github/workflows/frontend-ci.yml, .gitignore, tools/start-local.ps1,
README.md, docs/frontend/minimal-test-ui-design.md, this plan.

- [ ] Add node_modules/dist/test output ignores and always-triggered Frontend CI.
- [ ] Add local loopback launcher/run instructions, preserving User Secrets and
  binding no configured provider to public interfaces. Avoid duplicate servers.
- [ ] Independent whole-branch review; fix and retest material findings.
- [ ] Create/attach PR, await every applicable check, squash merge without bypass.
- [ ] Verify main CI, start local UI/API, return URL and explain remaining limits.

## Progress

Planning recorded under user's autonomous authorization. No separate spec
approval is implied. Backend PR #9 merged as 69c9557 after successful PR CI
(run 36469358014). Fresh local backend suite: 400 tests, no skips; evaluator
harness: 27 fixtures with pacing/error regression checks. Main CI run
36469573056 passed, including evaluator and Docker smoke.
Live Gemini qualification is still open, with provider 503/timeouts recorded;
it is not silently treated as complete by the frontend plan.
Frontend implementation and verification are in progress in an isolated worktree.
