# Road-network waypoint construction proposal

**Status:** proposal for owner review, not approved for implementation. No exporter,
new dependency, provider interface or routing mode is implemented by this document.

## Intent and evidence

Generate logical connected road rings rather than add small loops/returns merely
to meet distance. Preserve the owner's exact start, original preferences, known
versus unknown road/elevation evidence, and the existing bounded search. The
[2026-10-14 experiments](../../evaluation/graphhopper-construction-2026-10-14.md)
show limited effects from the tested slope and turn weights. They do not rule out
other native profiles, but do not justify promoting either tested model.

## Options

| Option | Benefit | Cost/risk |
| --- | --- | --- |
| Continue tuning native round trips | Smallest code change, no new data module | More corpus/experiments needed; tested settings barely change the symptom |
| Hand-curated regional anchor templates | Fast in familiar regions | Manual coverage/maintenance; too close to a ready-route catalog for the original product intent |
| Local road-network anchor catalog + complete GraphHopper paths | Reproducible network-based construction, no extra online waypoint queries | Offline export and bounded catalog/selection module; must prove quality |

**Recommended proposal:** third option, initially opt-in for local road loops.
Keep .NET modular monolith, React and GraphHopper. No database, cloud, mobile app,
new AI agent framework or paid service is required for this phase.

## Boundaries

1. An offline, read-only exporter derives candidate anchor positions from the same
   pinned GraphHopper graph/profile used for routing, not from private GPXs or LLM
   coordinates. A small Java CLI beside engine infrastructure is preferred over
   a routing-engine fork or a new HTTP endpoint. Its API feasibility must be proven
   before implementation; use existing pinned GraphHopper/JDK dependencies.
2. Export must use the profile's actual accessible finite-weight edges, not
   reimplement bicycle access from raw OSM tags in C#. Preserve direction, road
   class, explicit/unknown surface and numeric/unknown elevation. Connectivity
   labels are selection hints, not proof that a legal closed path exists.
3. Infrastructure streams a versioned local catalog and builds a bounded spatial
   index. Use standard JSON parsing and geometry helpers, not a C# routing engine.
   The catalog contains public derived graph data only; user prompts, starts,
   route histories and private GPXs are not catalog inputs.
4. Application selects reproducible candidate anchors from accessible road-network
   positions, guided by requested length, spread and connected corridors. Avoid
   treating node height as complete route ascent. Minimize must preserve unknown
   states; any construction-specific use needs its own qualification.
5. GraphHopper remains authoritative: send ordered points such as start, anchor A,
   anchor B, start in **one** normal route request per attempt. Return the engine's
   complete connected geometry. Never cut/reverse/stitch arbitrary GPX fragments.

Likely write boundaries after approval: `infra/graphhopper/tools/` exporter;
`CyclingRoutes.Infrastructure/Routing/` catalog and multi-point adapter;
`CyclingRoutes.Application/Routing/` waypoint-source/selection contract; existing
candidate service, tests and evaluation harness. Add abstractions only at these
real ownership boundaries; no new solution projects are needed initially.

## Catalog contract

Metadata includes schema, graph identity, exact profile/model hashes, source PBF
hash and generation version. Catalog/routing identity mismatch must be explicit;
never silently use stale positions. Use a bounded streaming format, tentatively
NDJSON with a 64 MiB uncompressed/250,000-entry cap, measured before finalization.
Exporter over-cap fails rather than silently truncating coverage. Select junction
and representative accessible-edge anchors rather than dumping every graph node.

Prove read-only export against the pinned 11.1 storage API and profile weighting,
including turn restrictions and necessary stems. Do not mutate the working cache
or acquire a writable second graph owner. Preserve OSM attribution; review ODbL
distribution obligations before publishing a derived catalog. Data stays ignored
and local during the prototype.

## Runtime and budgets

- Default construction remains native. A future explicit local configuration mode
  opts into waypoints only with a compatible catalog; missing/mismatched catalog
  returns an honest configuration error, not an undisclosed provider switch.
- Exact requested start remains the route start; normal provider road snapping is
  visible, not an arbitrary move to a convenient settlement or anchor.
- Reuse current initial-length/calibration and seeds 1-3. Maximum **three HTTP
  routing calls total**, sequential, with the existing 45-second application
  deadline and 15-second client deadline. No extra `/nearest`, `/isochrone`, ORS
  or Gemini calls are hidden outside that count.
- Before implementation, verify whether engine `timeout_ms`/visited-node bounds
  apply per request or per leg. If per leg, divide a fixed total budget between
  legs or use an engine-supported total deadline. A multi-point HTTP request
  must not conceal multiplied server work after client cancellation.
- Provider input should carry typed original preferences through normal/advisor
  orchestration, with bounded supported behavior. No public preference overwrite,
  fabricated speed/duration, hard ascent cap or unqualified SeekClimbs promise.
- Existing closure/deduplication, target/surface/waytype gates, missing-data
  warnings, eligible-only scores and exact selected GPX remain authoritative.

## Validation sequence after approval

1. Feasibility spike: read-only exporter/profile parity and engine multi-leg total
   limits. Stop if either prerequisite fails; record the alternative explicitly.
2. Implement bounded exporter/catalog validation with synthetic graph fixtures;
   test unknown values, malformed/oversized catalog and version mismatch.
3. Implement deterministic local anchor selection and a typed provider boundary;
   test start preservation, reproducible seeds and no hidden network requests.
4. Add multi-point payload and orchestration tests before product code. Prove
   three-call/45-second cancellation behavior, closure, no-route outcomes, existing
   A-B/native compatibility and selected GPX identity.
5. Compare native/waypoint arms on the frozen public corpus and private replay-only
   controls. Record all failures/duplicates/exclusions, not only attractive paths.
   No new routing-call experiment budget is implied by approving this proposal.
6. Agree acceptance thresholds and obtain owner judgments on complete retained
   routes in familiar regions. Enable by default only after evidence and green CI.

## Explicitly open

Owner architecture approval, exporter feasibility, measured catalog size, total
per-leg computation limits and route-quality thresholds. The historical 1400+m
request is still unrecovered. Real roads, device GPX import and HTTPS phone
geolocation remain separate manual checks; cloud and new Gemini/ORS qualification
remain deferred. No implementation plan is claimed approved by this proposal.
