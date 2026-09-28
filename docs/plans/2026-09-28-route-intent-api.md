# Route Intent API Implementation Plan

> Execution: superpowers:executing-plans, inline with tests first.

**Goal:** Implement stage 3 of the approved MVP roadmap: HTTP contracts,
application validation, domain mapping, and ProblemDetails errors.

**Architecture:** Minimal API -> application validator -> existing domain.
**Tech stack:** .NET 10, System.Text.Json, ASP.NET Core, xUnit v3.
**Spec:** ../api/route-intent-validation.md

## Constraints

- Start from domain commit 437c661 on oleg/route-intent-api.
- Do not merge PR #5 or add this stage to its branch.
- No new packages or external services; do not generate fake routes.
- Keep explicit units, nullable input numbers, stable validation codes.
- Use the established docs layout and existing checkout on a separate branch.

## Tasks

- [x] 1. Add real HTTP contract tests in Tests.Integration/RouteIntentEndpointTests.cs.
  Assert valid loop and point-to-point, defaults/units, field errors, missing
  body, malformed JSON/types, unsupported media, and Development/Production parity.
  Run tests and observe 404 failures before registering the endpoint.
- [x] 2. Add Contracts/RoutePlanning request/response DTOs and Application/RoutePlanning
  RouteIntentValidator.Validate(RouteIntentRequest, CancellationToken) returning
  RouteIntentValidationResult (domain Intent or field Errors). Implement the
  spec's token mapping, coordinate/target checks, and cross-field rules.
  Add direct unit coverage for non-finite numbers, duration bounds and cancellation.
- [x] 3. Register validator, ProblemDetails middleware, and the API endpoint in
  Api/RoutePlanning/RouteIntentEndpoints.cs. Verify HTTP tests turn green.
  Binding failures must remain 400, including Development; no global conversion
  of arbitrary exceptions to client errors.
- [x] 4. Update HTTP examples/README/roadmap. Run full Release tests/build and
  Docker smoke tests for successful and invalid requests. Review the final diff.

## Review focus

- Missing latitude/longitude must not silently become valid zero coordinates.
- Positive seconds must not overflow TimeSpan or lose precision.
- HTTP binding errors must not leak stack traces or change into 500 responses.
- An invalid destination must never be silently ignored for a loop.
- A cancelled request must not be converted into a validation error.

## Ledger

- Baseline: PR #5 is open; its Backend CI passed. Existing 65 tests pass locally.
- Ruling: a validation endpoint provides a truthful usable boundary before a
  routing provider exists; it returns neither route IDs nor invented geometry.
- Ruling: whole seconds avoid ambiguous duration strings and floating-point
  conversion overflow; later clients can convert minutes/hours explicitly.
- Task 1 red: all 27 new HTTP cases fail with 404 before implementation.
- Task 2 red: direct validator tests cannot compile before the new types exist.
  After implementation, 91 unit and 28 integration cases pass. Analyzer warnings
  identify omitted test cancellation tokens; pass the xUnit token at call sites.
- Task 3 green: successful JSON responses, semantic errors, and malformed-input
  ProblemDetails pass in the real in-memory ASP.NET pipeline.
- Final local suite: 91 unit and 31 integration cases pass; Release build has
  zero warnings/errors. Additional binding cases cover unknown nested fields,
  quoted numbers, and values exceeding Int64.
- Docker image cycling-routes-api:api-smoke builds successfully. A temporary
  non-root Production container returns 200 with normalized preferences, 400
  with field codes for invalid preferences, and generic 400 for malformed JSON.
  /health also returns Healthy. The localhost-only container was stopped and
  removed after verification. The previously documented HTTPS-port warning
  remains a deployment concern.
- Final no-build test run without limiting workers also passes all 122 cases.
  git diff --check reports no whitespace errors.
- Independent read-only review found no actionable defects. Suggested additional
  coverage: JSON double overflow and quoted non-finite tokens, the full binding
  failure matrix in both environments, and an incomplete destination on a loop.
  These are deferred coverage opportunities, not observed failures. The reviewer
  did not rerun tests; execution evidence above comes from the implementation run.
- Stage completed locally on oleg/route-intent-api. The user subsequently
  requested a commit and PR; all 122 tests passed again before committing.
  PR #5 was merged, so this API change is submitted against updated main.
