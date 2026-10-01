# Local GraphHopper for manual route testing

User authorization: implement autonomously through local manual testing. Cloud deployment is deferred. No new ORS or Gemini requests are authorized.

## Scope

Provide a reproducible self-hosted GraphHopper 11.1 container with Java 25, a persistent regional OSM graph and SRTM elevation cache, and an explicit C# provider switch. Existing React workflows, GPX export, settlement naming, quality exclusions and distance/time constraints continue to work. Do not promise that changing engines alone fixes route quality.

Use the built-in racing-bike access/speed/priority model and elevation model, supplemented by a conservative road model that avoids steps, ferries and known unpaved roads without making prohibited roads accessible. Do not import the automotive example's ignored cycleway/path list. Do not equate primary roads with safe roads.

## Boundaries

- `Routing:Provider` defaults to `OpenRouteService`; `GraphHopper` is opt-in. An unknown provider or invalid server URL/profile fails startup rather than silently using another engine.
- `GraphHopperProvider` implements the unchanged `IRoutingProvider` for A-B and native `round_trip` requests. POST coordinate order is longitude, latitude. Distances are meters and GraphHopper milliseconds are converted to seconds.
- Requests are cancellable, bounded to 15 seconds and 8 MiB, with redirects disabled and no automatic retries/fallback. Provider error bodies and user coordinates are not exposed in errors.
- Parse unencoded LineString geometry and validated path-detail intervals. Missing/unknown surface remains unknown. Separate surface and road-type classification; a cycleway is not automatically asphalt and a footway is not a proven permitted cycleway. Never invent elevation when unavailable.
- Public API contracts remain compatible. AI is not required for local manual generation and remains off for the verification run.
- Docker binds only to host loopback, persists graphs and elevation outside Git, and downloads public artifacts without accounts/tokens. Record exact JAR/PBF/config identity and reuse the imported graph on restart; never reuse incompatible caches silently.
- Readiness means `/info` reports the expected road profile and elevation after import, not merely that the API's `/health` is green. Preserve unrelated processes and data.

## Acceptance

1. Offline adapter and DI tests cover A-B/loop serialization, units, nullable elevation, interval classification, malformed/error responses, cancellation, no fallback and invalid configuration.
2. Build and start the pinned engine, import the regional extract, record resource observations and confirm readiness. Restart must load the graph rather than reimport it.
3. Start the existing local backend/frontend with GraphHopper selected and external paid/quota providers disabled. Exercise real A-B and loop requests, GPX and elevation/segments through the application.
4. Document URLs, launch/stop commands, finite local evidence and known quality limitations for the user. Review the full diff and run CI before integration.

## Non-goals

Cloud resources, commercial GraphHopper API, new user registrations, autonomous road-network waypoint planning, a full routing-engine bakeoff, legal/safety certification, and expanded ORS/Gemini quotas.
