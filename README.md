# ai-bike-route-planner

An Israel-first cycling route planner, intended to turn English, Hebrew, and
Russian preferences into rideable routes and downloadable GPX tracks.

Current implementation: .NET 10 API, route-intent validation, provider-backed
point-to-point road routing, ranked road-loop candidates, GPX export, and a
Gemini-backed prompt interpretation API with clarifications, and a local React
testing interface with map selection, manual preferences, candidates and GPX.
Interpretation passes offline tests; extraction v3 passed the earlier 34-case
live corpus (2026-09-29). Current extraction v4 and advisor input v3 still need
fresh live qualification.
Earlier runs had provider errors and semantic mismatches; this is not a production
reliability guarantee. This branch adds opt-in bounded AI road-loop refinement,
but its live qualification is incomplete; see [qualification results](docs/evaluation/route-refinement-2026-09-29.md).
Gravel-specific routing, persistence and public deployment are not implemented.

## Browser testing

Install .NET 10 SDK, Node.js 24 and PowerShell 7.4+. Start Docker Desktop with
Linux containers and wait for its engine to be ready. For current manual road-route
testing, use self-hosted GraphHopper without ORS/Gemini keys. On this Windows
machine, run from the primary checkout. Check ownership before stopping any
older side-worktree session:

```powershell
Set-Location D:\sources\ai-bike-route-planner
pwsh -NoProfile -File tools/start-graphhopper.ps1
pwsh -NoProfile -File tools/start-local.ps1 -RoutingProvider GraphHopper
```

On other machines, substitute your repository root for the `Set-Location` path.
For a faster subsequent engine launch with an image built from the current
engine files, use
`pwsh -NoProfile -File tools/start-graphhopper.ps1 -SkipBuild -WaitSeconds 120`.
Omit `-SkipBuild` on first setup or after changing engine files.

