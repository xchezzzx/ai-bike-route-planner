# Track Segment Display

Status: conversational design approved; written specification awaiting review.
Date: 2026-09-29.

## Intent and Scope

Help a rider inspect the surface and road type of a generated route before
exporting it. The user approved styling only the generated track, not the
background road network. Preserve the current background cycleway highlighting.

The selected route has a Surface / Road type segmented control, defaulting to
Surface. Its color continues to identify the candidate. Other candidates remain
muted continuous lines. Include a compact legend and selectable segment details
in English, Russian and Hebrew, including RTL and mobile layouts.

This work does not change route generation, candidate selection, geometry, GPX,
Gemini contracts, provider budgets or access policy. It makes no additional ORS,
Gemini, geocoding or map-matching calls. Theme support and distance/time ranges
remain separate requested follow-ups, not part of this specification.

## Existing Flow

- `OpenRouteServiceProvider` already requests `surface` and `waytype` extras.
- `OpenRouteServiceEvidenceParser` validates index ranges but currently retains
  only category totals in `RoadEvidence`.
- `RoutedPath` carries optional evidence into quality assessment and responses.
- `GeneratedRouteResponse` contains geometry but no segment metadata.
- `RouteCandidateResponseMapper` serves candidate responses; the direct route
  endpoint also constructs `GeneratedRouteResponse` and must remain consistent.
- `RouteMap.tsx` draws whole-route GeoJSON lines using MapLibre. Clicks currently
  select a candidate or set a route endpoint.

## Chosen Approach

Retain provider evidence as geometry-indexed intervals in the backend and expose
normalized categories to the client. Derive render-only GeoJSON on the frontend.
Keep provider numeric codes inside the infrastructure boundary.

Inferring surface from the background map is rejected: that map does not expose
the same evidence or guarantee matching geometry. A new OSM enrichment service
is unnecessary for this slice and would introduce network and access-data work.

## Segment Contract

Add optional `segments` to each generated route response. Each entry has:

- `fromPointIndex`: zero-based inclusive index.
- `toPointIndex`: zero-based endpoint index, greater than `fromPointIndex`.
- `surface`: `asphalt`, `paved`, `unpaved`, `other`, or `unknown`.
- `wayType`: `stateRoad`, `road`, `street`, `path`, `track`, `cycleway`,
  `footway`, `steps`, `ferry`, `construction`, or `unknown`.

The interval describes edges [fromPointIndex, toPointIndex), drawn using points
from fromPointIndex through toPointIndex inclusive. Its last index is less than
the geometry point count. Adjacent intervals share an endpoint, never an edge.

Normalize at the union of surface and way-type boundaries so each edge has one
pair of attributes. Fill uncovered edges with unknown for the missing family.
Coalesce adjacent intervals only when both normalized attributes agree. Bound
the resulting interval count by the geometry edge count. Preserve cancellation
checks and existing response/geometry limits; never duplicate coordinates in the
HTTP metadata.

New backend responses with geometry provide a complete partition, including a
single unknown interval if no evidence is available. Old responses with absent,
null or empty metadata render as unknown, not as confirmed paved surface.

Retain the distinction between explicit asphalt and generic paved/concrete.
Do not relabel generic paved evidence as asphalt. Keep existing quality totals
and road-v1 classifications unchanged; the more specific display categories
must aggregate back to the same current quality buckets.

No access-permission field is introduced. A footway is labeled as mapped
pedestrian infrastructure, never as confirmed legal cycling access. Provider
routing through it is not proof of permission or safety.

## Validation and Failure Behavior

Backend malformed provider ranges retain the existing invalid-response behavior:
reject negative, non-integral, reversed, overlapping and out-of-bounds ranges.
Missing evidence and unsupported nonnegative codes remain unknown. Do not turn
provider validation failures into seemingly valid all-asphalt routes.

The frontend validates optional metadata before using it. A present nonempty
list must form a complete ordered partition with recognized categories and valid
integer indices. Invalid metadata does not prevent an otherwise valid route or
GPX from being used: discard the entire annotation list, render unknown and show
a localized segment-data-unavailable status. Do not infer partial annotations
from invalid input. Existing geometry validation remains authoritative.

## Visual Encoding

Surface mode:

- Asphalt: continuous candidate-colored line.
- Paved, not specifically asphalt: continuous line with a fine contrasting
  center stripe; a separate legend label avoids claiming confirmed asphalt.
- Unpaved: short candidate-colored dashes on a white inner backing.
- Other known surface: alternating long and short colored dashes.
- Unknown: fine dotted candidate-colored line, explicitly labeled unknown.

Road-type mode:

- State road, road and street: continuous line; details show the exact category.
- Cycleway: long colored dashes.
- Footway: short paired dashes, distinct from cycleways.
- Path and track: short dashes; details distinguish the two.
- Steps, ferry and construction: distinct caution treatment and explicit label
  when present; ordinary road search policy remains unchanged.
- Unknown: dotted line.

Use a contrast outline beneath the selected route, with an inner white backing
where gaps are intended to be white. Keep line widths screen-relative and verify
that patterns are recognizable across supported zooms. Color alone must not
communicate the category. Share the pattern definitions between map and legend.

## Interaction and Lifecycle

Only the selected route receives segment styling and detail interaction. Use a
continuous hit area so tapping a dash gap still selects the segment rather than
accidentally moving the start/destination. Prioritize selected segment hits over
candidate hits; candidate hits still select that route. Empty-map clicks retain
the existing endpoint-picking behavior.

Show surface and road type together in the selected segment detail region. Offer
an accessible segment list/disclosure for keyboard users, not only canvas taps.
Use normal React text rendering for labels, never provider HTML.

Mode changes update styles without refitting or recreating the map, changing
selection, clearing endpoints or issuing route requests. Selection changes clear
stale segment details. New results, empty results and map retries reset stale
details and restore the correct layers. Keep the chosen display mode while the
component remains mounted; no persistence dependency is needed.

Do not change GPX contents or encode display-only patterns into the exported file.
No new map library or UI framework is required.

## Acceptance and Verification

All automated checks use synthetic/saved provider fixtures, not live quotas.

1. Parser tests cover disjoint boundaries, single-edge ranges, coalescing, missing
   families, unknown codes, duplicate geometry points and malformed ranges.
2. Aggregated distances and road-v1 assessments stay unchanged on saved fixtures.
3. Direct generation, deterministic candidates and advised candidates expose the
   same segment contract. Coordinates, ordering and GPX remain unchanged.
4. Frontend tests cover absent/invalid metadata, every display category, both
   modes, empty results, candidate switching and stale detail cleanup.
5. Browser tests cover EN/RU/HE, desktop/mobile, keyboard access, tapping on a
   dash gap and preserving endpoint picking on empty map areas.
6. Screenshots and canvas-pixel checks verify nonblank maps, actual pattern
   rendering, muted alternatives, legible legends and no control overlaps.
7. Mode changes preserve camera and selected route, without extra API requests;
   repeated toggles and map retries do not accumulate layers or event handlers.
8. Full affected backend/frontend test suites and builds pass before PR claims.

## Delivery Boundary

This specification records the approved feature intent, not completed code.
After written-spec approval, produce a concrete implementation plan and obtain
plan approval before implementation. Keep commits scoped to segment evidence,
API mapping, rendering and verification. This work does not satisfy or waive
PR #15's outstanding Gemini qualification and route-comparison merge gates.
