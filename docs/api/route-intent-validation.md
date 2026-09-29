# Route intent validation API

Stage 3 of the MVP roadmap exposes the existing domain invariants over HTTP.
It does not persist a request, generate a route, or call AI/routing services.

## Contract

POST /api/route-intents/validate accepts application/json:

```json
{
  "start": { "latitude": 32.0853, "longitude": 34.7818 },
  "shape": "loop",
  "profile": "road",
  "targetDistanceMeters": 40000,
  "targetDurationSeconds": 7200,
  "elevation": "balanced"
}
```

- Coordinates are numeric degrees. Omitted/null coordinates are not zero.
- shape: loop or pointToPoint; profile: road or gravel. Both are required.
- elevation: minimize, balanced, or seekClimbs. Omitted/null means balanced.
- Tokens are case-sensitive. Unknown tokens are rejected, not guessed.
- targetDistanceMeters is a finite positive number when supplied.
- targetDurationSeconds is a positive whole number, at most 922337203685
  (the largest whole-second duration representable by TimeSpan).
- Optional targetDistanceRangeMeters and targetDurationRangeSeconds are closed
  `{ "min": ..., "max": ... }` objects. Both bounds are required and positive;
  min must be <= max (equal bounds are valid). Distance bounds are finite numbers;
  duration bounds are whole seconds, at most 922337203685 each.
- Omitted/null ranges preserve scalar clients. A scalar and range for the same
  metric are mutually exclusive; different metrics can independently use either.
- Loops require at least one distance/time scalar or range. For pointToPoint all
  targets are optional; supplied invalid values are still rejected.
- destination uses the same coordinate object. It is prohibited for loop,
  required for pointToPoint, and must differ from start by coordinate value.
- No service-area, feasible-length, road-access, or route-safety guarantee is made.
- Unknown and duplicate JSON properties (including nested bounds and
  case-insensitive aliases) are rejected to catch misspelled preferences.

200 returns validated parameters with explicit balanced default, numeric units,
and null for absent destination/targets. This is a validation result, not a route.
There is no ID, Location header, geometry, or GPX at this stage.

400 for semantic errors returns application/problem+json with errors keyed by
JSON field paths (for example start.latitude). Error arrays contain stable codes:
required, invalid_value, out_of_range, must_be_positive, target_required,
destination_not_allowed, must_differ_from_start. A missing pair of targets is
reported on both target fields for loops (also when shape is unresolved).
Range errors use `range_reversed` and `target_conflict` on the range field.
Missing, nonpositive or oversized/nonfinite bounds use `required`,
`must_be_positive` or `out_of_range` on the `.min`/`.max` path respectively.
The UI translates codes into RU/EN/HE.

Example range-only loop (also accepted by the loop generation endpoints):

```json
{
  "start": { "latitude": 32.0853, "longitude": 34.7818 },
  "shape": "loop",
  "profile": "road",
  "targetDistanceRangeMeters": { "min": 18000, "max": 22000 },
  "targetDurationRangeSeconds": { "min": 3000, "max": 4200 }
}
```

Validated responses and interpretation drafts/intents preserve both bounds;
absent scalar/range fields are null. Explicit ranges match exact inclusive
bounds, without the legacy scalar 10% tolerance. See [ranking](route-candidates.md).

Malformed JSON, wrong JSON types, unknown properties, and missing/null body return
generic 400 ProblemDetails, not field-level semantic codes. Unsupported content
types return 415 ProblemDetails. Binding details and stack traces are not returned.

## Responsibilities

Contracts contains transport DTOs only, not domain objects or ASP.NET references.
Application validates/maps DTOs to domain values and returns a result, not an
HTTP response. Domain constructor validation remains the final invariant guard.
API maps successful values to response DTOs and errors to ValidationProblem.
The endpoint passes RequestAborted via its CancellationToken parameter; the
synchronous application validator checks cancellation before doing work.

No mediator, agent framework, validation package, database, or provider abstraction
is needed for this stage. Incomplete AI extraction remains separate future work.

Framework error handling follows the [ASP.NET Core documentation](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/error-handling-api?view=aspnetcore-10.0).
