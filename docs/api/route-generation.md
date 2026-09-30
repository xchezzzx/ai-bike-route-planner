# First route and GPX

Stage 4 implements one provider-backed point-to-point road route, not preference
optimization. POST /api/routes/generate consumes the existing RouteIntentRequest.
It accepts shape=pointToPoint, profile=road, elevation omitted or balanced.
Other valid intents return 422 unsupported_intent before any provider call.
Loops and sampled preference ranking are available through the separate
[candidates endpoint](route-candidates.md). Gravel-specific routing remains later work.

200 returns geometry (named latitude/longitude/elevationMeters), distanceMeters,
estimatedDurationSeconds, ascentMeters/descentMeters when available, attribution,
warnings, name, and gpx (a GPX 1.1 XML string of exactly the returned geometry).
The [canonical geographic name](geographic-track-names.md) also appears in GPX
`trk/name`; use `name + ".gpx"` for downloads.
The client can save gpx as UTF-8 without another routing call. There is no route
ID or persistence. A-B requests may omit both targets. Targets are not optimized
yet: results include targets_not_optimized only when a target was supplied.
Provider time is an estimate, not rider-specific fitness.

400 retains the existing validation/binding semantics. Other ProblemDetails
contain an extension code, never the upstream body, coordinates or credentials:

| HTTP | code |
| --- | --- |
| 422 | unsupported_intent, route_not_found, routing_limit_exceeded |
| 503 | routing_not_configured, routing_credentials_rejected, routing_rate_limited, routing_unavailable |
| 504 | routing_timeout |
| 502 | routing_invalid_response |

## Provider choice and configuration

Use hosted openrouteservice, POST https://api.heigit.org/openrouteservice/v2/directions/cycling-road/geojson.
Send coordinates as [longitude, latitude], elevation=true, instructions=false,
and units=m. Request no ferries, fords or steps. API authentication is carried
only in Authorization, never in the URL. Redirects are disabled; requests have
a 15-second timeout and an 8 MiB buffered response limit. No automatic retries:
do not multiply quota usage. No API key means generation returns 503 while
/health and input validation remain usable.

Configure Routing:OpenRouteService:ApiKey through Visual Studio Manage User
Secrets for CyclingRoutes.Api, or the environment variable
Routing__OpenRouteService__ApiKey. Never commit the key or paste it into chat.
The application reads this setting at startup; restart after changing it.
No account, key, subscription, payment, or hosted deployment is created by this change.

The public plans page redirects to the HeiGIT account portal. Verify your
account's current free quota before live use; older indexed quotas are not a
guarantee. No paid service is required by the code. Keep credentials server-side.
This is a local development API, not a public quota-protected service: add
authentication/rate limits before exposing a configured key on a public host.

## Output trust and limits

The adapter checks response shape, finite coordinates/metrics, valid ranges,
at least two points, and positive distance/duration. Invalid success payloads
fail closed; no straight-line or invented fallback route is generated.
GPX includes attribution, one track/segment, and optional elevation, without
invented timestamps or speeds. Longitude +180 is exported as -180 because GPX
1.1 longitude has an exclusive upper bound. Other coordinates are preserved.

Israel is the first intended testing area, not an implemented geofence. Road
legality, access, current closures and safety are not certified by this API.
Live route quality and Garmin/Wahoo import must be verified separately.

## Sources checked

- [ORS request/return formats](https://giscience.github.io/openrouteservice/api-reference/endpoints/directions/requests-and-return-types)
- [ORS routing options](https://giscience.github.io/openrouteservice/api-reference/endpoints/directions/routing-options)
- [ORS error codes](https://giscience.github.io/openrouteservice/api-reference/error-codes)
- [ORS plans](https://openrouteservice.org/plans/)
- [HeiGIT endpoint migration](https://ask.openrouteservice.org/t/deprecating-api-openrouteservice-org-in-favour-of-api-heigit-org/7912)
- [GPX 1.1](https://www.topografix.com/GPX/1/1/)
