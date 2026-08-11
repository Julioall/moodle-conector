# Portal v2 Product Specification

**Goal:** Transform the Moodle Connector into a Claris-based academic operations portal without moving Moodle access or AI behavior into the browser.

## Product boundary

The Portal is a deterministic operational interface. It may display, filter, count, export when supported, and open Moodle resources. It must not use MCP internally, call Moodle directly, use Supabase as its academic backend, invoke AI, grade submissions, change grades, produce feedback, or initiate grading flows.

```text
Portal Web → /api/portal/* → Application Services → Registry/Policy/Connections → Moodle
ChatGPT → MCP → same Application Services
```

## First release

Order: foundation, session/auth, Claris shell, connections and multi-Moodle selector, courses, course detail, students, dashboard, pending items, freshness/errors, feature flag.

Routes: `/`, `/cursos`, `/cursos/:connectionRef/:courseId`, `/alunos`, `/alunos/:connectionRef/:studentId`, `/pendencias`, `/conexoes`, `/configuracoes`.

## Resource identity

`CourseRef = connectionRef + courseId`; `StudentRef = connectionRef + studentId`; `ActivityRef = connectionRef + courseId + activityId`. These identities appear in DTOs, URLs, TanStack Query keys, logs, and audit correlation.

## Pending/grading boundary

“Aguardando correção” is read-only: view, filter, count, and open Moodle are allowed. Evaluating, changing grades, producing feedback, and triggering grading are prohibited.

## Later waves

Wave 2 adds tasks, agenda, follow-up, activity feed, manual messages with prepare/confirm, deterministic reports, settings, and workspace roles. Persistent operational entities use workspace scope from their first schema version.

## Definition of done

Claris-first UI, same-origin SPA served by ASP.NET, server-side authorization, multi-Moodle identity, deterministic DTOs, freshness, pagination, frontend/backend tests, CI and Docker green, no AI/Supabase/MCP imports in production Web, and no regression in MCP/ChatGPT flows.
