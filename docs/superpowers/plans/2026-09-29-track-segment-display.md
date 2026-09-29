# Track Segment Display Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Show surface and road-type patterns on the selected generated track, with localized, accessible segment details.

**Architecture:** Preserve normalized geometry-indexed ORS evidence in the backend and expose it as optional response metadata. Validate annotations separately from route geometry on the client, then build selected-route GeoJSON and MapLibre layers. Keep generation, ranking and GPX unchanged.

**Tech Stack:** Existing C# / ASP.NET Core, xUnit, React / TypeScript, MapLibre GL JS, Vitest and Playwright; no new dependencies.

**Spec:** `docs/superpowers/specs/2026-09-29-track-segment-display-design.md` (user-approved).

## Global Constraints

- Style only generated tracks; preserve the background map and existing cycleway highlighting.
- Default mode is Surface; alternate mode is Road type. Only the selected route receives detailed styling.
- English, Russian and Hebrew, including RTL, mobile and keyboard access.
- No additional ORS, Gemini, geocoding or map-matching calls; synthetic/saved fixtures only.
- No changes to geometry, GPX, road-v1 policy, search, budgets, access claims or Gemini contracts.
- Unknown evidence is never presented as asphalt; generic paved is distinct from asphalt.
- Indices describe edges [fromPointIndex, toPointIndex), with the endpoint included when drawing.
- Theme support and distance/time ranges are separate follow-ups, not silently dropped or implemented here.
- PR #15 remains blocked by its existing live gate. This feature cannot waive that gate.

## Review Focus

- Shared boundaries and zero-length edges: no omitted/duplicated geometry edges (Tasks 1, 3).
- Old or corrupt optional annotations: route and GPX still usable, annotations become unknown (Tasks 2, 3).
- Clicking a dash gap or intersecting alternatives: never accidentally move an endpoint (Tasks 4, 5).
- Repeated mode switches, replacement routes and map retry: no camera reset, stale details or duplicate handlers (Tasks 4, 5).
- Dense segment sets and Hebrew mobile layouts: bounded metadata, usable keyboard list and no overflowing controls (Tasks 1, 4, 5).

## Execution and File Boundaries

Use the existing isolated worktree after rechecking status and attached artifacts. Do not reset the user's preview or create a duplicate worktree. Current base for this feature is `e551d42` on `oleg/agentic-refinement`; refresh this fact at execution time. Keep the existing Draft PR merge gate intact. Use the separate `oleg/track-segment-display` branch based on the current refinement branch. Open a Draft PR targeting main so existing CI (which only triggers for main-target PRs) executes; clearly record its dependency on PR #15 and inherited commits. Do not merge before PR #15 qualifies.

All paths below are relative to repository root. Commands run there unless a frontend working directory is specified. Every task follows RED -> minimal implementation -> GREEN -> scoped commit. Do not rerun live qualification as part of any test command; verify `CYCLING_LIVE_ADVISOR` and other live opt-in variables are unset before tests.

### Task 1: Preserve Normalized Segment Evidence

**Files:**
- Create `src/backend/CyclingRoutes.Application/Routing/RouteSegment.cs`.
- Modify `src/backend/CyclingRoutes.Application/Routing/RoadEvidence.cs`.
- Modify `src/backend/CyclingRoutes.Infrastructure/Routing/OpenRouteServiceEvidenceParser.cs`.
- Extend `src/backend/CyclingRoutes.Tests.Integration/OpenRouteServiceEvidenceTests.cs`.

**Interfaces:**
- Add enums `RouteSurface` (Unknown, Asphalt, Paved, Unpaved, Other) and `RouteWayType` (Unknown, StateRoad, Road, Street, Path, Track, Cycleway, Footway, Steps, Ferry, Construction).
- Add `RouteSegment(int FromPointIndex, int ToPointIndex, RouteSurface Surface, RouteWayType WayType)`.
- Add optional trailing `IReadOnlyList<RouteSegment>? Segments = null` to `RoadEvidence`; preserve existing constructors/fixtures.
- Existing `OpenRouteServiceEvidenceParser.Parse(JsonElement properties, IReadOnlyList<RoutePoint> points, CancellationToken cancellationToken)` produces a complete, coalesced partition alongside unchanged totals.

