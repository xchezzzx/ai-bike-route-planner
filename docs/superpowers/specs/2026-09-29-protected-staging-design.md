# Protected test deployment

## Scope and authorization

Prepare a closed tester release from main d186bac in the supplied isolated
worktree. Parent owns commits, rebase, PR and merge; this task does none of those,
and does not provision accounts, set secrets, enable billing or call providers.
No frontend source or roadmap edits. Preserve parallel ranges/geolocation/naming
work when parent integrates this branch.

## Access boundary

Use browser-native HTTP Basic authentication, fixed username `tester`, a runtime
password of at least 24 characters, SHA-256 credential hashes compared in constant
time, and a 1024-character Authorization cap. Never log credentials. Production
defaults to Protected and refuses startup without a valid password and canonical
HTTPS PublicOrigin (origin only, no path, credentials, query or fragment).
Development and Testing default to Local; other anonymous execution must explicitly
set Access:Mode=Local. Invalid modes fail startup. Local is not for public hosting.

Protect the SPA, static assets and APIs. Only exact GET/HEAD /health is anonymous.
Every unsafe /api request must have exactly one Origin equal to configured
PublicOrigin; reject missing/null/foreign origins and cross-site fetch metadata.
Do not derive this origin from Host or forwarded headers. No CORS. Keep existing
strict JSON validation. Basic credentials can be cached and sent automatically,
so authentication alone is not a CSRF defense. Browsers provide the login prompt;
there is no reliable application logout (close private session or rotate password).

Render terminates TLS and redirects HTTP at the edge. The container serves HTTP
8080 without application HTTPS redirects or forwarded-header trust. Only publish
it behind a trusted HTTPS-terminating edge. Add no-store, nosniff, frame denial,
no-referrer and Permissions-Policy with geolocation=(self), camera/microphone off;
emit HSTS in protected mode. Do not add a CSP that breaks existing map assets.

## Resource limits

ASP.NET built-in rate-limiter middleware applies an aggregate chained limiter to
authenticated /api traffic: 5 requests per fixed minute, 100 per fixed 24-hour
window, 2 concurrent requests, zero queue. Limits are shared across all clients,
paths and credentials; untrusted client IP/forwarded headers cannot create budgets.
All credential checks (successful or failed, including assets) have a separate
aggregate 20/minute fixed window, acquired before password verification. Exhaustion
temporarily blocks even correct credentials, avoiding an unlimited guessing oracle.
Every limiter rejection returns 429 and Retry-After (concurrency uses 1 second).
ProblemDetails codes: access_unauthorized (401), access_origin_forbidden (403),
access_rate_limited (429).
Health/static assets do not consume API budgets. Local mode skips protection.
All counters are process-local, reset on restart/sleep/redeploy, and fixed windows
can burst at boundaries. They are not provider hard quotas or billing guarantees;
one API request may make multiple provider calls.

## Packaging and release

Root Dockerfile builds unchanged React with Node 24 and ASP.NET 10, bundles dist
into wwwroot, runs as the existing non-root APP_UID. Root .dockerignore excludes
secrets, local configuration, build outputs and unrelated files. No build secrets.
ASP.NET serves the SPA behind access checks; unknown /api paths return 404, never
index.html. Preserve the existing API-only image and smoke with explicit Local.

Render blueprint: one free Docker web service, Frankfurt, main,
autoDeployTrigger: checksPass, /health, no DB/disk/previews/paid resources. Secrets
are sync:false with no values. Connected GitHub is required for automatic checks-
gated deploys; public repository URL deployment alone is insufficient. Owner must
confirm account access, no payment method / zero spend limit, HTTPS origin and
provider quotas before enabling the deployment. No live deployment in this task.

## Verification

Test startup fail-closed, Basic parsing and all protected paths, security headers,
Origin/Fetch-Metadata CSRF failures, strict JSON and no-key 503, aggregate minute,
daily and concurrency budgets, login limiting, local escape hatch, HTTP proxy
behavior and unknown API 404. Build and run full-app Docker smoke without keys,
including actual HTML/assets and failed Production startup. Run the full backend
suite without changing CI test selection. Report unperformed live qualification.
