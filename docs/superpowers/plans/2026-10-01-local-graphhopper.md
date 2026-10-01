# Local GraphHopper Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement task-by-task, with an independent final review.

**Goal:** Bring the self-hosted road-routing engine to local manual testing.

**Architecture:** Keep the existing application/provider boundary and frontend contracts. Add a GraphHopper HTTP adapter and reproducible isolated Docker service; explicitly select it in the local launcher.

**Tech Stack:** .NET 10, GraphHopper 11.1, Java 25, Docker Compose, PowerShell 7.4, existing React/MapLibre.

**Spec:** `docs/superpowers/specs/2026-10-01-local-graphhopper-design.md`

## Global Constraints

- No cloud deployment or ORS/Gemini live requests.
- Preserve original user requirements, backward-compatible API, and unknown evidence.
- Pin engine and data identity; loopback-only service; persistent ignored graph/elevation.
- Explicit provider selection, no fallback/retries, 15-second request and 8-MiB response bounds.
- Preserve the main checkout's dirty documentation and existing private artifacts.

## Review Focus

1. Partially supplied/overlapping/out-of-range path details must not become trusted asphalt or route-type evidence.
2. Provider error responses must not leak raw user locations or become misleading success responses.
3. Unit/coordinate mismatches must not distort distance, time or elevation/GPX.
4. Incompatible or partial caches and interrupted downloads must not silently pass readiness.
5. Local switching and restart must not call external quota services or kill unrelated processes.

### Task 1: Reproducible local engine

Files: `infra/graphhopper/`, `tools/start-graphhopper.ps1`, local engine documentation.

Produces: loopback `http://127.0.0.1:8989/route` and `/info`, profile `road`, with elevation and `surface`, `road_class`, `road_environment` path details.

- [x] Add a bounded synthetic OSM smoke scenario before the engine implementation; demonstrate absent engine/config failure.
- [x] Add pinned image, cycling import/model, Compose persistence and downloader/cache identity launcher.
- [x] Run synthetic smoke; build/import Israel-and-Palestine extract with SRTM and record readiness/resources.
- [x] Verify graph reuse after restart; document exact launch/stop and data limitations.
- [x] Commit the isolated engine infrastructure.

### Task 2: Adapter and explicit provider selection

Files: `CyclingRoutes.Infrastructure/Routing/GraphHopper*.cs`, `CyclingRoutes.Api/Program.cs`, integration tests.

Consumes Task 1's HTTP contract; produces the existing `RoutedPath` through `IRoutingProvider` without API changes.

- [x] Write failing HTTP adapter/DI tests for the Review Focus cases, requests, milliseconds, evidence and error mapping.
- [x] Implement strict parser, evidence mapping and adapter; add validated explicit provider registration with ORS default.
- [x] Run the targeted tests and whole backend suite.
- [x] Commit the adapter/integration.

### Task 3: Local app and manual handoff

Files: `tools/start-local.ps1`, local docs/evaluation notes and regression checks.

- [x] Write failing launcher checks for explicit provider and zero external credentials.
- [x] Add GraphHopper opt-in and readiness to the owned-process launcher; preserve default behavior.
- [x] Start frontend/backend with local engine; test A-B, loops, explicit ranges, GPX, elevation and nonblank map.
- [x] Run full verification and independent whole-branch review, fix blockers, create/attach PR and inspect CI. Final-head green CI remains the separate merge gate below.
- [ ] Merge after green CI under existing authorization; leave a working owned local session for manual testing.

## Plan Review

Self-review: spec coverage and shared contract names checked; tasks separate infrastructure, HTTP boundary and manual workflow. User explicitly requested autonomous completion, so additional interactive design/plan approvals are omitted. Native loop search remains heuristic and manually qualified, not guaranteed to satisfy every range or produce better routes.

## Execution Record

Preflight: existing provider contract and test isolation checked; baseline backend 579 integration + 434 unit tests passed. The clean existing managed worktree was reused; the main checkout's dirty documentation and ignored private files were preserved.

Ruling: infrastructure preparation and adapter implementation ran concurrently with disjoint file ownership because their HTTP contract was fixed; no intermediate review pause was introduced under explicit autonomous authorization. Cost if wrong: contract integration failures, covered by local container/API checks and the final review.

Ruling: select the explicitly pinned 2026-09-29 PBF because 2026-09-30 checksum download failed. Cost if wrong: a one-day older OSM snapshot, never presented as latest data or silently replaced.

Verification before final review: backend Release 637 integration + 434 unit tests, frontend 303 tests and 120 desktop/mobile browser scenarios passed. Engine synthetic smoke passed independently with no regional/elevation downloads. Live application A-B, two loop ranges, excluded 20 km case, GPX/elevation/segments and desktop/mobile screenshots/pixel checks were recorded; no ORS/Gemini calls. Native round-trip failure classification was reproduced, tested RED then GREEN, and verified live.

PR #27 is attached. Initial Linux CI exposed host-owned bind-mount permissions with dropped root capabilities; runtime UID:GID mapping fixes it without relaxing permissions. A Docker-managed Linux-volume regression passed RED then GREEN, and the corrected synthetic engine job passed GitHub CI. Offline evaluation/importer and live desktop/mobile checks were rerun successfully.

Independent full-branch review found one blocker: nonempty truncated MMAP graph data could pass the essential-file check. The new regression reproduced actual server startup with truncated geometry (timeout exit 124 rather than refusal), then passed after atomic completed-data SHA-256 verification was added before Java opens the graph. Cache-integrity format is part of identity; old variants remain preserved. Same-size corruption and missing checksum-manifest regressions were added. Remaining actual merge/CI outcome is tracked in PR #27 rather than predicted here.

Follow-up review confirmed the cache blocker resolved. Final regional import completed in 39.8 seconds (1219 MiB peak), subsequent restart reused it in 7.4 seconds, and regional/live browser checks passed again. Linux backend CI exposed multiple executable matches from `Get-Command dotnet`; selecting one application is now shared by launcher and configuration probe. The multiple-match regression and the full launcher/configuration suite passed locally. No verification gate was bypassed; final merge outcome remains in PR #27.

Backend CI then passed all 1071 .NET tests and the actual Linux launcher/configuration checks. Frontend CI had 119/120 browser cases pass: one pre-existing elevation theme/keyboard case restored the previous hover position after redraw. A stationary native-mouse-event unit regression reproduced the overwrite, then passed after pointer-intent filtering. Clicks/touches and actual movement remain active; independent follow-up review found no blockers and 15 repeated desktop EN/RU/HE elevation scenarios passed. A concurrent local unit run had one five-second timeout in a mocked App scenario; the sequential whole-suite run passed all 304 tests without relaxing timeouts. Final-head CI and full browser verification remain required for release.