- [x] Add `Segments_PreserveSurfaceWayBoundaries`: on existing three-point fixture, surface `[0,2,3]`, way types `[0,1,6],[1,2,2]`, assert:

```csharp
Assert.Equal(new[] {
    new RouteSegment(0, 1, RouteSurface.Asphalt, RouteWayType.Cycleway),
    new RouteSegment(1, 2, RouteSurface.Asphalt, RouteWayType.Road)
}, route.Evidence!.Segments);
```

- [x] Add theories for all existing surface codes, missing families, unknown codes and identical adjacent pairs. Map 3 to Asphalt, 1/4 to Paved, current NonRoad codes to Unpaved, current OtherKnown codes to Other, others to Unknown. Assert current quality buckets unchanged. Add a four-point fixture to prove independent surface/way boundaries, unknown gaps and zero-length edges; segment count must never exceed point count minus one.
- [x] Run `dotnet test src/backend/CyclingRoutes.Tests.Integration --filter FullyQualifiedName~OpenRouteServiceEvidenceTests`; confirm new tests fail before implementation.
- [x] Retain normalized intervals during existing range validation. Merge the two ordered partitions with a two-pointer sweep and coalesce identical adjacent pairs. Preserve invalid-response behavior, cancellation checks and existing totals; do not infer metadata from provider summary percentages.
- [x] Rerun the same tests plus `dotnet test src/backend/CyclingRoutes.Tests.Unit --filter FullyQualifiedName~RoadQuality`. Assert malformed ranges still fail and old totals/assessments still pass.
- [x] Commit only the task files: `feat: preserve geometry-indexed route segment evidence`.

### Task 2: Expose One Consistent Optional API Contract

**Files:**
- Create `src/backend/CyclingRoutes.Contracts/RoutePlanning/RouteSegmentResponse.cs`.
- Modify `src/backend/CyclingRoutes.Contracts/RoutePlanning/GeneratedRouteResponse.cs`.
- Create `src/backend/CyclingRoutes.Api/RoutePlanning/GeneratedRouteResponseMapper.cs`.
- Modify `src/backend/CyclingRoutes.Api/RoutePlanning/RouteCandidateResponseMapper.cs` and `RouteGenerationEndpoints.cs`.
- Extend integration tests `RouteGenerationEndpointTests.cs`, `RouteCandidatesEndpointTests.cs`, `RoutePlanEndpointTests.cs`.

**Interfaces:**
- `RouteSegmentResponse(int FromPointIndex, int ToPointIndex, string Surface, string WayType)`.
- Optional trailing `IReadOnlyList<RouteSegmentResponse>? Segments = null` on `GeneratedRouteResponse`.
- `GeneratedRouteResponseMapper.ToResponse(GeneratedRoute route) -> GeneratedRouteResponse`; use in direct and candidate mappings.
- Camel-case category strings exactly match the approved spec. For absent internal segment metadata return one `[0, points.Count - 1]` Unknown/Unknown segment.

- [x] Add endpoint tests with stub providers, including a provider with no evidence. Assert all three endpoint families expose identical normalized segment fields; missing evidence returns `unknown`, never `asphalt`. On unchanged fixtures assert coordinates/order and GPX string equal the pre-change expected values.
- [x] Run `dotnet test src/backend/CyclingRoutes.Tests.Integration --filter "FullyQualifiedName~RouteGenerationEndpointTests|FullyQualifiedName~RouteCandidatesEndpointTests|FullyQualifiedName~RoutePlanEndpointTests"`; observe new assertions fail.
- [x] Implement the additive DTO and shared mapper; leave request schemas, generated geometry and GPX writer untouched. Excluded candidates have no new geometry/segment payload.
- [x] Rerun targeted tests; check loop and A-B responses plus deterministic/advised candidate paths. No changed call counts.
- [x] Commit: `feat: expose route segment metadata consistently`.

