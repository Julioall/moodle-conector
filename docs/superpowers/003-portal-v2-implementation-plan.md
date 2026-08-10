# Portal v2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Claris-first Moodle Connector Portal in `src/MoodleConnector.Web`, backed by stable `/api/portal` contracts and the existing .NET application core.

**Architecture:** Migrate Claris UI in three layers—views, view models/hooks, and data adapters—while keeping all Moodle access, authorization, CSRF, aggregation, and persistence server-side. The ASP.NET application serves the built SPA same-origin and preserves MCP as a separate interface over the same Application Services.

**Tech Stack:** React, TypeScript, Vite, React Router, TanStack Query, Tailwind, Vitest, React Testing Library, ASP.NET Core, existing Moodle Connector Application/Infrastructure, Docker.

---

## Task 1: Establish Web project

**Files:** Create `src/MoodleConnector.Web/package.json`, `vite.config.ts`, `tsconfig.json`, `index.html`, `src/main.tsx`, `src/app/`; modify solution/build files as needed.

- [ ] Copy the minimal Vite/React configuration from Claris and set the project root to `src/MoodleConnector.Web`.
- [ ] Add scripts `dev`, `lint`, `typecheck`, `test`, and `build`.
- [ ] Add the Web project to repository build orchestration without changing existing .NET project boundaries.
- [ ] Run `npm ci`, `npm run typecheck`, and `npm run build`; expected: a static `dist` is produced.

## Task 2: Migrate Claris design system and shell

**Files:** Create `src/MoodleConnector.Web/src/components/ui/`, `src/components/layout/`, `src/index.css`; tests under `src/test/`.

- [ ] Migrate only the primitives and shell used by the first release: `AppLayout`, `AppSidebar`, `TopBar`, logo/icon, cards, badges, buttons, inputs, tables, tabs, skeleton, toast, alert, pagination, and responsive sidebar.
- [ ] Preserve Claris visual tokens and responsive behavior; remove imports that transitively load Supabase, AI, chat, grading, or campaigns.
- [ ] Add a shell test asserting navigation labels and mobile sidebar behavior.
- [ ] Run the focused Vitest test; expected: PASS.

## Task 3: Add HTTP, auth, CSRF, and query foundations

**Files:** Create `src/MoodleConnector.Web/src/integrations/http/portal-client.ts`, `src/integrations/auth/session-gateway.ts`, `src/app/providers/AppProviders.tsx`, `src/features/auth/`; modify `MoodleConnector.Presentation` auth configuration.

- [ ] Implement same-origin `fetch` with credentials, timeout, abort signal, normalized errors, and optional valid `X-Correlation-ID`.
- [ ] Make the backend validate or replace correlation IDs and generate the authoritative audit ID.
- [ ] Implement session bootstrap from `/api/portal/session` and cookie-based auth with `HttpOnly`, `Secure`, `SameSite`, and antiforgery token issuance before mutations.
- [ ] Add tests for 401, timeout, malformed correlation ID, CSRF failure, and session loading.
- [ ] Run frontend and backend focused tests; expected: PASS.

## Task 4: Freeze Portal API contracts

**Files:** Create DTO/contract tests in `src/MoodleConnector.Presentation` and `tests/MoodleConnector.Application.Tests/Portal/`; create matching TypeScript contracts under `portal/src/features/*/api/contracts`.

- [ ] Implement the envelope and list metadata from `docs/superpowers/005-portal-v2-api-contracts.md`.
- [ ] Define `CourseRef`, `StudentRef`, and `ActivityRef` consistently in URLs, DTOs, query keys, and logs.
- [ ] Add contract tests for authorization, pagination, freshness, `connectionRef`, errors, and absence of secrets.
- [ ] Run the focused .NET test project; expected: PASS.

## Task 5: Implement connections and multi-Moodle state

**Files:** Create `src/MoodleConnector.Web/src/features/connections/`; add/refactor Portal connection endpoints and DTOs in Presentation/Application.

- [ ] Migrate the Claris Moodle connection card and onboarding visual components, replacing data clients with Portal contracts.
- [ ] Implement selected connection state with `all` and individual `connectionRef` values.
- [ ] Ensure every query key includes the connection scope and every result preserves its source.
- [ ] Add tests for card rendering, selector behavior, capability display, and connection-scoped cache keys.

