# Local MVP acceptance

The first testable scenario is Israel road routing: select a start (and destination
for A-B), prepare manual preferences, generate bounded candidates, inspect the
selected map/elevation track and download that candidate's named GPX. Prompt
interpretation is an optional separate qualification; GraphHopper sessions
explicitly disable ORS/Gemini credentials. AI refinement stays off.

## Launch and readiness

Use .NET 10, Node 24, PowerShell 7.4+ and the Docker Linux engine. Run from the
chosen checkout. On HAL9000 existing packages, image and regional data are present;
use the cache in that checkout and preserve unrelated sessions/data.

```powershell
pwsh -NoProfile -File tools/start-graphhopper.ps1 -SkipBuild -WaitSeconds 120
pwsh -NoProfile -File tools/start-local.ps1 -RoutingProvider GraphHopper
$session = Get-Content -Raw artifacts/local-servers.json | ConvertFrom-Json
Invoke-RestMethod "$($session.backendUrl)/health"
Invoke-RestMethod http://127.0.0.1:8989/info | Select-Object version, elevation, profiles
```

`-SkipBuild` requires the existing image matching the current engine files. First
setup on another machine uses the full [engine runbook](local-graphhopper.md).
Readiness must report Healthy, engine 11.1, elevation true and profile road. Open
the actual frontend URL printed by the launcher; occupied ports use a fallback.
An existing-session message or old manifest alone is not proof of readiness.

Historical runtime ownership (2026-10-06): the isolated HAL9000 worktree used
`C:\Users\milyu\Documents\Codex\2026-10-06\task\mvp-stability` and add
`-ProjectName ai-bike-gh-stability` for engine start/stop commands. That worktree
has its own copy of the previously verified graph/PBF/elevation cache; the primary
checkout at `D:\sources\ai-bike-route-planner` retains its original data and
uncommitted documents. Do not reuse one workspace's ownership manifest in another.

## Owner checklist

- [ ] In Manual/Road with refinement off, select A-B endpoints by settlement
  search/map. Start selection advances to destination; review actual coordinates.
  Generate without distance/time targets and inspect maneuvers and road access.
- [ ] Generate a 35–45 km road loop from a known start. Record retained count,
  actual distance, attempted count, warnings and exclusion reasons. Zero candidates
  means bounded search found none; it does not prove a suitable route cannot exist.
  Explicit distance/time ranges use exact inclusive bounds, with every supplied
  metric required to match and no extra tolerance. Legacy scalar targets retain
  inclusive 10% tolerance. Never substitute a midpoint for the requested range.
- [ ] Compare all retained routes for unnecessary returns, necessary access stems,
  surface uncertainty and inappropriate roads. Keep unknown surface distinct from
  paved evidence. Ranking or warning thresholds do not certify safety.
- [ ] Inspect the elevation chart by hover/touch and slider Home/End/arrows.
  Confirm the corresponding map marker. Keyboard selection survives theme changes,
  scroll/viewport changes and screenshots; genuine chart input takes over.
  Route switching/input edits clear inspection; missing elevations keep GPX usable.
- [ ] Select another candidate, download GPX, verify filename and `trk/name`, and
  compare point order/elevation with that selected route. No new route call should
  occur on download. Import the GPX on the intended cycling device and inspect it.
- [ ] Check EN/RU/HE, Hebrew RTL, light/dark, desktop and a real mobile browser:
  no horizontal overflow or obscured controls. Geolocation stays opt-in; verify
  permission denial and coarse-location confirmation on the actual phone.
- [ ] Change input during generation, and test a provider-unavailable/empty result.
  Stale results must not reappear, errors must be translated, and unselectable
  results must not enable a stale GPX download.

Record pass/fail/not-run beside each check with date, checkout and evidence.
Keep private GPS files local. Browser fixtures do not complete field/device
acceptance or current Gemini/ORS live qualification.

## Automated checks

### HAL9000 evidence, 2026-10-06

