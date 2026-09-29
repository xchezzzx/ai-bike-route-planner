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
   Point-to-point road adapter and GPX are implemented and merged through PR #7, with a successful
   live Tel Aviv request. Manual route-quality/device checks remain; gravel is
   explicitly unsupported in this first adapter. See [generation contract](api/route-generation.md).
5. Candidate generation and ranking: loops, distance/time/elevation preferences,
   explainable trade-offs, provider limitations and infeasible-request handling.
   Implemented and merged through PR #8 with bounded three-seed ORS search, pure ranking, exact
   deduplication and partial results. One live Tel Aviv search returned three
   closed loops within distance tolerance; all GPX files passed schema validation.
   See [candidate contract](api/route-candidates.md). Local verification passes
   275 tests after review fixes; Docker smoke and live GPX validation passed during
   initial implementation. A repeated independent Application review completed.
   Manual road/device checks remain separate from automated verification.
6. Prompt interpretation: RU/EN/HE structured extraction, clarification for
   missing parameters, strict validation, test prompts and provider abstraction.
   Stage 6a is implemented and merged through PR #9: Gemini adapter, bounded HTTP
   endpoint, deterministic clarification policy and a 27-case evaluation corpus.
   Offline tests pass. After an initial quota-limited evaluation, a paced live
   run attempted all 25 cases: 15 passed, nine received upstream HTTP 503, and
   one injection case was falsely rejected as unsupported. No 429 in that run.
   Those initial runs did not complete live qualification; see the later evidence.
   Prompt contract v2 clarified injection handling: EN/RU/HE injection scenarios
   received correct successful responses; the latest original 25-case run had
   11 passes and 14 upstream 503 failures. No mismatches among those 11 responses.
   See [interpretation contract and runbook](api/prompt-interpretation.md).
   Contract v3 adds selected-map-point references and targetless A-B requests.
   Earlier v3 run: 32 passed, one ai_unavailable and one Russian targetless A-B
   false location clarification. A later unchanged-contract full run on
   2026-09-29 passed all 34 cases, closing the current corpus qualification gate.
   Prior failures remain evidence of variability, not erased by a successful run;
   manual route input remains independent of Gemini.
   Stage 6b is implemented on the feature branch, not yet delivered to main:
   AI guides candidate construction/refinement through routing tools; graph-based
   routing supplies traversable geometry. Never fabricate GPX coordinates with an LLM.
   The [bounded refinement design](api/agentic-refinement-design.md) was approved
   on 2026-09-29. Its [implementation plan](plans/2026-09-29-agentic-refinement.md)
   is approved and Tasks 1-5 are implemented. Independent review findings were
   corrected with regression tests. Live qualification failed (1/6 advisor cases;
   mixed four-city comparison), so Task 6 delivery/merge remains incomplete.
   See [evidence and next checks](evaluation/route-refinement-2026-09-29.md).
7. React interface: start-point map selection, prompt, visible interpreted
   preferences, candidates, metrics, GPX download, language switch and Hebrew RTL.
   Implemented and merged through PR #10, with manual input as a
   Gemini-independent testing path. Verification: 68 unit/component tests,
   12 desktop/mobile browser tests and a
   live three-candidate ORS search with exact selected-route GPX download.
   Independent review and both PR/main CI workflows passed. The local UI/API
   launch from the primary checkout passed fresh real-map/RTL checks. See the
   [frontend delivery plan](plans/2026-09-28-minimal-test-ui.md).
8. Persistence when a concrete use case needs it: PostgreSQL/PostGIS for saved
   requests/routes and caching. Keep authentication and Strava beyond the MVP.
9. Deployment and CI/CD: container smoke tests, environment configuration,
   hosting secrets, staging deploy, production deploy and basic monitoring.
   Backend CI includes the evaluator harness and no-key Docker smoke; frontend
   CI exercises a production build with deterministic API/map browser fixtures.
   Local loopback launch is available. Public staging/production hosting is not
   configured; free-tier verification and abuse protection remain prerequisites.

## Current execution order

