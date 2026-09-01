# Design QA — Agenda e Tarefas profissionais

## Comparison target

- Source visuals: `docs/specs/assets/agenda-professional/task-list-detail-reference.png`, `event-edit-modal-reference.png`, `task-modal-reference.png`, `agenda-list-detail-reference.png`.
- Implementation URLs: `http://localhost:8787/tarefas?connectionRef=goias` and `http://localhost:8787/agenda?connectionRef=goias`.
- Implementation captures: `.audit-screenshots/tasks-final.png` and `.audit-screenshots/agenda-final.png`.

## Normalization

- Reference captures are desktop light-theme screens at approximately 1672 × 941.
- The local Browser viewport is approximately 1256 × 912 at desktop and was also checked at 390 × 844.
- The authenticated local test account has zero tasks and zero agenda events. The empty state is real data; no fictitious records were inserted. Populated row/detail comparison is consequently limited by available data.

## Evidence

### Comparison passes

- Typography: headings, supporting copy, compact metadata, labels, and modal hierarchy use the existing Claris type scale with readable contrast and truncation-safe content.
- Spacing and layout: list groups use bordered containers with divider rows; task rows expose status, priority, owner, deadline, progress, and actions in a reference-like desktop grid; agenda rows expose time, duration, event icon, type, context, and actions. The non-reference bulk-selection action and the empty Blocked Kanban column were removed from the primary board surface.
- Viewport resilience: desktop and 390 px mobile snapshots show no overlap or horizontal clipping; desktop-only row columns collapse safely on narrow screens and the shell switches to compact navigation.
- Colors and tokens: light surfaces, subtle borders, semantic priority/status pills, primary accents, and sticky panel/modal surfaces follow the application token system.
- Images and icons: no raster imagery is required by the references; Lucide icons are consistently sized and aligned.
- Copy and content: Portuguese headings, filter controls, empty states, modal labels, and actions remain coherent. IANA timezone, RRULE, location, UUID, and structured-reference fields remain hidden from user-facing forms; compatibility values are retained internally.
- States and interactions: list/calendar toggles, filter disclosure, create buttons, task/event modals, modal scrolling, and detail drawers were opened and inspected. Drawers do not dim the workspace and retain a fixed action footer. The shared task/event tag editor converts entries into removable, category-colored chips; escola/curso/aluno prefixes are transient suggestion filters and are not persisted.
- Accessibility: semantic headings, labelled controls, checkbox controls, modal focus, and mobile tap targets were preserved; no console warnings/errors were observed.

## Findings

No actionable P0, P1, or P2 visual findings remain for the available local data state.

- P3 / data-state difference: references contain populated examples and detail panes, while the local test account is empty. This is a validation-data limitation, not a rendering defect.
- P3 / shell reuse: existing Claris navigation/top bar remains instead of duplicating the reference shell; the change stays scoped to Agenda and Tasks.

## Comparison history

1. Refined the task and agenda list density to mirror the supplied row-based references.
2. Added filter disclosure, grouped bordered lists, wider non-modal detail panes, and sticky action footers.
3. Rebuilt the container, recaptured both routes, opened both creation modals, and checked a mobile viewport.

## Interaction validation

- Opened `/tarefas` and `/agenda` with the local test account.
- Opened the “Nova tarefa” and “Novo evento” modals and confirmed the requested event fields are absent.
- In “Novo evento” and “Nova tarefa”, typed prefixed values and confirmed conversion to removable chips containing only the selected value (without the prefix).
- Checked the desktop empty states and a 390 × 844 mobile viewport.
- Rebuilt and restarted `moodle-connector-app`; the health endpoint returned `Healthy`.
- `npm run typecheck` passed.
- Focused task/shell tests passed (4 tests) and the course-panel test passes in isolation (3 tests). A full parallel run intermittently reported 9 unrelated `course-panel` failures; no task/agenda test failed.
- `npm run build` passed.
- Browser console error/warning check was empty.

## Final result

passed
