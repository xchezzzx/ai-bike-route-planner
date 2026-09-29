# Current location as start

Autonomous implementation authorized on 2026-09-29. This fulfills the approved
roadmap addition, without background tracking or new provider calls.

- Call getCurrentPosition only after the location icon is activated.
- Request high accuracy with a 10-second timeout and maximumAge zero.
- Accept finite coordinates and finite nonnegative reported accuracy. At most
  100 metres applies directly; a coarser fix requires explicit confirmation.
- Apply only to start, using the existing planner invalidation path. Show the
  reported accuracy and center the map once for each accepted fix, including
  repeated fixes at the same coordinate. Preserve existing results on failure.
- Cancel logically on any input edit, planning action, later location request
  or unmount. Browser callbacks cannot be physically aborted, so fence them by
  request identity and planner revision. No late callback may overwrite edits.
- Localize denied/unavailable/timeout/unsupported/insecure-context errors in
  EN/RU/HE. Keep map/manual inputs available. Distinguish reset-map and locate.
- No location storage, telemetry or reverse lookup. Map centering may fetch
  tiles. Production needs HTTPS and Permissions-Policy geolocation=(self).

Verification: hook race/error tests, App result-invalidation/start-only tests,
desktop/mobile geolocation mocks and visible centered marker. Real accuracy,
browser permission UI and embedded-browser policy require user-device checks.

Reference: https://developer.mozilla.org/en-US/docs/Web/API/Geolocation/getCurrentPosition

## Progress

- [x] Tests and implementation
- [x] Local verification and independent review
- [ ] PR, green CI, squash merge and main verification

160 frontend tests pass. Production build passes (existing MapLibre chunk-size
warning unchanged). The full 78-case browser suite passed; after the review fix,
the additional two desktop/mobile deferred-focus regressions passed as well.
Independent review found a pending map-focus race. Its component regression was
observed failing before clearing focus on newer planning/refinement actions and
passing afterward. Browser checks use delayed map data and require route-sized
pixel coverage. Desktop RU and mobile HE layout screenshots were inspected.
No live routing/AI requests or real-device location checks were performed.
