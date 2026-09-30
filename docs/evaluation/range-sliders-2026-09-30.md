# Manual target range sliders

The manual distance and duration range controls now use two Radix Slider thumbs
instead of minimum/maximum text fields. Scalar Target mode keeps its text input.
Both bounds are displayed with localized units. Sliders fill the control width
and use the existing light/dark palette; Hebrew reverses the slider direction.

Selecting an empty Range initializes an explicit 35-45 km or 60-120 minute
draft. Clearing a range removes both bounds from the request and disables its
thumbs; the plus action enables it again. Existing valid range drafts survive
mode changes. Inactive scalar drafts are not submitted in Range mode.

The normal scale is 1-100 km or 1-480 minutes with a one-unit keyboard step.
Existing decimal or extended bounds are preserved and extend the initial scale
when necessary. The scale stays stable while dragging. API constraints and
route-search budgets are unchanged; slider edits use the existing cancellation
and result-invalidation flow and never generate a route automatically.

## Local verification

- Seven focused component cases failed before implementation because sliders
  and the optional-range actions were absent. All 199 frontend tests pass after
  implementation, including keyboard edits, RTL direction, preserved drafts,
  exact API units, cleared duration, extended bounds and result invalidation.
- The production build passes. The existing MapLibre chunk-size warning remains.
- Six desktop/mobile keyboard range cases pass across EN/RU/HE. Six additional
  pointer cases pass across those languages in dark mode, exercising mouse
  dragging on desktop and touchscreen taps on mobile. They verify serialized
  bounds, optional duration and no horizontal overflow.
- The initial pointer test run failed at an incorrect theme selector before
  reaching the slider. The corrected semantic combobox selector passes all six
  cases without a timeout increase or product workaround.
- Screenshots inspected: desktop RU and mobile HE range layouts; dark pointer
  layouts are captured in the corresponding Playwright result directories.
- The existing local preview at http://127.0.0.1:5173/ displays the sliders and
  the optional duration state. No live Gemini/ORS requests were made.

Full pull-request CI remains the merge gate. Road-loop reliability and improved
empty-result wording are recorded separately in the roadmap backlog; this UI
change does not claim improved route quality. Cloud deployment remains deferred.
