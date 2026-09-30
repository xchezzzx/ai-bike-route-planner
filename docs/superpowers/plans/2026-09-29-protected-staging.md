# Protected staging implementation plan

Design: ../specs/2026-09-29-protected-staging-design.md
Execution: native, autonomous as explicitly requested; no commits/push/provisioning.

## Tasks

1. Write integration tests in AccessProtectionTests.cs using a Production factory
   with in-memory test credentials and no provider keys. Assert 401 on app/assets/
   API, anonymous GET/HEAD health only, Basic challenge, invalid config startup
   rejection, Local compatibility, exact-origin 403, strict JSON 400 and valid
   no-key request 503. Run filter AccessProtectionTests and observe RED.
2. Implement Access/AccessOptions.cs, AccessMiddleware.cs and pipeline registration
   in Program.cs. Validate options on startup; cap and hash Basic credentials;
   apply security headers before failures, CSRF before endpoint execution. Add
   explicit Access:Mode=Local only in existing tests that force Production.
   Serve static files and SPA; reserve /api fallback. Run security tests GREEN.
3. Add tests for shared 5/min API and 20/min login-check limits, daily 100 capacity,
   Retry-After, and 2 concurrent provider calls with zero queuing. Observe RED.
   Implement AccessRateLimits.cs with built-in fixed-window/concurrency limiters,
   used by ASP.NET global rate-limiter middleware, not IP partitions. Daily factory
   tests exercise the same limiter constructor used in the chain. Run GREEN.
4. Add root Dockerfile/.dockerignore, render.yaml and tools/smoke-protected-deployment.ps1.
   Smoke: missing-config process exits, health, unauthenticated HTML/assets/API 401,
   authenticated HTML/assets, missing/foreign origin 403, malformed JSON 400,
   valid no-key interpret 503, unknown API 404. Add CI job with no secrets; leave
   existing test selection unchanged and set old API smoke explicitly Local.
   Build/run smoke locally with sequential, CPU-conscious builds.
5. Add docs/deployment/protected-staging.md with runtime configuration, account/
   GitHub blocker, native prompt/logout limits, TLS and cost/quota caveats and
   official Render sources. Run full backend suite and existing evaluation-script
   checks; self-review all scoped changes and record verification below.

## Review focus

- Misconfigured production must fail at startup, not serve anonymously.
- Cached Basic credentials must not bypass absent or hostile Origin checks.
- Proxy headers and Host must not affect security origin or create limit budgets.
- Static files/fallback must never bypass authentication or turn API 404 into HTML.
- Minute/login/API windows and concurrency must not queue or depend on client IP.

## Execution record

- Preflight: clean supplied branch oleg/protected-staging at d186bac; Docker Linux
  engine available. Design/plan written before tests or production code.
- Ruling: use supplied autonomous execution authorization without additional
  approval gates; parent retains all git and account/deployment operations.
- Access tests: initial RED confirmed missing authentication/CSRF/configuration
  enforcement; 44 passed after implementation. Rate tests: 3 initial failures
  confirmed missing minute/login/concurrency protection; 48 access/rate tests green.
- Ruling: bound all Basic credential verification at 20/min before hashing, not only
  failed responses; otherwise the correct guess remains detectable after the budget
  is exhausted. Cost: shared temporary lockout includes correct clients and assets.
- Full-suite first run exposed API fallback masking 415 with 404 and a TestServer
  startup-validation disposal race. Exclude API in the SPA route constraint and
  resolve validated options before the hosting loop; focused 64 tests passed.
- Public error codes requested by parent are access_unauthorized (401),
  access_origin_forbidden (403), access_rate_limited (429), tested after RED for
  absent ProblemDetails codes. Retry-After is integer seconds.
- First Docker smoke passed with actual bundled React HTML/assets and no provider
  keys; final verification after all fixes is recorded below.
- Test isolation incident: first Development-mode access test unexpectedly returned
  200 instead of no-key 503, indicating an unintended Gemini call through local
  configuration loaded before test overrides. No secret values were read/displayed.
  Access test factory now replaces provider options and forbids outbound HTTP;
  full-suite commands override provider configuration with invalid whitespace and
  clear the live-advisor flag. No further live provider calls are authorized.
- Final self-review found that named provider registrations override default HTTP
  handlers. Three loopback-only regression tests failed, then passed after test
  factory PostConfigureAll<HttpClientFactoryOptions> installed the network blocker
  after named options. No real provider was used for these regression probes.
- Docker regression: method-constrained SPA routing returned 405 for unknown API
  POST/DELETE. Three new cases failed; serving SPA only after an unhandled non-API
  GET/HEAD 404 fixed this without masking existing 415 responses. All four unknown-
  API method/path cases now pass; actual SPA navigation passed Docker smoke.
- Final backend verification (2026-09-30): DOTNET_PROCESSOR_COUNT=2, live-advisor
  disabled and provider settings overridden with whitespace; `dotnet test
  src/backend/CyclingRoutes.slnx --configuration Release --verbosity minimal
  --no-restore -m:1`: 432 integration + 316 unit = 748 passed, 0 failed, 0 skipped.
- Both `tools/tests/evaluate-prompts.tests.ps1` and
  `tools/tests/evaluate-refinement.tests.ps1` passed offline. Their deliberate
  malformed/quota/disconnect scenarios print errors, but harness exit codes are 0.
- Final `tools/smoke-protected-deployment.ps1` passed, including full React/API
  build, startup rejection, anonymous GET/HEAD health, 401/challenge on app/assets/
  API, authenticated assets and SPA navigation, CSRF 403, malformed JSON 400,
  valid no-key request 503, unknown API GET/POST 404, and 429/Retry-After/error codes.
  Temporary containers removed. Existing large MapLibre chunk warning remains;
  no frontend restructuring is in scope.
- Final review: self-review (no subagent tool); separate security review by parent
  remains necessary. No outstanding implementation finding; no provisioning or
  real-cloud TLS/Basic/geolocation/provider qualification was attempted.
- Tasks 1-5 complete. Git index/commits/push/rebase/PR left to parent, including
  CRLF normalization and integration with ranges/naming/geolocation. Parent-owned
  frontend i18n/access-test changes were not edited by this task.
- Independent security review found no production-access defects, but identified
  the same inherited configuration-snapshot problem in three older no-key test
  fixtures. Seven regression cases failed on synthetic credentials before any
  network call. Shared `OfflineProviders` now overrides both provider options and
  blocks all default/named HTTP clients in these fixtures and access tests.
  Eleven focused isolation tests then passed; CI repeats them with synthetic
  credentials. Assertions do not print credential values on failure.
- Parent frontend translations for 401/403/429 passed three RED-to-GREEN tests in
  EN/RU/HE; the client preserves safe error codes and does not retry automatically.
- Final integration on main after PRs #19/#20: 392 unit + 518 integration = 910
  backend tests passed, 192 frontend tests passed, all 11 isolation regressions
  passed with synthetic inherited credentials, and the full-app Docker build and
  no-key smoke passed again. Temporary containers were removed. PR CI remains a
  separate merge gate; real hosting has not been provisioned.
