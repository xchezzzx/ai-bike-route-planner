# Endpoint Selection

The shape selector is explicit input in Prompt and Manual modes. Prompt requests
send optional `shape` with the selected start/destination. A missing extracted
shape uses that selection; a contradictory explicit prompt needs clarification.
Point-to-point requests do not need a distance/time target. Loop requests do.
No prompt rewriting or additional model call is involved.

## Interaction

- In A-B mode selecting a start advances the map picker to destination. Selecting
  an endpoint by search or map clears previous interpretation/routes and fences
  stale responses using the existing revision/cancellation mechanism.
  Clearing a point makes that missing endpoint the next map-click target;
  clearing start never silently replaces an existing destination.
- Initial ordinary map clicks select missing endpoints. Once all active endpoints
  exist, another ordinary click opens From here / To here instead of moving them.
  Right-click opens the same menu at any time. Mobile taps support initial picking
  and the menu after active endpoints exist; no long-press is required.
- From here replaces the start. To here replaces the destination and switches a
  loop to A-B. Route/segment hits keep their existing inspection behavior; explicit
  right-click actions remain available over a rendered route.
- Each endpoint has one settlement-search combobox. Typing clears its previous
  coordinate immediately; selecting a suggestion sets the source settlement point
  and centers the map. Free text alone is not a valid geographic selection.
- A selected map/location point is labelled Near the nearest settlement within
  10 km, or Point on map. This is not a municipality containment/address lookup.
  Coordinates are never replaced by the nearest settlement. Exact coordinate
  editing is retained as a collapsed advanced fallback.
- Keyboard arrows/Enter select suggestions and scroll the active option into
  view; Escape closes them. Map actions are
  keyboard-focusable with arrow navigation and Escape dismissal. Labels, themes,
  mobile layout and Hebrew RTL use the existing UI conventions.

## Local Data

`src/frontend/src/data/places.json` contains the same 1,224 public settlement IDs,
ASCII display names and coordinates as the existing embedded naming snapshot.
It adds source `name` and comma-separated `alternatenames`, not model-generated
translations. Search handles accents, spacing and punctuation; source Hebrew
and Cyrillic aliases are searchable where present. Display labels remain the
canonical ASCII source names. Alias coverage is not complete and these convenience
aliases are not language-tagged or independently verified addresses.

Source: [GeoNames country extract documentation](https://download.geonames.org/export/dump/readme.txt),
licensed CC BY 4.0, supplied without accuracy/completeness guarantees. The UI
credits GeoNames/CC BY 4.0. No runtime geocoder, provider quota, account, new package,
or transfer of search queries/map points to a geocoding service is introduced.

## Reproduction

The exporter reads the original pinned IL.zip retained in the naming worktree's
ignored cache. It rejects any archive with a different SHA-256 and verifies each
position/name against the committed naming snapshot. It changes no naming data.

```powershell
pwsh -NoProfile -File tools/export-place-catalog.ps1 -ArchivePath PATH_TO_PINNED_IL_ZIP
```

The frontend manifest records source/output SHA-256, record count and alias policy.
Tests check exact coordinate/ID parity, raw output hash, EN/Hebrew/Cyrillic search,
nearest-label behavior, combobox selection and stale-result invalidation. Browser
tests mock all provider traffic and exercise the real MapLibre canvas.

This is settlement search, not street/address/POI search, navigation, or proof of
road access. Selecting a settlement center still needs human map/route review.
