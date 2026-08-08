---
name: moodle-courses
description: Core operations for managing and retrieving information about Moodle courses, such as listing a user's courses, finding specific courses, and retrieving course contents.
---

# moodle-courses

This SKILL provides the cognitive layer for interacting with Courses in Moodle. It translates user intents into canonical Moodle Web Service operations using the SafeReadExecutor.

## Responsibilities & Scope (v0.1)

This SKILL is strictly scoped to the following domain actions:
- List the user's enrolled courses.
- Search/resolve a course by its ID or shortname.
- Get basic course details.
- Get course structure/contents (modules, sections).
- Resolve course/module identifiers.

> [!IMPORTANT]
> **Out of Scope:** This SKILL must **not** handle assignments, students, grades, or risks. 
> - For "Quem está matriculado?", route to `moodle-students`.
> - For "Quem não entregou?", route to `moodle-assignments`.
> - For "Lançar nota", route to grading workflows.

## Canonical Operations

Whenever possible, translate the user's intent to one of these registered and `LiveValidated` operations via the `SafeReadExecutor`:

| User Intent | Canonical Operation |
| :--- | :--- |
| "List my courses", "What courses do I have?" | `core_enrol_get_users_courses` |
| "Find course 1071864", "Get course X" | `core_course_get_courses_by_field` |
| "Show the course structure", "What contents are in this course?" | `core_course_get_contents` |

## Typical Workflow

1. **Understand Intent**: Determine if the user is asking about courses (and only courses).
2. **Prerequisites**: Ensure you have resolved the connection alias and capabilities (via `moodle-core` SKILL). You need the `moodleUserId` for some operations (which you may have from site info).
3. **Execute**: Call the appropriate Canonical Operation via `SafeReadExecutor`.
4. **Interpret**: The response may be wrapped if truncated in Agent Mode (`truncated: true`). Interpret the structure and present it meaningfully to the user.

> [!TIP]
> Do not rely on legacy wrapper tools for the canonical operations listed above. Use the registry and `SafeReadExecutor` directly.
