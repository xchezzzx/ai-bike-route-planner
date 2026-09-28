# Route Intent Implementation Plan

> Execution: superpowers:executing-plans, inline implementation. Independent review
> was requested but unavailable; final self-review and its limitations are recorded below.

**Goal:** Complete the previously approved issue #4 domain model and tests.

**Architecture:** Pure .NET domain values with constructor-enforced invariants.
Application orchestration and external adapters will consume this model later.

**Tech stack:** .NET 10, C#, xUnit v3 with VSTest.

**Spec:** ../domain/route-intent.md

## Constraints and decisions

- Preserve the user's in-progress migration to xUnit v3.
- Keep the existing VSTest CI commands: use the official mtp-off variant.
- No AI, database, routing API, UI, deployment, or new abstraction in this stage.
- Work in the existing checkout on oleg/4-route-intent-domain, retaining local edits.
- Initially kept uncommitted for inspection; the user subsequently authorized
  committing and opening a PR. Do not merge without a separate instruction.
- The user explicitly authorized implementation of the previously approved plan.

## Review focus

- NaN and infinity must never pass range validation.
- Object construction must not expose mutation that bypasses validation.
- Equal coordinates in different objects must reject a point-to-point trip.
- Duration-only and combined targets must both be accepted.
- Enum casts and runtime nulls must not create invalid intents.

## Tasks

- [x] 1. Restore a working test baseline after the xUnit package migration.
  Reproduce the runner failure; configure VSTest-compatible xUnit v3 and
  propagate cancellation in the health test. Verify the existing test runs.
- [x] 2. Add GeoCoordinateTests and DistanceTests, observe missing-type failure,
  then implement sealed records with validating constructors/get-only properties.
  Verify boundaries, non-finite values, positive meters, and value equality.
- [x] 3. Add RouteIntentTests for every invariant from the spec, observe failure,
  then implement enums and the RouteIntent constructor. Verify all unit tests.
- [x] 4. Update stale HTTP request and Docker restore inputs; document local
  commands, current review findings, and the next MVP stages.
- [x] 5. Run restore/build/test for the full solution and final review.
  Record results and the unavailable independent-review limitation below.

## Progress and review ledger

- Baseline: afc1985; local test-project changes replace xunit 2.9.3 with xunit.v3 4.0.0.
- Reproduced: build succeeds with two xUnit1051 warnings; restored solution
  dotnet test fails because Microsoft.Testing.Platform 2.3.3 rejects VSTest
  invocation on .NET 10.
- Ruling: use xunit.v3.mtp-off 4.0.0 rather than introduce MTP runner configuration
  throughout CI; this preserves the chosen xUnit major version and current CLI.
  Moving to MTP later will require deliberate runner/coverage configuration.
- Docker verification was initially unavailable because the Docker Desktop Linux
  engine was stopped; the follow-up verification below resolves this limitation.
- Task 1 complete: integration test passes (1/1) with xunit.v3.mtp-off and cancellation tokens.
- Task 2 red: unit build fails with CS0234 because the domain types do not yet exist.
- Task 2 complete: 29/29 unit cases pass after implementing immutable value objects.
- Task 3 red: missing RouteShape, CyclingProfile, ElevationPreference and RouteIntent
  prevent compilation before implementation. Green: 64 unit cases and 1 integration
  case pass in the full solution with --maxcpucount:1.
- Task 4 complete: corrected HTTP sample, copied all project references into the
  Docker restore layer, added README commands and staged roadmap/review documents.
- Release solution build: 0 warnings, 0 errors. git diff --check: no whitespace errors.
- Task 5 complete: the exact CI test command also passes locally:
  `dotnet test src/backend/CyclingRoutes.slnx --configuration Release --no-build --verbosity normal`.
  Result: 64 unit cases and 1 integration case, 0 warnings and 0 errors.
  A restricted-environment invocation failed without actionable diagnostics;
  the approved rerun outside that restriction succeeded without code changes.
- Independent reviewer could not run because of a service usage limit. Final
  self-review covered domain invariants, tests, runner configuration, Docker
  restore inputs, and documentation; no further actionable issues were found.
  This does not constitute independent review or a GitHub Actions run.
- Docker follow-up after the user started the engine: image build succeeds
  (0 build warnings/errors) with
  `docker build -f src/backend/CyclingRoutes.Api/Dockerfile -t cycling-routes-api:local-smoke src/backend`.
  A temporary Production container runs as UID 1654 and serves /health with
  HTTP 200, text/plain, and exactly Healthy. The host port was restricted to
  127.0.0.1; the container was stopped and automatically removed afterward.
  The local image remains. Runtime logs warn that the HTTPS redirect port is
  undefined; TLS termination and proxy configuration remain deployment work.
- The user requested a commit and PR on oleg/4-route-intent-domain after the
  Docker follow-up. A fresh pre-commit run passes all 65 tests.
