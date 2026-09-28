# Prompt interpretation API

POST `/api/route-intents/interpret` interprets preferences only. It makes at most
one Gemini call and never calls ORS, generates coordinates/GPX, or saves a prompt.
Implementation is offline-tested; real model quality is not yet qualified.

## Request and response

```json
{
  "prompt": "A road loop of 20 km with fewer climbs",
  "locale": "en",
  "start": { "latitude": 32.0853, "longitude": 34.7818 }
}
```

Prompt: nonblank, up to 4000 UTF-16 code units. Locale: exactly `en`, `he`, or
`ru`; it selects the language of application-owned clarification messages, not
the language the user must write. The entire UTF-8 JSON body is limited to 64 KiB.
Start and destination are optional map selections; invalid supplied coordinates
are rejected before any model call. Unknown JSON properties and numeric strings
are rejected. Other than absent/UTF-8 charset, content encodings are unsupported.

HTTP 200 is an interpretation result, not a generated route:

| Field | Meaning |
| --- | --- |
| status | `ready`, `needsClarification`, or `unsupported` |
| draft | Partially extracted preferences with caller-provided coordinates |
| intent | Validated route intent, or null |
| clarifications | Stable field/code pairs and localized messages |
| limitations | Stable codes for unsupported preferences/capabilities |
| assumptions | Explicit defaults such as `elevation_balanced` |

Show the draft, assumptions, and limitations before generating a route.
`ready` does not mean that a traversable, safe, or target-matching route exists.
Use a separate call to `/api/routes/candidates` for road loops or
`/api/routes/generate` for supported road A-B requests after confirmation.

Missing shape/profile/targets cause questions, not invented defaults. An ambiguous
value can remain visible in draft but cannot produce a confirmed intent.
Missing elevation alone defaults to balanced. A valid gravel intent returns
unsupported with `gravel_not_supported`; it is never changed to road.

There is no conversation memory. Resubmit a full revised prompt and current map
selections, not a bare reply like `30`. Named locations or coordinates in text
require map selection; remove the unresolved location wording when resubmitting.
This version cannot verify that a point matches a place name. Cafe stops, exact
ascent, safety guarantees and other unsupported requirements are not silently
accepted. Recognition of these requirements still depends on model quality.

## Configuration

In Visual Studio, use **Manage User Secrets** on `CyclingRoutes.Api`. Add these
configuration names to the existing secrets object without removing the ORS key:

- `Ai:Gemini:ApiKey`: your Gemini Developer API key.
- `Ai:Gemini:Model`: a bare model ID supporting structured output on your project.

The corresponding process environment names are `Ai__Gemini__ApiKey` and
`Ai__Gemini__Model`. Do not put secrets in appsettings, `.http`, commits, screenshots,
or shell commands saved to history. Development loads User Secrets; production
must use the host's secret/environment facilities. Missing AI configuration does
not prevent startup, health, validation, or existing ORS routes.

