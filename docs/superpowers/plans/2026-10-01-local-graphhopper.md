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
- [ ] Run full verification and independent whole-branch review, fix blockers, create/attach PR and inspect CI.
- [ ] Merge after green CI under existing authorization; leave a working owned local session for manual testing.

## Plan Review

Self-review: spec coverage and shared contract names checked; tasks separate infrastructure, HTTP boundary and manual workflow. User explicitly requested autonomous completion, so additional interactive design/plan approvals are omitted. Native loop search remains heuristic and manually qualified, not guaranteed to satisfy every range or produce better routes.

## Execution Record

Preflight: existing provider contract and test isolation checked; baseline backend 579 integration + 434 unit tests passed. The clean existing managed worktree was reused; the main checkout's dirty documentation and ignored private files were preserved.

Ruling: infrastructure preparation and adapter implementation ran concurrently with disjoint file ownership because their HTTP contract was fixed; no intermediate review pause was introduced under explicit autonomous authorization. Cost if wrong: contract integration failures, covered by local container/API checks and the final review.

Ruling: select the explicitly pinned 2026-09-29 PBF because 2026-09-30 checksum download failed. Cost if wrong: a one-day older OSM snapshot, never presented as latest data or silently replaced.

Verification before final review: backend Release 637 integration + 434 unit tests, frontend 303 tests and 120 desktop/mobile browser scenarios passed. Engine synthetic smoke passed independently with no regional/elevation downloads. Live application A-B, two loop ranges, excluded 20 km case, GPX/elevation/segments and desktop/mobile screenshots/pixel checks were recorded; no ORS/Gemini calls. Native round-trip failure classification was reproduced, tested RED then GREEN, and verified live.
