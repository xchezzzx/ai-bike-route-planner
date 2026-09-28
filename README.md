# ai-bike-route-planner

An Israel-first cycling route planner, intended to turn English, Hebrew, and
Russian preferences into rideable routes and downloadable GPX tracks.

Current implementation: .NET 10 API health check and a validated route-planning
domain model. Route generation, AI, persistence, and the React UI are not yet
implemented.

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
- Application: reserved for use cases and interfaces.
- Contracts: reserved for API request/response DTOs.
- Infrastructure: reserved for routing, AI, and persistence adapters.
- Api: host and HTTP endpoints.
- Tests.Unit / Tests.Integration: domain rules and in-memory HTTP verification.

See [domain rules](docs/domain/route-intent.md),
[current implementation plan](docs/plans/2026-09-28-route-intent.md),
[MVP roadmap](docs/mvp-roadmap.md), and
[repository review](docs/reviews/2026-09-28.md).