### Task 3: Validate Annotations and Build Render Features

**Files:**
- Modify `src/frontend/src/types.ts`.
- Create `src/frontend/src/routeSegments.ts` and `src/frontend/tests/routeSegments.test.ts`.
- Extend `src/frontend/tests/fixtures.ts` with explicit annotated fixtures without changing historical defaults.

**Interfaces:**
- `RouteSegment` contains the four spec fields with literal-string category unions; `GeneratedRoute.segments?: RouteSegment[] | null` is additive.
- `readRouteSegments(value: unknown, pointCount: number): { segments: RouteSegment[]; status: 'valid' | 'missing' | 'invalid' }` validates complete partitions. Missing/null/empty is missing. Malformed nonempty data is invalid. Both produce one unknown interval for valid geometry.
- `segmentFeatures(route: GeneratedRoute, segments: RouteSegment[], candidateIndex: number, color: string): FeatureCollection<LineString>` produces features with `segmentIndex`, `candidateIndex`, `color`, `surface`, `wayType` and inclusive endpoint slices. It does not mutate its arguments.

- [x] Add pure-function tests with four points and intervals `[0,1]`, `[1,3]`; assert coordinate slices have lengths 2 and 3, share only the endpoint and preserve order.
- [x] Add table tests for null/absent/empty, invalid categories, fractional/out-of-range indices, gaps, overlaps, excessive entries, trailing uncovered edges and non-arrays. Assert `invalid` discards the whole list and returns only unknown. Frozen geometry and GPX remain unchanged. Duplicate coordinates must not renumber segments.
- [x] In `src/frontend`, run `npm test -- tests/routeSegments.test.ts`; verify RED.
- [x] Implement validation bounded by point count before iterating metadata; keep it independent of geometry validity checks in `usePlanner.ts`. Do not make optional metadata errors reject valid route responses.
- [x] Run the focused test and `npm run build`; expected PASS and no TypeScript errors.
- [x] Commit: `feat: normalize track annotations for rendering`.

### Task 4: Render Patterns and Accessible Segment Details

**Files:**
- Modify `src/frontend/src/RouteMap.tsx`, `i18n.ts`, `styles.css`.
- Create `src/frontend/src/routeSegmentStyle.ts`, `RouteSegmentControls.tsx`.
- Create `src/frontend/tests/routeSegmentStyle.test.ts`, `RouteSegmentControls.test.tsx`, `RouteMap.test.tsx`.

**Interfaces:**
- `SegmentDisplayMode = 'surface' | 'wayType'`.
- `segmentPattern(segment: RouteSegment, mode: SegmentDisplayMode)` returns `{ dashArray: readonly number[] | null; centerStripe: boolean; caution: boolean }` shared by map and legend. Width-relative dash arrays: unpaved/path/track `[2,2]`, other `[4,2,1,2]`, unknown `[0.5,2]`, cycleway `[6,2]`, footway `[2,1,2,3]`, caution `[1,1]`. Solid has null dashArray; generic paved has centerStripe. Confirm distinctness in browser tests, adjusting rendering details together in one shared definition if needed.
- `RouteSegmentControls` props: locale, mode, onModeChange, segments, status, selectedSegmentIndex (`number | null`), onSegmentSelect. Use a radio group, compact legend, disclosure with an accessible select for segments, and detail region showing both attributes. No HTML injection.
- Map owns mode and active segment state. Route segment source/layers are separate from muted alternatives; feature properties use Task 3 names.

