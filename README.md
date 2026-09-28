# ai-bike-route-planner

An Israel-first cycling route planner, intended to turn English, Hebrew, and
Russian preferences into rideable routes and downloadable GPX tracks.

Current implementation: .NET 10 API, route-intent validation, provider-backed
point-to-point road routing, ranked road-loop candidates, GPX export, and a
Gemini-backed prompt interpretation API with clarifications. Interpretation is
offline-tested; live model qualification is pending. Gravel-specific routing,
agentic route refinement, persistence, and the React UI are not yet implemented.

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
docker run --rm -p 8080:8080 cycling-routes-api
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
