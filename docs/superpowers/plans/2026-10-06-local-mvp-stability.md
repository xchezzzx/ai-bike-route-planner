# Local MVP stability implementation plan

Historical execution record for dot's 2026-10-06 local iteration. Its worktree and
no-publish restrictions below describe that iteration only. The owner approved
primary integration and green-CI publication on 2026-10-07 via the
[post-dot plan](2026-10-06-post-dot-route-quality.md).

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task by task. Steps use checkbox syntax for tracking.

**Goal:** Stabilize the existing local road-route planner and record reproducible acceptance evidence.

**Architecture:** Keep React/Chart.js/MapLibre and the .NET provider boundary. Diagnose elevation inspection event ordering and change only the failing interaction. Preserve route geometry, provider budgets and selected-route GPX.

**Tech stack:** .NET 10, Node 24, React, TypeScript, Chart.js, Playwright, GraphHopper 11.1.

**Spec:** User-confirmed iteration on 2026-10-06; existing `docs/frontend/elevation-profile.md`, `docs/development/local-graphhopper.md`, `docs/api/route-candidates.md`.

## Global constraints

- Work on `oleg/local-mvp-stability` in a separate worktree; preserve the primary checkout's four modified documents.
- No new dependencies, paid provider calls, private GPX uploads, push, PR, merge or deployment.
- Manual GraphHopper, Road, AI refinement off is the local acceptance path.
- Keep keyboard selection through theme changes, scrolling and screenshots; deliberate pointer/touch inspection must remain usable.
- Do not weaken assertions, add retries or increase timeouts to hide the defect.

## Review focus

- Pointer events caused by layout/scroll changes must not override deliberate control selection.
- Genuine pointer movement and touch selection must work after keyboard inspection.
- Route changes and invalidated results must clear chart/map inspection.
- GPX download must use exactly the selected candidate without a new provider request.
- Missing elevation, Hebrew RTL and mobile layout must preserve route selection and download.

### Task 1: Elevation inspection regression

Files: `src/frontend/src/ElevationProfilePanel.tsx`, `src/frontend/tests/ElevationProfilePanel.test.tsx`, `src/frontend/e2e/elevation-profile.spec.ts` as needed.

Interface: `onInspect(index: number | null)` continues to select original geometry indices; no API change.

- [x] Inspect the CI trace and reproduce keyboard selection loss through viewport resizing; capture the local native leave events. The exact CI event that restored hover index 15 was not recorded.
- [x] Add regressions asserting that keyboard selection survives leave/reentry and queued pointer input; observe RED.
- [x] Implement the event-handling correction; observe GREEN. Independent review's keyboard-only first-event finding also has RED/GREEN tests.
- [x] Run all frontend unit tests and production build; run all 122 offline browser cases, then rerun the affected 12 elevation cases after the review follow-up.

### Task 2: Local acceptance and documentation

Files: `docs/development/local-mvp-acceptance.md`, relevant current-status sections in `README.md`, `docs/mvp-roadmap.md`, `docs/plans/2026-09-29-mvp-completion.md`, `docs/frontend/elevation-profile.md`.

Interface: existing launch commands and fixture-based APIs; no new product behavior.

- [x] Verify offline .NET build/tests and launcher/evaluation regression scripts with existing cached dependencies.
- [x] Verify app startup with explicitly empty ORS/Gemini configuration and record engine readiness separately.
- [x] Check A-B/loops, selected GPX, errors/cancellation, EN/RU/HE, desktop/mobile through existing suites; also exercise the real local Manual A-B/loop UI and selected GPX download.
- [x] Add a concise owner checklist, clarify stale implemented-feature backlog entries, and distinguish field/device/current provider qualification from automated evidence.
- [x] Obtain fresh independent review; fix the material finding with RED/GREEN tests and obtain a clean re-review.
- [x] Verify the primary checkout is unchanged, record final pass/fail/not-run evidence and preserve this worktree for review.
