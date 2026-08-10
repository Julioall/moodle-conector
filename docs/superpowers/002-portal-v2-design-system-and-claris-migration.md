# Portal v2 Design System and Claris Migration

## Claris-first rule

Before creating UI, search the Claris repository for an equivalent. If one exists, adapt it; otherwise create a focused component. Preserve visual hierarchy, interaction, accessibility behavior, and useful tests.

## Migration layers

1. View/components: reuse `components/ui`, `components/layout`, course cards, student components, dashboard primitives, and task/agenda patterns.
2. View model/hooks: preserve screen-facing models but adapt loading, errors, freshness, query keys, and permissions.
3. Data adapter: rewrite clients and contracts for `/api/portal`; no Supabase tables, generated database types, Moodle credentials, or AI modules in Web.

## Target structure

```text
src/MoodleConnector.Web/
  src/app/{providers,routes}/
  src/components/{ui,layout}/
  src/features/{auth,connections,courses,students,dashboard,pending}/
  src/integrations/{http,auth,telemetry}/
  src/pages/
```

Map real Claris modules, not assumed folder names: services and Moodle/auth components feed `features/connections`; `features/courses`, `students`, `dashboard`, `tasks`, `agenda`, `messages`, `reports`, `settings` are migrated selectively. `features/claris`, chat, suggestions, grading panels, campaigns, and AI integrations are excluded.

## Visual constraints

Keep Claris shell, sidebar, top bar, cards, spacing, color tokens, typography, responsive behavior, and empty/loading/error states. Add Moodle selector and freshness labels without changing the overall visual language.