Before choosing a model, check the official [structured output documentation](https://ai.google.dev/gemini-api/docs/structured-output),
[pricing](https://ai.google.dev/gemini-api/docs/pricing), and your project's quota.
There is deliberately no hardcoded model default or automatic paid fallback.
The app cannot enforce a free tier: use a project/model with free quota and do
not enable billing for these development checks. API keys do not guarantee quota.

The raw prompt is sent to Google. Map coordinates are not sent as separate model
input, but an address written in the prompt still leaves the machine. Use only
synthetic requests until you have reviewed the provider's [data-use terms](https://ai.google.dev/gemini-api/terms).
Do not expose this unauthenticated API publicly before adding access/rate controls.

## Errors and bounds

Bad envelopes/JSON return 400, bodies over 64 KiB return 413, unsupported media
types/charsets return 415. These errors do not consume model calls.

| HTTP | ProblemDetails code |
| --- | --- |
| 503 | `ai_not_configured`, `ai_credentials_rejected`, `ai_rate_limited`, `ai_unavailable` |
| 504 | `ai_timeout` |
| 422 | `ai_request_rejected` (provider safety refusal) |
| 502 | `ai_invalid_response` |

The provider deadline is 30 seconds, including body reading; response bytes are
limited to 256 KiB. There are no retries. Caller cancellation propagates.
The provider must return one completed structured result; malformed/duplicate
fields, invalid enums, truncation, prose wrapping, and extra actions are rejected.
Raw provider errors, keys and prompts are not included in application errors.

## Verification

Offline checks, from the repository root:

```powershell
dotnet test src/backend/CyclingRoutes.slnx --configuration Release --no-restore --maxcpucount:1
pwsh -NoProfile -File tools/evaluate-prompts.ps1
pwsh -NoProfile -File tools/tests/evaluate-prompts.tests.ps1
```

The first script only validates the corpus unless `-RunLive` is present. The
second uses a temporary loopback HTTP stub to test report comparisons, failures,
quota/auth early stopping, and exit codes. Neither proves model accuracy.

For an intentionally configured API already running locally, opt in explicitly:

```powershell
pwsh -NoProfile -File tools/evaluate-prompts.ps1 -RunLive -BaseUrl https://127.0.0.1:7221 -ModelId YOUR_CONFIGURED_MODEL
```

Use a loopback URL on which the API actually listens and whose certificate is
trusted and covers that address. Do not disable certificate checks. For a local
HTTP-only development launch, choose an unused port and explicitly set
`ASPNETCORE_URLS` to `http://127.0.0.1:<port>` with `--no-launch-profile` instead.
The runner rejects non-loopback addresses and redirects; it never receives a key.

There are 27 synthetic cases, including 18 core EN/HE/RU cases. The runner makes
at most one sequential request per case, stops on configuration/auth/quota failure,
and records unrun cases explicitly. It waits 5000 ms between completed requests
by default, keeping this runner below the project's observed 15 RPM limit.
Override with `-RequestDelayMs` (0..60000); use zero only for local stub tests or
when intentionally testing a suitable quota. This does not coordinate other
clients or guarantee TPM/RPD availability. The configured delay is recorded in
the report and excluded from per-request latency. There is no initial delay,
delay after the last case, or wait for cases skipped after a quota failure.
Reports go under ignored `artifacts/` by
default. Exit 0 requires every case to pass; mismatches, incomplete runs, bad
responses and report-write failures exit nonzero. The reported model ID is the
operator-declared configuration, not a value verified against Google's response.

The first live run on 2026-09-28 used operator-declared model
`gemini-3.1-flash-lite`: one passed case (`ru-stop`), 15 `ai_unavailable` errors,
one `ai_rate_limited` error, and eight unrun cases after the quota stop. The
ignored report is `artifacts/prompt-evaluation-20260928T210155-6ed4134d.json`.
Authentication and one end-to-end interpretation succeeded, but multilingual
quality is not qualified. Availability errors combine provider 5xx and transport
failures; this report does not distinguish them. Before rerunning, inspect the
project's active [rate limits](https://ai.google.dev/gemini-api/docs/rate-limits)
and usage in AI Studio. Do not enable billing or repeatedly retry to clear this gate.

After the user supplied project limits (15 RPM, 250K TPM, 500 RPD), a second run
used a 5000 ms inter-request pause. Report:
`artifacts/prompt-evaluation-20260928T213547-309fa02d.json` (ignored).
All 25 cases were attempted: 15 passed, nine returned `ai_unavailable`, and
`en-injection` mismatched (`unsupported` instead of `ready`). Sanitized HttpClient
diagnostics confirmed 16 upstream HTTP 200 and nine upstream HTTP 503 responses
from the requested `gemini-3.1-flash-lite` endpoint. There were no 429 responses
in this run. This narrows the availability failures to provider HTTP responses,
but does not establish the cause of the earlier rate-limit error. The injection
case failed closed with `unsupported_preference`; it did not generate a route.
No model/schema change or weakened corpus expectation was made to hide that result.
Next: investigate provider availability and the injection false rejection
separately, then repeat qualification. No automatic retries or paid fallback.

Contract `prompt-interpretation-v2` clarifies that behavioral/output instructions
are not ride preferences, while actual ride requirements (including cafe stops)
must still be reported. Dataset filename/version remain v1; contractVersion tracks
the changed system prompt. Two additional non-core cases cover mixed injection
with a cafe stop and standalone injection without ride preferences.

A v2 run of the original 25 cases on 2026-09-28 returned 11 passes and 14
upstream HTTP 503 failures, with no semantic mismatches among returned results:
`artifacts/prompt-evaluation-20260928T215154-65bd1d6c.json` (ignored). Separate
probes confirmed en-injection ready; ru/he injection passed in the full run.
The separate probe set also encountered one timeout and two 503 failures.
This is evidence of improvement, not full qualification or guaranteed injection
resistance. All original expectations remain unchanged.

Live qualification remains pending until the corpus completes successfully.
Do not call fixture or fake-provider test results a multilingual model pass rate. Stage 6b, agentic
route refinement, the React UI, and manual road/device checks are separate work.