The launcher builds the backend, installs frontend packages if absent and prints
the actual browser URL (normally http://127.0.0.1:5173). It chooses another port
if a preferred port is occupied. Logs and a PID/start-time manifest are under
ignored `artifacts/`. Use **Manual**, **Road**, and keep **AI refinement** off;
this isolated session intentionally disables prompt interpretation and AI advice.
Run the app command again to display the existing owned session. To stop both:

```powershell
pwsh -NoProfile -File tools/start-local.ps1 -Stop
pwsh -NoProfile -File tools/start-graphhopper.ps1 -Stop
```

The [local acceptance checklist](docs/development/local-mvp-acceptance.md)
separates fixture tests from real engine, road, phone and cycling-device checks.

To rebuild/restart only the app after code changes, leaving the engine running:

```powershell
pwsh -NoProfile -File tools/start-local.ps1 -Stop
pwsh -NoProfile -File tools/start-local.ps1 -RoutingProvider GraphHopper
```

### Optional ORS and Gemini testing

Keep ORS and Gemini keys in the API project's User Secrets, never in frontend
variables. Switching providers requires stopping the existing owned app session:

```powershell
pwsh -NoProfile -File tools/start-local.ps1 -Stop
pwsh -NoProfile -File tools/start-local.ps1 -RoutingProvider OpenRouteService
```

Generating routes or interpreting prompts in this mode can consume provider quota.
Select start/destination on the map or enter coordinates. Prompt mode interprets
the text first; review its preferences and explicitly generate a route. Manual
mode validates parameters without Gemini. Optional AI refinement of a prepared
road loop calls Gemini only when Generate routes is pressed and advice is needed.
It is off by default; ordinary generation remains independent of Gemini. Both modes use the selected routing provider's
geometry, show provider warnings and download the selected GPX without another
provider call. No prompts or route history are stored by the UI.

### Self-hosted GraphHopper

The browser-testing commands above start the pinned GraphHopper 11.1 service
(Docker Linux containers) and select it explicitly.

Use **Manual** mode; the GraphHopper launcher session disables ORS/Gemini credentials,
so prompt interpretation and AI advice are intentionally unavailable. Stop
any previous owned app session before switching providers. The UI launcher prints
its actual URL and checks engine readiness; GraphHopper listens on loopback port
8989. Engine data is persistent and ignored by Git. Stop the app and engine
separately. See [setup, evidence and limitations](docs/development/local-graphhopper.md).

Normal launch still defaults to ORS. Direct API hosts can set `Routing__Provider=GraphHopper`
and `Routing__GraphHopper__BaseUrl=http://127.0.0.1:8989/`. Unknown providers/invalid
URLs fail startup; no automatic provider fallback or live retries are added.

The language selector supports English, Russian and Hebrew (RTL). MapLibre uses
OpenFreeMap tiles; loading a map sends viewport/tile requests to that external
service. Map and route-provider attribution remain visible. A tile/WebGL failure
does not disable numeric input or GPX download. Check road access and conditions
yourself; generated routes are not safety-certified.

The Liberty basemap highlights explicit `subclass=cycleway` paths in blue,
including its bridge/tunnel path strokes. Other paths keep the original styling:
`bicycle=yes` alone does not make a footpath a dedicated cycleway. Widths, dashes,
road stacking and route overlays are unchanged. This is a display improvement,
not a complete bicycle-lane inventory, access guarantee or routing preference.
It uses the existing OpenFreeMap tiles, with no new service, key or subscription.

Frontend-only development (run the backend separately on 127.0.0.1:5080):

```powershell
npm --prefix src/frontend ci
npm --prefix src/frontend run dev
```

`BACKEND_URL` configures only the local Vite proxy, not a provider key. The proxy
accepts loopback HTTP URLs only. The production build expects same-origin `/api`;
cloud hosting/authentication/rate limits are a later deployment step.

```powershell
npm --prefix src/frontend test
npm --prefix src/frontend run build
npm --prefix src/frontend exec -- playwright install chromium
npm --prefix src/frontend run test:e2e
```

Browser CI uses deterministic API/map fixtures and consumes no provider quota.
Backend CI also tests the evaluation runner and a no-key Docker container.
Both workflows run for main PRs/pushes; neither is a cloud deployment pipeline.
See the [frontend design](docs/frontend/minimal-test-ui-design.md) and
[implementation plan](docs/plans/2026-09-28-minimal-test-ui.md).

## Local development

Install the .NET 10 SDK. From the repository root:

```powershell
dotnet restore src/backend/CyclingRoutes.slnx
dotnet build src/backend/CyclingRoutes.slnx --configuration Release --no-restore
dotnet test src/backend/CyclingRoutes.slnx --configuration Release --no-build
dotnet run --project src/backend/CyclingRoutes.Api --launch-profile https
```

The API listens at https://localhost:7221. GET /health returns HTTP 200 and
the plain text Healthy. In Development, /openapi/v1.json exposes OpenAPI.
For local HTTPS certificate setup, run `dotnet dev-certs https --trust`.
The IDE request file is src/backend/CyclingRoutes.Api/CyclingRoutes.Api.http.

POST /api/route-intents/validate accepts start/destination coordinates and riding
preferences with explicit meters and whole seconds. It returns validated
parameters (200) or field-level validation codes in ProblemDetails (400).
This endpoint does not generate or save a route. See the
[API contract and example](docs/api/route-intent-validation.md).

POST /api/routes/generate builds one point-to-point road route using the selected
provider (openrouteservice on the current HeiGIT API by default). For ORS, set Routing:OpenRouteService:ApiKey through Visual
Studio's Manage User Secrets for CyclingRoutes.Api (Development), or through
Routing__OpenRouteService__ApiKey in the process environment. Do not commit keys.
Without an ORS key, default generation returns 503; health and validation still work.
Self-hosted GraphHopper does not require a routing API key.

The response includes geometry, estimated metrics, attribution, and a GPX XML
string to save as UTF-8. Targets are not optimized yet, and unsupported intents
return 422 rather than silently changing the request. See the
[generation contract and limitations](docs/api/route-generation.md).

POST /api/routes/candidates searches up to three road-loop candidates and ranks
them against distance/time targets and elevation preferences. Search length must
be between 1 and 100 km. The response includes per-candidate metrics, GPX,
target deviations, and warnings when targets are missed or search is incomplete.
This bounded search does not guarantee an optimal route or verified road safety.
See the [candidate contract and limitations](docs/api/route-candidates.md).

Public test deployments must use Protected access mode and an HTTPS edge. See the
[closed tester deployment guide](docs/deployment/protected-staging.md) for access
settings, shared rate limits, public error codes and hosting restrictions.

POST /api/route-intents/interpret accepts a prompt, locale (en/he/ru), and optional
map coordinates. It returns inspectable preferences or clarifications, never a
generated route. Configure Ai:Gemini:ApiKey and Ai:Gemini:Model separately from
ORS. Without them this endpoint returns 503; the rest of the API still works.
See the [interpretation contract, privacy notes and evaluation runbook](docs/api/prompt-interpretation.md).

Tests use xUnit v3 4.0.0 with the explicit `xunit.v3.mtp-off` package, the
Visual Studio adapter, and VSTest. This preserves the existing GitHub Actions
commands. The plain `xunit.v3` 4.0.0 package enables MTP v2 and requires a
different runner setup; it is not a drop-in replacement here.

On machines where MSBuild creates too many workers, add `--maxcpucount:1`
to build/test commands. The tests do not need a running API or database.

## Container

With Docker Desktop's Linux engine running, use src/backend as build context:

```powershell
docker build -f src/backend/CyclingRoutes.Api/Dockerfile -t cycling-routes-api src/backend
docker run --rm -p 127.0.0.1:8080:8080 -e Access__Mode=Local cycling-routes-api
```

The standalone container serves HTTP on port 8080; no TLS certificate is
bundled. Local mode is anonymous and must stay on loopback. For a combined React
and API image, use the root Dockerfile and `./tools/smoke-protected-deployment.ps1`.
Production defaults to Protected and refuses startup without access settings.
Render handles public HTTPS; the app does not trust arbitrary forwarded headers
or redirect its internal HTTP hop. See the deployment guide before provisioning.

## Structure and progress

- Domain: immutable values and RouteIntent invariants, no external dependencies.
- Application: validation, route generation, candidate ranking, provider interface, GPX.
- Contracts: API request/response DTOs with explicit units.
- Infrastructure: openrouteservice and Gemini HTTP adapters; persistence remains future work.
- Api: host and HTTP endpoints.
- Tests.Unit / Tests.Integration: domain rules and in-memory HTTP verification.

See [domain rules](docs/domain/route-intent.md),
[domain implementation plan](docs/plans/2026-09-28-route-intent.md),
[API implementation plan](docs/plans/2026-09-28-route-intent-api.md),
[MVP roadmap](docs/mvp-roadmap.md), and
[repository review](docs/reviews/2026-09-28.md).
