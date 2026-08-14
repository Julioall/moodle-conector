# Design QA

## Reference

- Source visual truth: `C:/Users/Julio/Desktop/Repositorios/claris/src` and `C:/Users/Julio/Desktop/Repositorios/claris/docs/CLARIS.md`.
- Reference implementation inspected directly in the local Claris repository.

## Comparison

- Implementation screenshot: unavailable; no browser-rendered capture exists.
- Viewport: unavailable.
- State: unavailable.
- Source pixel dimensions: unavailable; the reference is source code rather than an exported image.
- Implementation pixel dimensions: unavailable.
- CSS viewport and device density: unavailable.
- Density normalization: not applicable; no screenshots were captured.
- Full-view comparison evidence: unavailable because the integrated browser had no available browser instance.
- Focused-region comparison evidence: unavailable for the same reason.
- Browser retry in this turn: runtime initialized, browser discovery returned an empty list (`[]`); no valid rendered screenshot could be captured.
- Primary interactions tested in a browser: none.
- Console errors checked in a browser: unavailable.

## Findings and fixes applied

- Reused the Claris shell structure, logo treatment, sidebar hierarchy, footer, auth surface, spacing tokens, colors and typography already present in the Moodle Connector design system.
- Removed the extra visual drift caused by persistent page eyebrow labels and bordered page headers.
- Aligned the top bar geometry while retaining only Moodle connection scope, status and refresh actions backed by the current connector.
- Reintroduced the Claris top-bar rhythm — last synchronization, offline state, notifications and edit-mode affordance — while keeping Moodle selection and refresh scoped to the existing connector. Notification activity is loaded lazily through a bounded `activityOnly` dashboard request.
- Added the Claris dashboard week selector (`Semana atual` / `Última semana`) to the connector UI and API contract. The backend returns the selected São Paulo week bounds while leaving unsupported Moodle historical metrics explicit instead of fabricating snapshots.
- Reworked Tasks into a Claris-like Kanban/list experience with task cards, status movement, filtering and a detail drawer backed by the current task gateway.
- Reworked Agenda into a Claris-like calendar/list experience combining current agenda events and task deadlines, with month navigation, adjacent-month cells, selected-day details and event creation backed by the current gateways.
- Added event editing end to end: the agenda UI reuses the source-like edit affordance and the connector now exposes a permission- and antiforgery-protected `PATCH /api/agenda/{id}` using the existing local calendar store.
- Reused one Claris-like task detail drawer across Tasks and Agenda, including status changes through the current task gateway and cache invalidation after mutation.
- Aligned the task list to reuse the same card treatment as the Claris reference and added the source-like per-column creation controls to Kanban.
- Aligned dashboard course overview and the five-indicator row to the source list/card composition using the already-loaded course query, avoiding additional per-course requests.
- Extended the current ASP.NET dashboard contract with bounded local event/task counts and course-scoped indicators; unsupported correction and weekly-delta metrics remain explicitly nullable rather than being fabricated.
- Added real course-scoped awaiting-grading indicators from the existing assignment submissions gateway, bounded to 20 assignments for dashboard latency, and computed risk/attention from available access and submission evidence with an explicit warning about the source of the calculation.
- Reworked the course detail page to match the Claris composition: header with Moodle context, four indicator cards, overview/progress surface, tab counts, paginated students and paginated activities, all using existing `/api/courses` gateways.
- Reworked Configurações to expose the Claris-first Geral/Mensagens tabs, added local Aparência preferences through the existing `next-themes` dependency, and grouped connector-specific Teams/Groups/Security actions under Acesso. Administrative queries are now lazy and only run when their tab is selected.
- Replaced the old bulk-message composition surface with the Claris-like Moodle conversation inbox: searchable conversation list, unread state, selected thread, day grouping, sender bubbles and message composer. The UI reads and sends only through typed `/api/messages/conversations` endpoints backed by the existing Moodle message gateway.
- Added the connector-backed conversation read/send contract, recipient scoping to known Moodle conversations, antiforgery and write-permission checks, React Query caching/invalidation and the shared Claris-like global activity line.
- Added the Claris-like account bootstrap flow: registration and first login now lead to the first Moodle connection before entering the operational shell, without broadening the default permission policy.
- Removed implementation-only connection/freshness rows from the main Courses, Schools and Dashboard compositions; the selected Moodle remains visible in the shared top bar.
- Kept unsupported Claris modules and actions out of the portal instead of fabricating backend capabilities.

