# Closed tester deployment

This prepares deployment; it does not create a hosting account or provision a
service. Account access and the connected GitHub repository are still owner tasks.
Do not publish Local mode or expose the container directly over public HTTP.

## Configuration

The root Dockerfile builds the existing React application and ASP.NET API into one
non-root container listening on HTTP 8080. Render provides the public HTTPS origin.
There is no CORS or separate public frontend deployment. Supply runtime variables
in Render's environment settings, never in Git, a Dockerfile or build arguments.

| Variable | Value / requirement |
| --- | --- |
| `Access__Mode` | `Protected` (default outside Development/Testing) |
| `Access__Password` | Owner-generated random secret, at least 24 characters; UTF-8 `tester:` plus password at most 750 bytes |
| `Access__PublicOrigin` | Exact canonical HTTPS origin, e.g. `https://your-service.onrender.com`, no trailing slash/path/query/fragment |
| `Ai__Gemini__ApiKey` | Optional runtime provider secret; absent means interpretation is unavailable |
| `Ai__Gemini__Model` | Explicit approved model when Gemini is enabled |
| `Routing__OpenRouteService__ApiKey` | Optional runtime provider secret; absent means generation is unavailable |

The browser prompts for username **tester** and the shared password. Share it with
approved testers through a private channel. Do not put credentials in URLs.
Basic credentials are cached by the browser: there is no reliable app logout.
Use a private browsing session and close it when finished. Rotate the password
and redeploy to revoke access for everyone; there are no individual user accounts.

Production refuses startup with missing/invalid access settings. For anonymous
local development only, explicitly use `Access__Mode=Local`; Development and
Testing also default to Local. Health GET/HEAD `/health` remains anonymous.
Other health methods, HTML, JS/CSS assets and APIs require authentication.

Unsafe API requests require an exact `Origin` header equal to PublicOrigin, even
for authenticated command-line clients. Missing/null/foreign origins and cross-
site fetch metadata return 403. This guards cached Basic credentials against CSRF.
Existing strict JSON rules still apply (malformed input returns 400). Unknown API
paths return 404, never SPA HTML. No provider keys returns an honest 503.

Render handles TLS and HTTP-to-HTTPS redirection. ASP.NET deliberately does not
redirect the internal HTTP hop or trust X-Forwarded-* / Host as a security origin.
Deploy only behind the HTTPS edge, without exposing the container port publicly.
Responses include no-store, nosniff, frame denial, no-referrer, HSTS in Protected
mode, and `Permissions-Policy: geolocation=(self), camera=(), microphone=()`.
HTTPS geolocation still needs each tester's browser permission. Map tiles use the
existing external tile service; this is not a new paid service or subscription.

## Shared Limits

Authenticated API traffic shares 5 requests per fixed minute, 100 per fixed
24-hour window, and 2 concurrent requests with no queue. All API endpoints count,
including validation and errors after access checks. There is no per-IP, per-user
or forwarded-header bypass. Health and static assets do not consume API permits.
All credential checks and anonymous challenges share a separate 20/minute budget,
acquired before password verification. This includes successful authentication and
static assets: after exhaustion, even correct credentials wait for the next window.
Several testers refreshing simultaneously may hit this intentionally tight limit.
Every limiter rejection returns 429 and Retry-After; concurrency uses 1 second.

These are **ephemeral process counters, not provider quotas or spending caps**.
Restart, deploy, or a new process after sleep resets them. Fixed windows permit
bursts across boundaries. Several provider calls can occur within one API request.
Shared tester credentials do not protect against a malicious authorized tester,
and an attacker can temporarily exhaust the shared login budget. Set provider-side
quotas separately before authorizing live tests. There is no database or durable
quota store, and these limits are not intended as comprehensive DDoS protection.

## Public Error Codes

Read the `code` member of the `application/problem+json` response, not the English
`title`. These identifiers are stable across EN/RU/HE frontend translations.

| HTTP | `code` | Meaning |
| --- | --- | --- |
| 401 | `access_unauthorized` | Missing, malformed or invalid Basic credentials; includes `WWW-Authenticate` |
| 403 | `access_origin_forbidden` | Missing/non-exact Origin or cross-site Fetch Metadata |
| 429 | `access_rate_limited` | Shared login/API window or concurrency limit; includes integer `Retry-After` seconds |

No response echoes credentials or the rejected origin. All 429 causes use the same
code. The frontend translates all three codes into EN/RU/HE without displaying
server details or retrying automatically.

## Render Checklist (Owner)

1. Confirm access to a Render workspace and connect the intended GitHub repository.
   Public-repository-URL deployment alone does not support automatic deploys.
2. Confirm no payment method is attached and set the build-pipeline spend limit to
   zero. Do not enable paid compute, disks, databases or preview environments.
3. Import `render.yaml`: one **free** Docker web service, **Frankfurt**, **main**,
   root Dockerfile/context, port 8080, health `/health`. Verify these before create.
4. Supply the access password and assigned canonical HTTPS origin through the
   `sync: false` prompts/settings. Leave provider secrets/model empty for initial
   no-key qualification. An unknown/missing origin must fail closed; never switch
   to Local to work around setup. Do not add a real secret to this document.
5. After merged CI passes, check public HTTPS health, native Basic challenge,
   protected assets, cross-origin rejection, and 503 with keys absent. This live
   qualification is not performed by the local smoke or by this preparation task.
6. Only after separate approval, supply existing provider secrets and verify their
   quotas/billing settings. Do not create provider accounts or enable billing as
   part of deploying this blueprint.

`autoDeployTrigger: checksPass` enables deploys after linked-branch CI succeeds.
Do not use manual deploys to bypass failing checks. The first Blueprint creation
is an owner action; inspect the selected commit/checks before creating it.
`sync: false` keeps values out of the blueprint, but does not migrate or rotate
existing secrets automatically. Git/PR/rebase/merge are handled by the parent.

Render Free sleeps after 15 idle minutes and provides 750 instance hours per
workspace/month. Bandwidth and build overages can be billed when a payment method
exists; without one, services/builds are suspended instead. A zero build spend
limit alone is not a bandwidth spending cap. Keep no payment method and recheck
current terms before provisioning; Free is not an unconditional no-charge promise.

Official references checked 2026-09-30:
[Free limits](https://render.com/docs/free),
[Blueprint fields](https://render.com/docs/blueprint-spec),
[Deploys and connected repositories](https://render.com/docs/deploys).

## Local Verification

From the repository root with PowerShell 7 and Docker:

```powershell
./tools/smoke-protected-deployment.ps1
```

The script builds frontend and backend sequentially, runs test containers capped
at one CPU/512 MiB, supplies only an ephemeral synthetic login and no provider
keys, checks failed Production startup plus HTTP security/app behavior, and removes
its containers even on failure. It does not touch accounts or live providers.
The same script runs in Backend CI alongside the unchanged backend test job.
The older API-only Docker smoke explicitly selects Local mode.
