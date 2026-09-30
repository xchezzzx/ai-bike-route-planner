# Offline Road-loop Evaluation

This utility reads local files only. It uses the production geometry metrics,
road-v1 assessor, ORS adapter and distance-range selector. No keys, provider
requests, database, service startup or additional packages are required.
The tool is not part of the deployed API.

From the repository root:

```powershell
dotnet run --project src/backend/CyclingRoutes.Evaluation -- gpx "D:/path/reference.gpx" artifacts/evaluation/reference.json
dotnet run --project src/backend/CyclingRoutes.Evaluation -- ors artifacts/saved-response.json artifacts/evaluation/candidate.json 35000 45000
```

The ORS input is the **raw GeoJSON provider response**, not an API candidate
response. The last two arguments are inclusive distance bounds in metres.
The raw response must have metre-based summaries. An explicit
`metadata.query.units` other than `m` is rejected; when metadata is absent,
metre units are an input requirement rather than independently verified.
To replay a scalar 40 km request with its existing +/-10% tolerance, use
36000 and 44000. ORS duration is reported, not used as an eligibility target by
this distance-only command. Do not use it to qualify duration/combined cases.

Reports contain SHA-256 of original bytes, source kind, point count, geometry
length, endpoint gap, provider summaries when available, road-v1 quality and
distance/surface exclusion reasons. Policy warnings for retained candidates are
included separately from reasons. Coordinates, names, paths and input timestamps
are omitted. Inputs and existing reports are never overwritten.
Reports are written to a temporary sibling and only published after a complete
write; a failed write cannot become final evidence.
Exit 0 means analysis succeeded, **not** route acceptance; inspect
`policyEligible` and `reasons`. Invalid input returns nonzero without a report.

Supported GPX: 1.0/1.1, one track and one segment, finite coordinates and optional
finite elevation. DTDs/entities, multiple segments/tracks, route-only files and
degenerate geometry are rejected rather than joined or guessed. Input limit is
16 MiB, with at most 200,000 GPX points. GPX has no provider distance/duration or
surface evidence; eligibility stays null and all surfaces remain unknown.

Exact repeated edges and a shared departure/return stem reuse road-v1 semantics.
They miss approximately retraced roads, alternate carriageways and turn
complexity. Endpoint gap is a geometric diagnostic, not a safe connection.
Provider distance and geometry length are separate measurements.

## Public-city Control Matrix

[road-loop-control-v1.json](road-loop-control-v1.json) contains eight **unrun**
cases based on the four public-city starts in the existing refinement corpus.
It covers distance scalar/range, duration scalar/range and combined ranges.
These coordinates are reproducible controls, not verified safe cycling starts.
The test suite validates each request through the API without provider calls.
The older `evaluate-refinement.ps1` corpus is unchanged; this matrix is not
compatible with that runner and must not be passed to it.

## Next Comparison Protocol

1. Agree a new live-call budget and select cases before calling providers.
   Gemini is unnecessary for initial construction comparison.
2. Compare current changing-seed calibration against an isolated experimental
   fixed-seed strategy, with identical intent, starting length, profile,
   three-call maximum, deadline and surface policy. No production switch yet.
3. Save each raw response and request seed/length locally, with strategy and
   attempt identity. Never replay a response at a different length and claim
   that it simulates provider construction.
4. Record every attempted call, failures, duplicate geometry, retained count,
   target fit, surface uncertainty and retracing. A no-match or failed arm is
   not a passing quality result. Preserve original ranges.
5. Inspect resulting GPX manually alongside the reference. Record manoeuvre
   and road-choice feedback separately from numeric eligibility.
6. Select a production change only after this evidence. If both strategies
   remain poor, proceed to the approved controlled-waypoint prototype.

Eight cases and two full three-call arms would require up to 48 ORS calls.
This is a ceiling for planning, **not authorization**. A smaller subset is
appropriate for the first comparison. No live calls were made by this step.