Priority map correction (2026-09-29): highlight explicit cycleways in Liberty
before continuing interpretation qualification and stage 6b. Implemented with
blue path strokes, preserving pedestrian paths, bridge/tunnel ordering and
generated-route overlays. No new map provider or key. Unit regression was RED
before implementation; desktop pixel test found zero blue pixels before the fix
while the pedestrian control passed. Afterward 74 frontend tests, a production
build and all 18 desktop/mobile browser tests passed. Independent scoped review
found no material defects. Real OpenFreeMap tiles at Reading Park show the blue
cycleway next to unchanged white pedestrian paths; screenshot in primary
artifacts/reading-cycleway-20260929.png. This does not verify all on-road bike
lanes or legal access, and it does not change the ORS routing profile.

1. Completed route-request usability delivery through PR #12: optional distance/time for
   A-B, clear generation readiness, supported manual choices and multilingual
   selected-map-point interpretation. See the
   [execution record](plans/2026-09-29-route-request-usability.md).
2. Current full live interpretation corpus passed 34/34, separately from offline
   fixtures. Requalify whenever the prompt/schema/model changes; earlier failures
   still inform availability and production-readiness decisions.
3. Road-loop quality now takes priority following the negative 40 km experiment:
   assess surface evidence and exact retracing, select zero to three near-target
   candidates, and expose uncertainty/exclusions in both search modes. Scope
   approved in conversation; the [written design](superpowers/specs/2026-09-29-road-loop-quality-design.md)
   awaits review and is not implemented. Controlled road-network waypoint
   construction follows as a separate prototype, not a promised engine migration.
4. Stage 6b: bounded AI-guided candidate construction/refinement using routing
   tools, with application-owned budgets and unchanged user constraints.
   Implementation is ready for PR review; first diagnose rejected live advisor
   responses and complete a separately bounded requalification before merge.
5. Add automatic track names and verify actual Israeli route/GPX quality.
6. Prepare public deployment: abuse protection, current free-tier checks,
   hosting configuration, secrets and staging/production verification.

Persistence is not a prerequisite for these deliveries. Stage 6b is implemented
but not live-qualified or merged. Geographic naming, field/device acceptance
and public hosting remain outstanding.

## Planned addition: automatic track names

Requested on 2026-09-29; not implemented. Add to the route/GPX delivery stage
before persistence, without requiring an LLM to invent place names.

- Name each generated candidate using the settlements nearest its actual start
  and finish, the route surface/profile label, and its actual generated distance.
  Example: `Tel-Aviv-Haifa-road-105` and `Tel-Aviv-Haifa-road-105.gpx`.
- Use actual route distance rounded to the nearest whole kilometre, not the
  requested target distance. Keep precise distance available in route metrics.
- Use one canonical name in the API response, route selection UI, GPX track
  name (`trk/name`) and downloaded filename. Selecting another candidate must
  use that candidate's name and GPX.
- Resolve settlement names from geographic data (reverse geocoding or a local
  settlement dataset). Choose the data source during implementation after
  checking Israel coverage, licensing, free quotas and caching requirements.
- Proposed filename convention: English/Latin place names, hyphen separators,
  safe filesystem characters, independent of the selected UI language.
  Proposed loop format: `Tel-Aviv-loop-road-40`, avoiding duplicate endpoints.
- Distinguish the routing profile from verified physical surface coverage:
  `road`/`gravel` must not imply that every segment's surface was checked when
  only the provider's routing profile is known.
- Missing settlement data or a lookup failure must not block route generation
  or GPX export. Use an explicit neutral fallback name, never fabricated places.
- Acceptance: endpoint lookup, loops, same-settlement A-B routes, distance
  rounding, unavailable place names, filename sanitization and consistency
  across UI/GPX/downloads are covered by tests.

## Previously proposed service shortlist

MapLibre/OpenFreeMap; openrouteservice or GraphHopper; local PostgreSQL/PostGIS;
Ollama or Gemini for development; Cloudflare Pages for frontend; Koyeb for API;
Aiven for cloud PostgreSQL; Sentry and UptimeRobot for monitoring.

These are proposals from the original plan, not verified commitments to current
free tiers. Recheck quotas, commercial terms, Israel coverage, PostGIS support,
and idle/sleep behavior when implementing each integration. An API abstraction
must keep these choices replaceable. There is no service account or deployment
configured by this stage.
