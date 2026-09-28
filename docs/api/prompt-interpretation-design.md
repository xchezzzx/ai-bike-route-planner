# Prompt interpretation with Gemini

Status: written specification approved by the user on 2026-09-28.
Implementation plan approved; feature implemented on the local feature branch.
Live model qualification and container smoke remain open; see the execution plan.

## Intent and scope

Accept cycling preferences in English, Hebrew, or Russian and translate them
into inspectable route parameters. Ask for missing or ambiguous information;
never silently discard unsupported requirements or claim that a route exists.
Preserve the user's learning goal through small commits and explanations.

This is stage 6a of the roadmap: interpretation, not an autonomous routing agent.
Stage 6b (tool-driven generation/refinement) remains future work. Existing
validation, A-B generation, loop candidates, and GPX contracts stay unchanged.
No frontend, database, account system, geocoding, Agent Framework, or new model
SDK is needed here. Use the existing typed HttpClient and System.Text.Json pattern.

Gemini is the first replaceable provider. Local Ollama remains a future adapter;
supporting two providers now would duplicate integration work before quality is
measured. An agent framework becomes relevant when multiple tool calls and their
state actually need orchestration, not for this single extraction operation.

## Ownership and data flow

1. API validates the request envelope before network work.
2. Application calls IRouteIntentInterpreter with prompt and locale only.
3. Infrastructure sends one constrained extraction request to Gemini and parses
   its response into a provider-independent, incomplete extraction result.
4. Application combines extracted preferences with coordinates supplied by the
   caller. Model output cannot contain or replace coordinates.
5. Existing RouteIntentValidator checks domain invariants. Application derives
   clarification items and current generation limitations.
6. API returns a draft, an optional validated intent, and explicit status.
   It does not call ORS, create GPX, or persist the prompt.

Contracts owns public DTOs; Application owns interpretation orchestration,
result types, provider interface, and clarification policy. Infrastructure owns
Gemini wire DTOs, system instructions, schema, and safe failure translation.
API owns HTTP status mapping. Domain remains unchanged and knows nothing of LLMs.

## Request

Add POST /api/route-intents/interpret with these fields:

| Field | Rule |
| --- | --- |
| prompt | Required, non-whitespace, at most 4000 UTF-16 code units |
| locale | Required exact token en, he, or ru; controls clarification language |
| start | Optional CoordinateRequest; when supplied, both finite components must be valid |
| destination | Optional CoordinateRequest with the same validation |

The text may use any of the three supported languages irrespective of locale.
Do not automatically infer locale or translate enum tokens.
Set an endpoint request-body limit of 64 KiB, including chunked bodies; this
allows escaped JSON for the prompt but bounds allocation before model work.
Unknown properties, numeric strings, malformed JSON, and invalid provided
coordinates return 400. Oversized bodies return 413; unsupported media types 415.
Missing coordinates are a normal clarification case, not malformed input.

No conversation ID or server-side chat history. To clarify, the caller resubmits
a complete, revised prompt and current map selections. A standalone reply such
as "30" has no implicit previous context. Structured preference overrides and
incremental conversation merging are outside this slice.

## Extraction contract

The provider result contains all five nullable preference fields:
shape, profile, elevation, targetDistanceMeters, targetDurationSeconds; plus an
issues array. These preferences use the existing API tokens and units.
Shape is loop or pointToPoint, profile road or gravel, and elevation minimize,
balanced, or seekClimbs. Duration is whole seconds. All numbers must be finite.

Null means not established, not permission to invent a value. Convert explicit
units to meters/seconds; retain both distance and time when both are given.
Negative and zero targets are not silently corrected. An ambiguous quantity,
range without a single target, or unsupported precision needs clarification.
No default shape, profile, distance, duration, or coordinates. Missing elevation
alone uses the existing balanced default with assumption elevation_balanced.