## Comparison history

1. Audited the Claris source and Moodle Connector implementation.
2. Applied shell, page composition and density alignment.
3. Applied focused Tasks and Agenda visual/interaction alignment.
4. Unified task detail interactions across Tasks and Agenda.
5. Added bounded dashboard indicators and agenda event editing in the current connector backend.
6. Added lazy top-bar activity, real correction/pending indicators, bounded assignment analysis and Claris-aligned course detail composition.
7. Added dashboard week selection and Claris-aligned Settings surfaces with lazy access queries.
8. Ported the Claris-like Moodle conversation flow into the current connector and aligned global activity feedback.
9. Added the account registration and first-Moodle-connection path required by the existing onboarding backend.
10. Re-ran automated verification; visual comparison remains pending browser availability.

## Automated verification

## Moodle-first scope update

- Added `docs/product/claris-moodle-migration-matrix.md` with the source/dependency matrix, wave order and explicit `EXTERNAL_DEPENDENCY` deferrals.
- Removed direct chat delivery from the web route. Individual Moodle messages now use `prepare → preview → PendingAction → exact approval → /api/messages/confirm → Moodle → audit`.
- Added Moodle-first pending/correction, campaign, forum read and evidence-history surfaces. Campaigns prepare Moodle messages through the existing PendingAction approval flow; no external delivery provider was introduced.
- Added coverage for the direct-message preparation and confirmation gateway contract and the direct preparation handler.
- The presentation build and the offline-restored .NET test suite passed; the direct-message preparation/confirmation handlers are covered by the current test run.

## Current implementation status

- Added the internal Moodle-first automation runtime: definitions, PostgreSQL
  persistence, hosted scheduler, scoped worker execution context, retries,
  idempotent action claims and execution history.
- Added the portal Automacoes route with create, edit, remove, manual run and
  history flows. Moodle message actions stop at preview/PendingAction and need
  human approval before delivery.
- The external integration deferral remains unchanged: no IA chatbot,
  WhatsApp provider, external API key or new container was introduced.

- Frontend typecheck: passed.
- Frontend lint: passed.
- Frontend tests and production guard: passed (12 files, 22 tests; guard covered 125 source files).
- Frontend production build: passed (initial JS 471.08 kB, 150.21 kB gzip; MessagesPage chunk 39.70 kB, 11.60 kB gzip; PendingCorrectionsPage chunk 16.95 kB, 4.97 kB gzip; AutomationsPage chunk 17.77 kB, 5.15 kB gzip; CampaignsPage chunk 7.72 kB, 2.78 kB gzip; ForumsPage chunk 5.47 kB, 2.05 kB gzip).
- Presentation build: passed with 0 warnings and 0 errors.
- .NET application tests: passed after an offline restore using the local NuGet cache (649 passed, 0 failed, 0 skipped).
- `git diff --check`: passed; only expected line-ending warnings were reported.
- App smoke: passed; 16 SPA routes and 2 compatibility paths checked.
- Docker image was rebuilt from the current source and the local stack restarted successfully. Runtime smoke against the rebuilt app passed for 16 SPA routes and 2 compatibility paths; `/health` returned 200, `/api/session` returned 401 unauthenticated, and `/api/csrf` returned 200.
- Real read-only Moodle integration passed against the local Connector using the ignored live credentials: connection registration/validation, 5 course reads, 5 activity reads and the conversations endpoint (0 conversations) completed; temporary test accounts were removed afterward. The tested account lacks `core_enrol_get_enrolled_users`, so the participants endpoint now returns an explicit 502 capability error instead of an unhandled 500.

final result: blocked