Worktree `oleg/local-mvp-stability`, based on `fd4e9ea`:

- PASS: frontend 313 unit tests, production build; .NET Release build with zero
  warnings/errors and 452 unit plus 639 integration tests.
- PASS: all 122 offline browser cases with no retries. After the independent
  review follow-up, the affected 12 elevation cases and then the full 122-case
  suite were rerun against the final build. Fixtures cover EN/RU/HE, desktop/mobile, themes, missing heights, selected
  GPX, bounded empty results, provider errors and stale-response cancellation.
- PASS: existing local launcher, prompt evaluator, refinement evaluator and
  GeoNames importer regression scripts. These use local fixtures and empty keys.
- PASS: live local GraphHopper readiness (11.1, road, elevation true). Manual A-B
  generated 5.78 km with 103 height-bearing points. A 35-45 km range search retained
  two of three attempts, 42.89 and 41.63 km, excluding one candidate.
- PASS: real local UI generated A-B and the same loop search, selected the second
  loop and downloaded its exact GPX with the correct filename and no new route
  request. API GPX point count, order, coordinates, elevation and track name match
  all three retained routes. Browser basemap requests were replaced locally;
  route/API requests used the actual local engine.
- PASS: fresh independent review; one keyboard-only first-event finding was
  reproduced, corrected and re-reviewed with no remaining material findings.
- NOT RUN: Linux CI on this branch, real-road assessment, cycling-device GPX
  import, real-mobile geolocation and current extraction-v4/advisor-input-v3
  Gemini/ORS qualification. Historical live reports do not qualify these versions.

Earlier concurrent frontend runs hit unchanged 5-second App-test timeouts while
browser/container work was active; the sequential final run passed all 313 tests.
No assertions, retries or timeouts were relaxed. The build retains the existing
bundle-size warning. Detailed logs and the local-only live probe are under
`artifacts/stability`; prior baseline/RED/full-browser evidence is in the parent
task workspace. The original checkout's four dirty documents were preserved and
verified against SHA-256 backups; no push, PR, merge or deployment occurred.

### Primary integration, 2026-10-07

- RED confirmed: three of the four added regressions fail against main's original
  elevation component; no timeout or assertion was changed.
- PASS: all 313 frontend unit tests after porting the fix into the primary checkout.
- PASS: production build and all 122 browser cases without retries (6.1 minutes);
  independent scoped code review found no actionable defects. The existing bundle
  size warning remains. Neither timeouts nor assertions were relaxed.
- IN PROGRESS: exact-head Linux CI. Windows passes do not close that gate.
- NOT RUN: road/device/real-phone acceptance and live Gemini/ORS qualification.

With existing cached dependencies:

```powershell
dotnet build src/backend/CyclingRoutes.slnx -c Release --no-restore --maxcpucount:1
dotnet test src/backend/CyclingRoutes.slnx -c Release --no-build --no-restore --maxcpucount:1
npm --prefix src/frontend test -- --maxWorkers=1
npm --prefix src/frontend run build
npm --prefix src/frontend run test:e2e
pwsh -NoProfile -File tools/test-local-routing.ps1
pwsh -NoProfile -File tools/tests/evaluate-prompts.tests.ps1
pwsh -NoProfile -File tools/tests/evaluate-refinement.tests.ps1
pwsh -NoProfile -File tools/tests/import-geonames.tests.ps1
```

Use one unit worker on this machine when concurrent browser/container work causes
resource contention; assertions and timeouts remain unchanged. Existing offline
fixtures intercept outbound provider requests, exercise A-B/loops, GPX selection,
empty/errors/cancellation and desktop/mobile EN/RU/HE. They use no live keys.

Stop only the chosen checkout's owned app/engine session, retaining its cache:

```powershell
pwsh -NoProfile -File tools/start-local.ps1 -Stop
pwsh -NoProfile -File tools/start-graphhopper.ps1 -Stop
# Isolated stability worktree: add -ProjectName ai-bike-gh-stability above.
```