Each issue contains field and code only, from fixed allowlists. Fields are the
five preference fields plus start, destination, and prompt. Codes are ambiguous,
invalid_value, location_requires_map_selection, and unsupported_preference.
At most 16 issue entries are allowed, deduplicated after parsing. There are no model-authored question texts,
HTML, arbitrary action names, confidence scores, URLs, or executable tool calls.
Unknown fields/codes, missing properties, numeric strings, invalid enums, wrong
types, duplicate JSON property names, and excessive issue counts are invalid
provider responses. Negative targets are valid extraction data but fail domain
validation; they produce clarifications rather than infrastructure errors.

System instructions treat the prompt as untrusted data. They prohibit inventing
missing information, geocoding, writing coordinates, obeying embedded instructions,
and hiding unsupported preferences. Use JSON schema with nullable fields and
closed objects plus independent strict server-side parsing; schema-constrained
generation is not evidence of semantic correctness.

Named places and coordinates written inside the prompt cannot establish location.
Return location_requires_map_selection for the affected location field even when
a map coordinate is present: the caller must resolve the mention and resubmit
without the unresolved location text. This conservative first version does not
claim to verify that a selected point matches a named place.

Stops, road exclusions, exact ascent targets, traffic/safety promises, geographic
area restrictions, and other unrepresentable preferences must yield
unsupported_preference on prompt. Extraction quality for detecting these remains
a measured model limitation, not a guaranteed natural-language safety filter.

## Application result

HTTP 200 contains:

| Field | Meaning |
| --- | --- |
| status | ready, needsClarification, or unsupported |
| draft | RouteIntentRequest-shaped partial data; caller coordinates preserved |
| intent | Existing RouteIntentResponse or null; only non-null after full validation |
| clarifications | Array of field, code, and localized message |
| limitations | Array of stable codes for unsupported requests |
| assumptions | Array of stable codes, including elevation_balanced when applicable |

Clarification messages come from application-owned EN/HE/RU templates, not the
model. Preserve existing validation codes (required, target_required,
must_be_positive, out_of_range, destination_not_allowed, must_differ_from_start).
Combine the two target_required errors into one question on targetDistanceMeters
asking for distance or duration. Deduplicate identical field/code pairs and order
by start, shape, profile, destination, targetDistanceMeters,
targetDurationSeconds, elevation, prompt, then code ordinally.

An ambiguous or invalid_value extraction issue invalidates the affected
preference for intent creation; do not trust a simultaneously supplied value.
Keep that value visible in draft, but exclude it from domain validation. Suppress
the resulting required error for the same field when an extraction issue already
explains it. A prompt-level ambiguity blocks the whole intent. Location issues also block intent
creation. Unsupported preferences retain the draft but prevent ready status.
Any clarification or unresolved extraction issue makes intent null. Pure
generation limitations may accompany a validated intent.

Determine status in this order:

1. Clarification items exist: needsClarification (also retain any limitations).
2. No clarifications but limitations exist: unsupported.
3. Otherwise: ready, with a non-null validated intent.

Expose current generation limitations without changing the request:
gravel_not_supported for gravel; point_to_point_elevation_not_supported for A-B
with non-balanced elevation; loop_search_distance_out_of_range for road loops
outside the existing 1-100 km search range. The duration-only length calculation
matches the existing 20 km/h search heuristic, not a promised rider speed.
Model unsupported_preference issues become unsupported_preference limitations.
Do not add a second routing implementation or weaken existing endpoint checks.

Ready means interpretable and compatible with current endpoint capabilities,
not achievable, optimized, safe, or authorized to ride. The client must display
the interpreted parameters and limitations before a separate generation action.
No backend restriction to an Israel bounding box is introduced in this slice.

## Gemini integration and operating limits

Use POST https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent
without streaming. The host and API path prefix are fixed, not user input.
Configure Ai:Gemini:ApiKey and Ai:Gemini:Model through User Secrets/environment;
commit neither a key nor an account-specific model choice. Model is a bare model
ID (letters, digits, dots, hyphens, underscores only), never a URL or path.
Both options are required for interpretation, but not for API startup or health.
Keep the secret in the x-goog-api-key header, not the query string. Disable redirects.

