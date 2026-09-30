# Geographic Track Names

Every generated route exposes an additive string `name`:

- `POST /api/routes/generate`: `name` on the response.
- `POST /api/routes/candidates`: `candidates[].route.name`.
- `POST /api/routes/plan`: `search.candidates[].route.name`.

No route-name field is added to excluded candidates because they do not expose a
generated route. All existing geometry, metric, warning and segment fields remain.

The backend computes the name once. GPX 1.1 `trk/name` is exactly the same value.
The client must use `name + ".gpx"` as the download filename and may display `name`
without reconstructing it from a requested distance or location. There is no new
download endpoint, HTTP Content-Disposition header or separate filename field.
Frontend changes and integration are intentionally left to the parent task.

## Format

| Case | Example |
| --- | --- |
| Loop | `Tel-Aviv-loop-road-40` |
| A-B, different settlements | `Tel-Aviv-Haifa-road-105` |
| A-B, same settlement ID | `Tel-Aviv-road-40` |
| Unknown loop start | `Route-loop-road-40` |
| Both A-B endpoints unknown | `Route-road-40` |
| Only one A-B endpoint known | `Tel-Aviv-Unknown-road-40` or `Unknown-Haifa-road-40` |

Settlement lookup uses the **actual first and last geometry coordinates**, not the
requested coordinates or a prompt's place names. A loop uses its actual start.
Same-settlement A-B comparison uses GeoNames IDs; distinct IDs sharing a name
remain two tokens. Shape comes from the supported intent, not endpoint equality.
Profiles use `road`/`gravel`; this does not enable gravel routing in the API.

The suffix is actual provider-reported `distanceMeters / 1000`, rounded to the
nearest integer kilometre with `MidpointRounding.AwayFromZero` and invariant
formatting: 39,499.9 m => `39`, 39,500 m => `40`, 40,500 m => `41`. Short valid
routes can round to `0`. Target distance and duration are never used for the name.
Invalid, negative, non-finite or implausibly huge metrics (> 999,999,999 rounded
km) use `unknown`; provider validation remains responsible for rejecting invalid
routes. Naming does not relax that validation.

ASCII letters, digits and hyphens only; punctuation/separator runs become one
hyphen, leading/trailing separators are removed, decomposable accents are folded.
GeoNames ASCII names are used directly, without inventing a transliteration.
Each place token is at most 40 characters; the total name is bounded below 120
characters and retains the shape/profile/distance suffix. The extension is not
part of `name`. Names are descriptive, **not unique IDs**: nearby routes, rounded
distances or truncated labels can collide. A dataset refresh may change names.

## Coverage and Attribution

An embedded GeoNames `IL` settlement subset provides nearest-point lookup within
10 km (inclusive, with a one-micrometre floating-point boundary tolerance). Equal
distance ties prefer the lowest GeoNames ID. This is a naming heuristic, **not
containment or a jurisdiction determination**; outside coverage and at borders,
coasts or dense urban areas it can return no match or a nearby settlement.

There are no runtime network geocoder requests. The singleton loads a local
assembly resource once; each route requires at most two scans of 1224 points.
Missing or invalid local data becomes an empty lookup, so naming cannot fail a
provider request merely because the gazetteer is unavailable. Names are neutral
when there is no usable match. GeoNames attribution is appended to API attribution
and GPX `metadata/desc` when a geographic name is used, retaining routing-provider
attribution. Neutral names do not claim to use GeoNames data.

See [dataset provenance, license and reproduction instructions](../../src/backend/CyclingRoutes.Infrastructure/Naming/Data/README.md)
and [design, verification and integration handoff](../plans/2026-09-29-geographic-track-names.md).
