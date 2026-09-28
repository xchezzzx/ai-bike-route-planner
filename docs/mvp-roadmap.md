# MVP roadmap

This is the staged direction agreed in the project conversation. Backend:
C#/.NET 10 modular monolith. Frontend: React + TypeScript, web only. Initial
service area: Israel. Languages: English, Hebrew (RTL), and Russian.

1. Repository and CI baseline: solution references, GitHub Actions, protected
   main, pull-request workflow, /health integration test. Completed previously.
2. Route planning domain (issue #4): GeoCoordinate, Distance, profile/shape/
   elevation enums, RouteIntent, invariant tests and documentation. Implemented
   and tested; merged through PR #5.
3. Application/API boundary: explicit request DTOs with units, mapping into the
   domain, validation errors as ProblemDetails, request cancellation and tests.
   Implemented, tested and merged through PR #6.
   See [API contract](api/route-intent-validation.md). Incomplete AI
   extraction remains future work and must stay distinct from a valid RouteIntent.
4. First real route: provider interface and one adapter; route geometry,
   metrics and GPX export. Verify actual road/gravel quality on known Israeli
   routes. Start with a reproducible request before adding natural-language input.
   Point-to-point road adapter and GPX are implemented and submitted for review, with a successful
   live Tel Aviv request. Manual route-quality/device checks remain; gravel is
   explicitly unsupported in this first adapter. See [generation contract](api/route-generation.md).
5. Candidate generation and ranking: loops, distance/time/elevation preferences,
   explainable trade-offs, provider limitations and infeasible-request handling.
6. Prompt interpretation: RU/EN/HE structured extraction, clarification for
   missing parameters, strict validation, test prompts and provider abstraction.
   AI guides candidate construction/refinement through routing tools; graph-based
   routing supplies traversable geometry. Never fabricate GPX coordinates with an LLM.
7. React interface: start-point map selection, prompt, visible interpreted
   preferences, candidates, metrics, GPX download, language switch and Hebrew RTL.
8. Persistence when a concrete use case needs it: PostgreSQL/PostGIS for saved
   requests/routes and caching. Keep authentication and Strava beyond the MVP.
9. Deployment and CI/CD: container smoke tests, environment configuration,
   hosting secrets, staging deploy, production deploy and basic monitoring.

## Previously proposed service shortlist

MapLibre/OpenFreeMap; openrouteservice or GraphHopper; local PostgreSQL/PostGIS;
Ollama or Gemini for development; Cloudflare Pages for frontend; Koyeb for API;
Aiven for cloud PostgreSQL; Sentry and UptimeRobot for monitoring.

These are proposals from the original plan, not verified commitments to current
free tiers. Recheck quotas, commercial terms, Israel coverage, PostGIS support,
and idle/sleep behavior when implementing each integration. An API abstraction
must keep these choices replaceable. There is no service account or deployment
configured by this stage.
