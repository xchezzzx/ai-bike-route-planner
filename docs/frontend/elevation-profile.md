# Elevation profile and route selector

## Layout and interaction

- Desktop: map on the left, request settings on the right, route choices beneath settings.
- Each route choice shows distance, ascent, and provider time with icons and localized accessible names/tooltips.
- Mobile: settings and choices precede the map; the elevation profile follows the map directly, before segment controls.
- While results are present, the map has stable dimensions independent of chart availability and segment legend height; switching legend modes cannot shift the track's camera framing.
- Hovering or tapping the chart selects the nearest original geometry point by cumulative distance. Keyboard/touch inspection is also available through a native range control.
- A separate amber marker highlights that exact point without moving or refitting the map. Switching routes, clearing inputs, or invalidating results clears inspection.
- English, Russian, Hebrew, light, dark, and system theme remain supported. The distance axis and inspection slider run left to right in every locale; Hebrew labels retain RTL.

## Data and limits

The profile uses `GeneratedRoute.geometry[].elevationMeters` already supplied by the routing provider. No additional elevation service, AI call, or backend contract is introduced. Chart.js 4.5.1 renders straight lines with `spanGaps: false`.

The horizontal axis is cumulative Haversine distance along the original geometry, including repeated and return sections. It can differ from the provider's distance shown in the route selector. Min/max heights come from all known geometry heights; ascent/descent and provider time remain provider totals, not recomputed from a simplified chart.

Null and nonfinite heights are missing, not zero. Finite zero and negative heights are valid. Missing spans are never joined or interpolated. An isolated known height appears as a dot. If all heights are missing, a localized unavailable state replaces the chart and inspector; route selection and GPX export remain usable. Invalid coordinates discard the profile rather than inventing distances.

Large tracks are sampled to a soft budget of 1,200 plotted points, preserving endpoints, global extrema, each gap boundary, and bucket minima/maxima. Mandatory gap boundaries can exceed the budget on a track with many alternating gaps. Original indices and the full geometry remain intact: inspection uses a binary search over full cumulative distances, not sampled indices. Equal-distance ties choose the earliest original point.

Chart updates ignore replayed pointer events so updating a selected dot cannot overwrite a keyboard selection with an old hover. Theme/locale/route changes dispose the prior chart and its callbacks. Missing inspected heights have an unknown-height readout and map marker, without an invented chart dot.

Control inspection owns its selection through layout-induced canvas leave and
stationary reentry. A leave event cannot select an in-chart point even when the
canvas moves before Chart.js processes it. Queued pointer input updates the
physical-position baseline without replacing a newer control selection; older
events cannot move that baseline backwards. Deliberate movement, click or touch
resumes pointer inspection, and leaving the chart then clears pointer inspection.

This feature does not estimate slopes, smooth elevations, color slopes, navigate during a ride, or improve route quality. It does not change provider budgets or deployment decisions.

## Verification

- Unit tests: cumulative distance, invalid input, zero/negative heights, gap/extrema preservation, deterministic nearest-point ties, and 50,000-point tracks.
- Component tests: three compact metrics, sidebar placement, inspection invalidation, marker lifecycle without camera changes, no-height state, chart disposal, and pointer replay protection.
- Offline Playwright: real Chart.js/MapLibre canvases, chart pixel checks, keyboard/hover/touch inspection, three languages, light/dark screenshots, desktop/mobile layout, GPX fallback, and a 50,000-point route. External provider requests are intercepted; no live ORS/Gemini calls.

Run `npm test`, `npm run build`, and `npm run test:e2e` from `src/frontend`.
