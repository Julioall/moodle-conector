---
name: moodle-students
description: Resolve course participants, enrollment, groups and student activity context without changing membership.
---

# moodle-students

Use this skill for questions about students, participants, enrollment, groups, attendance context, and recent access. It is read-only and must not infer a student identity from a display name when Moodle returned an ambiguous match.

## Routing

- Course participants or enrolled users: `core_enrol_get_enrolled_users` through the participants flow.
- Course roster or student-only view: specialized participants tool.
- Groups and group membership: `core_group_get_course_groups` and the registered group flow.
- User lookup: `core_enrol_search_users` or `core_user_get_users_by_field`, subject to the current connection capability.
- Submission ownership: route to `moodle-assignments`; grades and risk decisions route to their respective skills.

## Identity and connection rules

Resolve the requested Moodle alias before querying. Preserve Moodle user IDs as the authoritative identity. If a name, email, or short identifier matches more than one person, return the ambiguity and ask the caller to refine it. Never use a user ID from another connection.

## Completeness

Roster and group endpoints may paginate or return capability-dependent fields. Mark partial results, retain the source course and connection, and do not convert an absent row into “not enrolled” until the relevant pages are exhausted.

## Boundaries

This skill decides which student-oriented flow should answer. It does not authorize writes, expose credentials, or bypass `SafeReadExecutor`. Enrollment changes and preference updates belong to controlled write workflows and are denied by generic read execution.