## Task 6: Implement courses and course detail

**Files:** Create `src/MoodleConnector.Web/src/features/courses/`; create/refactor course Portal handlers, DTOs, and tests in Application/Presentation.

- [ ] Migrate Claris course cards, filters, pagination, course panel layout, and activity sections.
- [ ] Implement `/courses` and `/courses/{connectionRef}/{courseId}` with existing course services and policy checks.
- [ ] Add student/activity subresources with explicit composite identity.
- [ ] Test list pagination, course detail, connection collision, loading, empty, error, and stale-data states.

## Task 7: Implement students and consolidated profile

**Files:** Create `src/MoodleConnector.Web/src/features/students/`; add student aggregator handlers/DTOs and tests.

- [ ] Migrate student list, profile, grades/read-only, activity, completion, and history components.
- [ ] Implement `/students/{connectionRef}/{studentId}` as a backend aggregate rather than five browser calls.
- [ ] Keep grades and activity informational; do not add grading commands.
- [ ] Test composite identity, profile aggregation, pagination, authorization, and no-write behavior.

## Task 8: Implement dashboard as bounded composition

**Files:** Create `src/MoodleConnector.Web/src/features/dashboard/`; add dashboard query/DTO/handler tests.

- [ ] Implement `/api/portal/dashboard` only after course/student read models are stable.
- [ ] Include summary, priorities, pending indicators, recent activity, generated time, and connection scope.
- [ ] Set and test an operational fan-out budget; avoid synchronous gradebook fan-out and move expensive data to focused endpoints/projections when measured.
- [ ] Test one-request dashboard loading, bounded latency instrumentation, stale fallback messaging, and multi-Moodle aggregation.

## Task 9: Implement read-only pending work

**Files:** Create `src/MoodleConnector.Web/src/features/pending/`; add pending endpoint handlers/DTOs/tests.

- [ ] Migrate the Claris pending/review visual pattern.
- [ ] Allow view, filter, count, pagination, and “open in Moodle”.
- [ ] Explicitly reject evaluate, change grade, feedback, and grading-flow operations in UI and API.
- [ ] Add contract and UI tests proving no grading command exists in the Portal surface.

## Task 10: Add freshness, errors, feature flag, and SPA hosting

**Files:** Modify Web query hooks, Presentation routing/static files, Dockerfile, Caddy/nginx configuration, and configuration options.

- [ ] Show `Atualizado em` for Moodle-derived screens and last valid query time after failure.
- [ ] Implement `PortalV2__Enabled` with safe legacy fallback.
- [ ] Configure ASP.NET to serve `MoodleConnector.Web/dist` and preserve `/api`, `/mcp`, OAuth, health, and well-known routes outside SPA fallback.
- [ ] Run a navigation smoke test through the feature flag; expected: both legacy and v2 paths resolve correctly.

## Task 11: Add CI, guards, Docker, and final verification

**Files:** Modify CI workflow, Dockerfile, `.gitignore`, and add `scripts/check-portal-web-boundary.mjs` plus tests.

- [ ] Add frontend lint, typecheck, tests, build, .NET restore/build/test, and Docker build to CI.
- [ ] Fail the guard when production Web imports Supabase runtime access, OpenAI, MCP, chat, suggestions, grading, or direct Moodle clients.
- [ ] Ensure final image contains ASP.NET runtime and Web static assets but no Node runtime requirement.
- [ ] Run the complete checklist in `docs/superpowers/004-portal-v2-verification-checklist.md` and record results.

## Task 12: Wave 2 operational modules

**Files:** Create/extend `src/MoodleConnector.Web/src/features/tasks`, `agenda`, `follow-up`, `messages`, `reports`, `settings`, and workspace-scoped backend modules.

- [ ] Migrate each Claris feature through views, hooks/view models, and Portal data adapter.
- [ ] Add own persistence for tasks, follow-up, manual events, and activity feed with `WorkspaceId` from the first schema.
- [ ] Expose messages only through safe prepare/confirm flows; keep campaigns hidden until an approved backend contract exists.
- [ ] Add deterministic reports and export contracts without LLM dependency.
- [ ] Test each feature independently before removing the legacy route.
