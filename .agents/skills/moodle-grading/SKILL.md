---
name: moodle-grading
description: Discover grading capabilities, prepare grading work, review evidence and execute human-confirmed grade changes.
---

# moodle-grading

Use this skill for gradebook reads, grading preparation, AI-assisted grading review, batch previews, confirmation, and audit. Reading grades and writing grades are separate workflows.

## Read path

- Gradebook and activity grades use the gradebook gateway and registered read operations such as `gradereport_user_get_grade_items`, `gradereport_user_get_grades_table`, and `mod_assign_get_grades`.
- Capability discovery uses the grading discovery flow and must report blockers for missing submissions, files, or write functions.
- Submission context comes from `moodle-assignments`; student identity comes from `moodle-students`.

## Controlled write path

1. Prepare a bounded grading batch with explicit course, assignment, students, rubric/criteria, and proposed values.
2. Validate permissions, feature flags, Moodle capabilities, conflicts, and idempotency.
3. Present a preview with parameter hash, item count, warnings, and expiration.
4. Require the pending-action confirmation service before `mod_assign_save_grade` or batch grade execution.
5. Persist an audit record for every item and expose partial failures without claiming full success.

Never call grade-write functions through `SafeReadExecutor` or the generic read tool. Human confirmation is a product boundary and cannot be replaced by an LLM decision.