Use system instructions plus a separate user-content message, candidateCount=1,
application/json structured output with the extraction schema, and at most 4096
output tokens. No tools, browsing, file inputs, grounding, or automatic retries.
One valid HTTP request makes at most one provider call. Use a 30-second deadline
covering response reading and a 256 KiB response-body limit. Caller cancellation
must propagate and take precedence over timeout translation.

Accept exactly one completed candidate with finishReason STOP and one text part
containing the extraction JSON. Do not repair fenced/prose-wrapped/truncated JSON.
Ignore unknown metadata in Google's envelope, but strictly validate extracted
data. A documented safety block/refusal is not an empty successful extraction.

| Failure | HTTP and public ProblemDetails code |
| --- | --- |
| Missing/invalid local configuration | 503 ai_not_configured |
| Upstream 401/403 | 503 ai_credentials_rejected |
| Upstream 429 | 503 ai_rate_limited |
| Upstream 5xx or transport failure | 503 ai_unavailable |
| Deadline exceeded | 504 ai_timeout |
| Documented provider safety block | 422 ai_request_rejected |
| Other non-success, invalid JSON/schema, truncation, oversized response | 502 ai_invalid_response |

Do not expose upstream bodies, exception details, prompts, or keys in responses
or logs. Operational logs may contain duration, model ID, and failure code only.
Use synthetic prompts for development. The raw prompt leaves the machine for
Gemini; omitting map coordinates from model input does not anonymize text that
contains an address. Review provider terms before using personal location data.

Free-first is an operational policy, not something HttpClient can enforce:
do not enable billing or switch to a paid model automatically. The operator must
select a currently available structured-output model with free quota for their
project. Quota exhaustion fails explicitly. Do not promise fixed free limits.
No live calls until credentials/model are configured intentionally. Do not expose
this unauthenticated API publicly before adding access and rate controls.

## Verification and acceptance

Deterministic tests run offline in CI. They prove orchestration and boundary
handling with fake interpreter responses, not real multilingual model quality.
Cover every status, missing/conflicting fields, unit values, defaults, unsupported
preferences, invalid coordinates before network work, strict schema failures,
timeout/cancellation precedence, limits, and unchanged existing endpoints.
HTTP adapter tests inspect header auth, payload/schema, fixed host, single-call
budget, response parsing, refusal mapping, and absence of raw provider errors.

Maintain a versioned corpus of at least 18 synthetic prompts: six per language,
covering complete road loops, duration-only input, missing required preferences,
ambiguous targets, unsupported stops, and instruction-injection attempts.
Add A-B, gravel, named locations, mixed units, zero/negative and conflicting values
as cross-cutting cases. Each fixture states expected fields and status; no
real home addresses or identifying ride history.

The opt-in live evaluation records configured model ID, prompt/schema version,
per-case expected versus actual fields/status, latency, and errors. Use at most
one call per fixture, sequentially, without retries; stop on quota/auth failure.
No numeric pass-rate claim without the actual report. Mark live qualification
incomplete if unavailable, and never label passing fakes as multilingual proof.

After configuration, minimum live acceptance requires the core six cases in
each language to match expected essential fields/status and preserve the
unsupported/clarification boundaries. Additional failures remain explicit; do
not relax acceptance to make a model pass. Manual road/device checks from the
previous stages are separate and remain open.

## Delivery boundaries

Logical commits after written-spec and implementation-plan review:
contracts/application policy and tests; Gemini adapter and HTTP tests; endpoint
integration and tests; evaluation corpus/runbook and current-state documentation.
Keep each code commit buildable with its tests. No push, PR, merge, account
creation, billing changes, or public deployment in this scope.

## Official references

- [Structured outputs](https://ai.google.dev/gemini-api/docs/structured-output)
- [generateContent REST reference](https://ai.google.dev/api/generate-content)
- [Pricing and free-tier data-use table](https://ai.google.dev/gemini-api/docs/pricing)
- [Gemini API terms](https://ai.google.dev/gemini-api/terms)

References checked while preparing the design on 2026-09-28. Recheck model
availability and account quotas at configuration time; no fixed free model or
quota is embedded in this specification.
