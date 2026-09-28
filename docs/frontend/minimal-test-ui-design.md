# Minimal route testing UI

## Intent and authorization

The user requests autonomous implementation, logical commits, PR creation, and
squash merge only after successful CI. They also request a minimal frontend for
testing. This design chooses conservative details within that authorization;
it does not claim a separate user review of this new document.

The deliverable is a local browser application, not a landing page. It exposes
the implemented backend workflows so the user can inspect AI preferences and
real road routes. The experimental Gemini qualification remains incomplete.

## Decisions

- React + TypeScript + Vite, npm lockfile, Node 24. No state framework or router
  for one screen. Alternatives considered: Swagger alone lacks map inspection;
  Next.js adds an unnecessary second backend.
- MapLibre GL with OpenFreeMap Liberty vector tiles, Israel/Tel Aviv initial
  view. Preserve map and route-provider attribution. No map API key.
- Local Vite proxy `/api` and `/health` to the loopback ASP.NET API. Secrets
  stay in .NET User Secrets. Bind both servers to loopback. Do not add permissive
  CORS, a public deployment, accounts, payments, storage or automatic retries.
- Use current stable npm versions and lock installed dependencies. Package
  scripts: dev, build (typecheck plus Vite), test (Vitest), test:e2e (Playwright).
- Sources: https://vite.dev/guide/ ; https://openfreemap.org/quick_start/ ;
  https://playwright.dev/docs/ci . Availability of external tiles is not an SLA.

## Screen and interaction

Compact header with Cycling Routes, language selector (EN/RU/HE) and API health.
An unframed controls column beside a large map; stacked layout on narrow screens.
White/neutral surfaces, green primary actions, contrasting route alternatives.
No marketing hero, decorative cards or introductory feature explanations.
Use lucide-react icons with accessible names/tooltips for map/reset/download tools.

Select start or destination mode, then click the map. Explicit numeric latitude
and longitude fields provide an accessible fallback and precision. No geocoding
or geolocation permission request. Do not assume the user's home coordinates.

Two explicit input modes: prompt and manual. Prompt mode sends locale, text and
selected coordinates to `/api/route-intents/interpret` only when requested.
Render draft, assumptions, clarifications and limitations. A separate explicit
Generate action is enabled only for a ready validated intent; never generate
silently after interpretation. Unsupported or ambiguous outputs remain blocked.

Manual mode accepts shape, road/gravel, distance in km, duration in minutes and
elevation. Defaults are visible (loop, road, balanced). Blank targets remain
absent; reject nonfinite/zero/negative quantities and incomplete coordinates.
POST to `/api/route-intents/validate` before generation, use returned canonical
intent. Show validation errors. Gravel and unsupported A-B elevation remain
visible unsupported choices, not silently converted. Changing mode, coordinates,
locale, prompt or any preference aborts pending work and clears stale intent,
routes and errors. Late responses must never restore stale results.

For loop generation call `/api/routes/candidates`; for A-B call
`/api/routes/generate`. Show ranked results, selection, distance, provider time,
ascent (unknown is not zero), target match, warnings, assumptions and attribution.
Draw actual returned geometry and fit map bounds on result selection. Download
the selected returned GPX string as UTF-8 via Blob, with no additional provider
request. Never invent geometry, silently fall back to demo data, or label routes
safe. Keep a concise road/access verification warning visible with results.

## Reliability and localization

One in-flight operation; visible pending state and cancel control. Abort on
unmount/change. Timeouts bounded above the API candidate deadline (60 seconds).
Manual retries only. Map tile/WebGL failure must not break numeric input or
results/download. External map errors have a visible status. API failures show
localized safe messages for known codes, not raw provider content/HTML.

All application labels, errors, warning/assumption codes and empty states have
EN/RU/HE translations. Backend clarification messages use requested locale.
Hebrew sets document lang and dir=rtl; coordinates/numbers use ltr direction.
Use semantic labels, keyboard focus, live status, responsive dimensions and
no horizontal page overflow at 390px. Prompts/routes are not persisted.

## Verification and delivery

Unit/component tests cover serialization, unit conversion, empty vs zero,
manual validation, blocked unsupported intent, API errors, cancellation/stale
results, GPX selection and language/RTL. Browser tests cover prompt -> confirm
-> candidates -> GPX, manual A-B, provider failure and mobile Hebrew. Stub API
calls in CI; no provider credentials or paid requests. Map may use a local
deterministic style fixture in CI; additionally verify real tiles locally.

Add Frontend CI for all main pull requests and pushes: npm ci, tests, build,
Chromium installation and Playwright. Keep Backend CI always triggered. Before
merge inspect all jobs, not only the currently required backend check. Upload
test artifacts on failures. Cloud CD is not yet configured; do not call a
successful build a production deployment.

Serve locally after successful verification and provide the URL. Merge only
after independent review, all applicable PR checks, and no unresolved material
findings. Retain provider availability and injection-quality issues explicitly.
