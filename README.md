# ai-bike-route-planner

An Israel-first cycling route planner, intended to turn English, Hebrew, and
Russian preferences into rideable routes and downloadable GPX tracks.

Current implementation: .NET 10 API, route-intent validation, provider-backed
point-to-point road routing, ranked road-loop candidates, GPX export, and a
Gemini-backed prompt interpretation API with clarifications, and a local React
testing interface with map selection, manual preferences, candidates and GPX.
Interpretation is offline-tested; full live model qualification remains pending
because of provider availability and remaining semantic mismatches. Gravel-specific routing, agentic route
refinement, persistence and public deployment are not yet implemented.

## Browser testing

Install .NET 10 SDK, Node.js 24 and PowerShell 7.4+. Keep the ORS and Gemini
keys in the API project's User Secrets, never in frontend variables. Start both
loopback-only servers from the repository root:

```powershell
pwsh -NoProfile -File tools/start-local.ps1
```

The launcher builds the backend, installs frontend packages if absent and prints
the actual browser URL (normally http://127.0.0.1:5173). It chooses another port
if a preferred port is occupied. Logs and a PID/start-time manifest are under
ignored `artifacts/`. Run it again to display the existing owned session, or stop:

```powershell
pwsh -NoProfile -File tools/start-local.ps1 -Stop
```

Select start/destination on the map or enter coordinates. Prompt mode interprets
the text first; review its preferences and explicitly generate a route. Manual
mode validates parameters and does not call Gemini. Both modes use real ORS
geometry, show provider warnings and download the selected GPX without another
provider call. No prompts or route history are stored by the UI.

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

POST /api/routes/generate builds one point-to-point road route via openrouteservice
on the current HeiGIT API. Set Routing:OpenRouteService:ApiKey through Visual
Studio's Manage User Secrets for CyclingRoutes.Api (Development), or through
Routing__OpenRouteService__ApiKey in the process environment. Do not commit keys.
Without a key, generation returns 503; health and validation still work.

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

Do not expose a configured API publicly before adding authentication/rate limits.

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
docker run --rm -p 127.0.0.1:8080:8080 cycling-routes-api
```

The standalone container serves HTTP on port 8080; no TLS certificate is
bundled. HTTPS termination and forwarded headers must be configured for the
chosen hosting service before deployment.

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