- [x] Write failing style tests covering all category/mode combinations, and component tests for radio keyboard behavior, missing/invalid status, localized labels, exact segment selection and empty routes.
- [x] Add map-adapter tests proving mode changes do not call fitBounds/create a map, and selected-segment clicks (including gap hit areas) do not call endpoint selection. Clear detail selection when results/candidate change or map retry begins. Repeated updates must not add duplicate handlers/layers.
- [x] Run `npm test -- tests/routeSegmentStyle.test.ts tests/RouteSegmentControls.test.tsx tests/RouteMap.test.tsx` from `src/frontend`; confirm RED.
- [x] Implement shared styles, controls and rendering. Draw selected contrast outline, white backing, pattern layers and paved center stripe; exclude selected route from muted solid alternatives so it cannot fill its own gaps. Add a continuous near-transparent hit-test layer queried before candidate layers, with stroke wider than the visible track. Restore layers after map retry and guard handlers against stale props. Only geometry/candidate changes trigger existing fit behavior, never display mode changes.
- [x] Use fixed-height, responsive controls, logical CSS properties and text wrapping. Keep the segment selector within a disclosure rather than rendering thousands of individual controls. Implement all EN/RU/HE labels; never describe a footway as permitted cycling access.
- [x] Rerun tests and `npm run build`; all new and existing component tests must pass. Commit: `feat: inspect selected track surfaces and road types`.

### Task 5: Browser Verification and Delivery

**Files:**
- Create `src/frontend/e2e/track-segments.spec.ts` with self-contained route/style interception following `workflow.spec.ts`.
- Reuse `src/frontend/e2e/basemap.ts`, existing fixtures and `pngjs` pixel checks.
- Create `docs/evaluation/track-segment-display.md` recording actual executed checks, not predictions.

- [ ] Add deterministic browser tests for EN/RU/HE on both configured desktop/mobile projects. Synthetic routes include every category and separated long horizontal segments for stable pixel sampling. Block unexpected external requests; never fall through to live generation.
- [ ] Assert Surface default, both mode patterns, muted alternatives, visible legend, segment selection by pointer and keyboard, exact downloaded GPX, and unknown fallback on legacy/malformed annotations.
- [ ] Sample canvas pixels along known fixture lines to distinguish continuous color from white-backed gaps; capture screenshots for both modes and inspect them. Verify a tap centered on a known gap opens details without moving endpoints. Verify an empty-map click still changes the active endpoint.
- [ ] Verify route replacement, empty results, repeated mode switches and map retry clear stale details; camera remains unchanged for a mode-only switch. Assert no additional API calls during mode switches. Check no horizontal overflow, overlapping controls or hidden attribution at 390px and 1440px widths, including RTL.
- [ ] Run `npm run test:e2e -- e2e/track-segments.spec.ts` from `src/frontend`; use an unused preview port if 4173 is occupied, never kill an unrelated process. Confirm tests fail against missing/broken behavior, then fix only identified integration defects and rerun.
- [ ] Run `dotnet test src/backend/CyclingRoutes.slnx`, then frontend `npm test`, `npm run build`, `npm run test:e2e`, and `git diff --check`. Success requires all tests executed and green; do not count skipped live qualification as passed. If running preview locks assemblies, use Debug or separate output paths, not an unrequested server restart.
- [ ] Review the full feature diff for preserved road quality, budgets, geometry and GPX. Record exact counts, commands, screenshots and limitations in evaluation notes. Commit: `test: verify track segment display across locales and viewports`.
- [ ] Publish the scoped dependent PR, attach it to this task and wait for its final-head CI. State dependency on Draft PR #15 and its unresolved live gate. Do not merge around that gate. Refresh the user's preview only once implementation is verified and no unrelated session is disrupted; report the actual URL and code revision shown.

## Approval and Follow-Ups

Status: user-approved; Tasks 1-4 implemented, Task 5 final qualification in progress.

Preserve the user's autonomous execution preference: execute sequentially in this thread with scoped commits and an independent review when available. Approval of this plan starts implementation; do not ask again for each task.

Dark theme and distance/time ranges remain queued for their own designs after this slice. No live Gemini/ORS retry, provider-budget expansion or merge authorization is inferred from approving this plan.
