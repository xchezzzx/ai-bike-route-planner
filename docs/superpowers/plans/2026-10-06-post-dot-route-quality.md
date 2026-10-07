# Post-dot route-quality follow-up plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement approved tasks one at a time. Approved for execution on 2026-10-07; unchecked tasks are not shipped evidence.

**Goal:** Integrate the verified local UI stabilization, then improve road-route construction rather than only the presentation or sorting of existing candidates.

**Architecture:** Preserve the React/Chart.js/MapLibre frontend and the .NET modular monolith/provider boundary. Establish reproducible baseline evidence before changing routing preferences. Try a bounded slope-priority experiment on the existing GraphHopper graph before committing to a separate turn-aware import or a larger waypoint generator.

**Tech Stack:** .NET 10, React/TypeScript, Chart.js, Playwright, PowerShell 7.4+, Docker, GraphHopper 11.1.

**Spec:** Existing `docs/development/road-loop-geometry-quality.md`, `docs/api/route-candidates.md`, `docs/frontend/elevation-profile.md`; user's 2026-10-06 request to analyze dot's changes and plan further work. Construction experiments below require their own reviewed design before product-code changes.

## Verified starting point

- Primary checkout and origin/main: `fd4e9ea` (PR #28). No new published PR or commit from dot was found.
- Dot's worktree: `C:/Users/milyu/Documents/Codex/2026-10-06/task/mvp-stability`, branch `oleg/local-mvp-stability`, same base commit. Nine changed/new files remain uncommitted: three frontend files and six documents.
- The code change adds control ownership of elevation inspection, timestamped pointer-position tracking, layout-induced mouseout suppression, four unit regressions and one browser scenario run on both projects.
- This analysis independently reran the eleven elevation component tests: PASS. Saved final evidence reports 313 frontend tests, 1091 backend tests and 122 browser scenarios; hashes of the three current code/test files match the final browser evidence. The full suites were not rerun during this analysis.
- Latest main Frontend CI run `37326763812` failed the Russian desktop inspection assertion (expected 20, received 15). Backend CI passed. Dot's uncommitted fix has not run through Linux GitHub CI.
- Live frontend/API are running from dot's worktree, not the primary checkout. Read-only checks returned frontend HTTP 200 and API HTTP 200. GraphHopper `/info` reports 11.1, road, and imported `average_slope`; `max_slope` is absent.
- Persistent startup currently uses ignored `artifacts/stability/start-persistent-local.ps1` and its runner. It is not a checked-in reusable launcher feature.
- Routing backend and engine configuration have not changed. Elevation preference does not reach `IRoutingProvider`: Minimize is only a 20% normalized ascent term applied after candidate generation.

## Global constraints

- The original analysis changed only this plan. The owner's 2026-10-07 approval permits execution with logical commits, PRs and exact-head green-CI merges; the exclusions below still apply.
- Preserve all existing primary-checkout document edits and dot's uncommitted work; reconcile overlapping documentation deliberately.
- Target the primary working tree after integration. Do not remove the stability worktree while it owns running processes or contains unrecovered artifacts.
- No cloud deployment, billing setup, new paid services, ORS or Gemini calls. Future provider qualification needs a fresh explicit request budget.
- Initial acceptance path: local GraphHopper, Manual, Road, AI refinement off.
- Preserve exact inclusive distance/time ranges, scalar 10% tolerance, road/access gates, unknown surface/elevation states, selected-route GPX geometry and current attempt/deadline budgets.
- Manual search remains at most three routing calls with a 45-second total deadline. Advisor-assisted search retains its separately documented limits; no hidden increase to either path.
- Do not cut, move or reconnect arbitrary GPX fragments to disguise an unsuitable route.

## Review focus

1. Layout-generated pointer leave/reentry must preserve keyboard inspection; deliberate pointer and touch input must still take over.
2. An excluded high-ascent route must not silently change the ascent ordering of eligible routes.
3. Minimize must influence construction, without turning missing elevation into zero or becoming an undocumented hard ascent cap.
4. Better ascent or smoother geometry must not come from relaxed range/surface/access rules, fewer successful cases, or increased request budgets.
5. Startup/stop and worktree cleanup must preserve unrelated processes and the original graph/elevation cache; actual process survival must be checked after the launching task ends.

## Task 1: Integrate dot's bounded UI fix

**Files:** `src/frontend/src/ElevationProfilePanel.tsx`, `src/frontend/tests/ElevationProfilePanel.test.tsx`, `src/frontend/e2e/elevation-profile.spec.ts`, `docs/frontend/elevation-profile.md`, the new local acceptance document, and reconciled status sections of README/roadmap/completion plan.

**Interfaces:** Keep `onInspect(index: number | null)` and original geometry indices. No routing or public API changes.

- [x] Reconcile dot's document changes with the four existing primary-checkout edits; do not replace primary files wholesale with worktree copies. Launch runbook edits remain preserved for Task 2.
- [ ] Review and commit the three frontend files and directly related documentation as a logically complete fix; keep private runtime artifacts ignored.
- [x] Run the eleven component tests, all frontend tests, production build, and the full 122-case browser suite without retries, relaxed assertions or larger timeouts. Primary integration: 313 unit and 122 browser tests PASS; three regressions observed RED before applying the production fix.
- [ ] Open a PR and require green Linux frontend/backend/container checks for the exact head. A local Windows pass does not close the main CI failure.
- [ ] After green CI and authorized merge, fast-forward the primary checkout and confirm its committed tree matches the tested head.

**Check:** `npm --prefix src/frontend test -- --maxWorkers=1`; `npm --prefix src/frontend run build`; `npm --prefix src/frontend run test:e2e`. Verify preserved keyboard selection, theme/viewport transitions, touch, Hebrew RTL, and selected GPX. Existing map dimensions must remain stable.

**Deliverable:** Published stabilization fix, green CI, and one unambiguous primary source of product code. This task does not claim improved route generation.

## Task 2: Make local startup reproducible and consolidate runtime

**Files:** `tools/start-local.ps1`, an explicitly reviewed Windows-only helper under `tools/` if needed, `tools/test-local-routing.ps1`, README, `docs/development/local-graphhopper.md`, and the local acceptance checklist.

**Interfaces:** Preserve existing `-Stop`, `-RoutingProvider`, `-GraphHopperUrl`, PID/start-time ownership checks, port fallback, hidden windows, and loopback-only networking.

- [ ] Audit the ignored WMI prototype and separate production launch logic from diagnostic Windows job probes. Do not copy machine-specific wrappers blindly.
- [ ] Add tests for broker rejection, unavailable dependencies, stale manifests, provider mismatch, partial startup and foreign PID reuse before changing the launcher.
- [ ] Implement a documented persistent launch option if the audited approach is suitable. Retain normal shell startup and safe failure behavior on unsupported platforms.
- [ ] Test owned stop/restart and verify frontend/API remain available with identical PID/start-time identities after the launching task ends.
- [ ] Transfer the app runtime to the primary checkout at the tested version. Preserve engine/cache ownership and stop only the stability worktree's owned app processes.
- [ ] Recover needed ignored evidence and confirm no process/cache depends on the side worktree before archiving it. Do not delete caches as cleanup.

**Deliverable:** A tracked launch command, verified after-task survival, and matching documentation. No firewall/TLS/service changes are implicit.

## Task 3: Establish a reproducible quality baseline and isolate ranking effects

**Files:** `src/backend/CyclingRoutes.Evaluation/EvaluationCli.cs`, `src/backend/CyclingRoutes.Evaluation/OfflineRouteEvaluator.cs`, `src/backend/CyclingRoutes.Application/Routing/RoadCandidateSelector.cs`, `RouteCandidateRanker.cs`, their existing tests, and a dated quality report under `docs/evaluation/`. Create `tools/evaluate-road-loops.ps1` only if the current offline CLI cannot provide the required bounded local comparison; this script does not exist yet.

**Interfaces:** Existing candidate response, `excludedCandidates`, offline replay, exact-repeat diagnostics and near-return heuristic. Any new local diagnostic export must be opt-in and kept out of Git.

- [ ] Capture one exact problematic request and response locally: start, requested ranges, elevation preference, provider/profile, seeds and requested search lengths. The historic 1400+ m complaint cannot be reconstructed from engine logs alone.
- [ ] Freeze a public-city corpus including flat and mountainous starts plus the user's private good/bad GPXs as local-only controls. Record graph/model identities and bounds before comparison.
- [ ] For each attempt record provider outcome, duplication, actual distance/time/ascent, retained/excluded reasons, unknown surface/elevation, exact repeats and approximate near returns. Avoid logging prompt text, coordinates or full GPXs by default.
- [ ] Add a selector regression asserting that adding an excluded outlier does not alter eligible ascent ordering. Current normalization uses the maximum ascent before eligibility filtering; this is pre-existing, not introduced by dot.
- [ ] Review the scoring change separately: preserve 80/20 weights unless explicitly redefined, compute the comparison normalization over eligible candidates, and retain honest excluded diagnostics and full-set target-match bookkeeping.
- [ ] Publish baseline counts and timing, with field/manual judgments distinct from automated heuristics.

**Deliverable:** An exact reproducible failure and a baseline suitable for comparing construction changes. Weight tuning alone cannot improve the geometry or ascent of a lone surviving route.

## Task 4: Propagate elevation preference into GraphHopper construction

**Files:** `src/backend/CyclingRoutes.Application/Routing/IRoutingProvider.cs`, `RouteCandidateService.cs`, `RoutePlanningService.cs`, `RouteGenerationService.cs` (A-B), `src/backend/CyclingRoutes.Infrastructure/Routing/GraphHopperProvider.cs`, `OpenRouteServiceProvider.cs`, provider fakes/tests, and a separate reviewed experiment design.

**Interfaces:** Define a typed elevation-preference input on the internal provider boundary before implementation. Both normal and advisor search must pass the same original preference; public request preferences must not be overwritten.

- [ ] Write and review the small provider-contract/experiment design, including Balanced baseline, Minimize behavior, unsupported SeekClimbs behavior, and the scope of A-B support.
- [ ] Add payload and orchestration tests: Minimize reaches the adapter; Balanced preserves baseline behavior; all fakes/providers compile; limits/cancellation/range gates are unchanged; no ORS fallback occurs.
- [ ] Probe a request-level priority rule using imported `average_slope` on the current flexible-routing profile. Check signed-direction behavior and unknown elevation handling. Do not assume `max_slope` is available.
- [ ] Keep the base racingbike/elevation/road models and access/surface restrictions. Prefer priority changes for the experiment rather than falsifying speed/provider duration.
- [ ] Compare Balanced and Minimize on matched starts, seeds, lengths and graph identity. Report retained counts, ascent distribution, duration, near returns and latency, including failed or empty cases.
- [ ] Adopt a model only if evidence shows an ascent benefit without materially degrading eligibility, geometry or budgets. Otherwise preserve baseline and publish the failed experiment; Minimize is not a guarantee or hard cap.

**Deliverable:** Evidence-backed construction preference, or a documented negative result. Average-slope capability already exists, so reimporting the graph is not the first prerequisite for this experiment.

Reference: [GraphHopper 11.1 custom models](https://github.com/graphhopper/graphhopper/blob/11.1/docs/core/custom-models.md). Request-level acceptance still needs a local capability probe; imported metadata alone is not proof of effect.

## Task 5: Improve native loop construction; prototype waypoints only if needed

**Files:** Separate design, `infra/graphhopper/config.yml`, `road.json`, engine launcher/import tests, adapter integration tests and saved-response evaluation. A larger waypoint generator requires its own reviewed module boundaries.

- [ ] Use the same corpus to distinguish instructed reversals/junction penalties from long close return passages on different graph edges.
- [ ] Verify exact GraphHopper 11.1 turn-cost/encoded-value prerequisites before selecting penalty expressions. The previous `orientation` probe failed; do not add ineffective request values.
- [ ] Import a separately named experimental graph/profile with a distinct cache identity, preserving the baseline cache. Measure import time, memory and route latency.
- [ ] Compare baseline and experimental profiles under identical controls. Judge complete route geometry, not only maneuver count; necessary access stems and hairpins must remain viable.
- [ ] If native construction still produces inadequate rings, design road-network waypoint generation and use GraphHopper for connected legs. Account for leg calls in a separately approved total budget before implementation.
- [ ] Promote only verified changes; do not make unsafe main-road preferences or broaden access rules to obtain simpler-looking tracks.

**Deliverable:** A demonstrated construction improvement or evidence that justifies the separate waypoint project. Soft warnings alone do not close this task.

## Task 6: Complete real-world acceptance; leave cloud and AI qualification separate

**Files:** Local acceptance checklist and dated evaluation report; no extra UI feature work is required just to record results.

- [ ] Have the owner assess all retained routes in familiar regions and record unnecessary maneuvers, road suitability, surface uncertainty and necessary access stems.
- [ ] Import a selected GPX on the intended cycling device and confirm point order, name and elevation; mock/browser tests cannot prove device compatibility.
- [ ] Agree on an HTTPS entry point reachable from the real phone before geolocation acceptance. Loopback HTTP and mobile emulation do not satisfy this field check; no network/security setup is performed implicitly.
- [ ] Later, agree a fresh Gemini/ORS budget for extraction-v4/advisor-input-v3 qualification. Historical v3 reports do not qualify current versions.
- [ ] Choose cloud hosting in the separately requested joint session only, after route-quality acceptance and current resource measurements.

**Deliverable:** Explicit pass/fail/not-run acceptance status, not a blanket production-ready claim.

## Recommended order

Task 1 -> Task 2 -> Task 3 -> Task 4 -> Task 5 -> Task 6.

The immediate next action is to integrate and publish dot's existing bounded UI fix with green Linux CI. The next major product investment is Task 4 (construction-aware Minimize), followed by Task 5 (logical loop construction), not more cosmetic UI features or cloud deployment.
