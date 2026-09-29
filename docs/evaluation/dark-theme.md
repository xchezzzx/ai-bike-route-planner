# Dark Theme Verification

Date: 2026-09-29. Branch: `oleg/dark-theme`. Feature base: `a16ed9b`.
The user approved the bounded in-chat design before implementation.

Release update (2026-09-29): the user subsequently approved merging #15-#17 after
green CI with the documented route-quality limitations and later manual testing.
This supersedes the historical Draft/live-gate hold below. The integrated branch
also includes bounded adaptive loop calibration; its real-road effect is not
live-qualified. See [the qualification record](route-refinement-diagnostics-2026-09-29.md).

## Delivered Scope

- Light, dark and system modes through `next-themes` 0.4.6, with an explicit
  application storage key and native localized selector (EN/RU/HE).
- Shared semantic CSS colors for forms, results, notices, map controls and
  attribution. Existing route pattern colors and white dash backing remain.
- OpenFreeMap Liberty / Dark switching on the same MapLibre instance, preserving
  camera, candidate selection, inspected segment, draft input and downloaded GPX.
- Rebuild custom overlays on `style.load`; render readiness and the loading
  watchdog wait for `idle` with loaded resources. Full style replacement cancels
  obsolete initial requests without recreating the map or resetting its camera.
- Highlight explicit cycleways in Dark's `highway_path` layer as well as Liberty's
  existing path layers; never infer cycling access from pedestrian classification.
- No backend, Gemini prompt, route ranking, time/distance or deployment changes.

## Executed Checks

- Baseline: all 144 frontend tests passed.
- RED: browser selector absent, Dark cycleway unit case did not repaint, and the
  map lifecycle unit case observed no style swap. All passed after implementation.
- Initial browser run exposed a test locator issue: exact nested-label matching
  included the textarea contents after a React render. The accessible textbox
  name and value remained correct; role-based lookup fixed the test only.
- Independent review found three actionable issues. All received regressions:
  obsolete initial-style completion cancelled the active watchdog; parsed style
  JSON falsely marked stalled map data ready; dark input borders measured 2.67:1.
  Each failed before its fix and passed afterward. No review findings deferred.
- The 18 focused desktop/mobile browser scenarios passed: three locales, dark
  canvas, unchanged camera pixels after pan, exact GPX, selected segment/input
  preservation, system preference, persisted override, blocked storage, retry,
  rapid swaps, stalled initial style and stalled basemap source.
- Text samples meet 4.5:1; dark input border/fill meets 3:1. This is targeted
  automated contrast verification, not a claim of complete accessibility audit.
- Desktop EN and mobile HE screenshots were inspected for visible map, readable
  controls, RTL, non-overlap and attribution. Test screenshots are ignored output.
- Real OpenFreeMap Dark smoke at `http://127.0.0.1:61765/`: nonblank canvas,
  light/dark switching, no JavaScript errors. Generation endpoints were blocked;
  zero Gemini/ORS calls. Screenshot: ignored `artifacts/dark-theme-real-desktop.png`.
- Final frontend suite: 146 passed. Full desktop/mobile browser suite: 66 passed.
  Production build passed; `git diff --check` passed. Final-head CI is tracked
  on the dependent Draft PR and does not replace the live qualification gate.

## Dependencies and Limits

- Depends on Draft PR #16 and its PR #15 dependency. Green offline checks do not
  close the existing live Gemini gate. No merge before that gate is satisfied.
- Main-target dependent PR is required by the current CI branch filters; its
  inherited diff remains until predecessors merge. No workflow changes here.
- Theme changes reload map resources; network failure retains explicit retry.
- Browser storage denial permits switching for the current session but cannot
  persist a choice across reloads. System remains the default without a saved choice.
- Existing MapLibre bundle-size warning remains. No new service or API key.
- Distance/time ranges and other roadmap items remain separate work.

## References

- [next-themes](https://github.com/pacocoursey/next-themes)
- [OpenFreeMap styles](https://openfreemap.org/quick_start/)

The installed MapLibre implementation was inspected for `setStyle({ diff: false })`
request cancellation and `style.load` versus `idle` readiness semantics.
