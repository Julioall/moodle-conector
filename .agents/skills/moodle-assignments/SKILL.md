---
name: moodle-assignments
description: Read assignment definitions, submissions, deadlines, status and grading context through the Moodle Connector.
---

# moodle-assignments

Use this skill for assignment work: finding activities, inspecting their configuration, listing submissions, checking delivery status, and assembling grading context. It does not launch grades, send messages, or mutate submission state.

## Routing

- Assignment discovery or deadlines: `mod_assign_get_assignments`.
- Submission listing or filtering: `mod_assign_get_submissions`.
- A single submission/status request: `mod_assign_get_submission_status` or the registered specialized submission flow.
- Existing grades or grade definitions: `mod_assign_get_grades` and the gradebook skill.
- Course membership or student identity: route to `moodle-students`.
- Any grade/write intent: route to `moodle-grading` and require its controlled workflow.

## Identifiers

- `courseId` identifies the Moodle course and must be resolved in the selected connection.
- `assignmentId` / `instanceId` identifies the assignment activity instance used by the submission APIs.
- `cmid` is the course-module id. It is not interchangeable with `assignmentId`; when a tool accepts either, preserve which form the caller supplied and resolve it through course contents/activity discovery.
- `userid` identifies the Moodle user. Do not substitute a display name, local account id, or a user from another connection.

## Execution contract

Resolve the connection and capabilities first. Use `SafeReadExecutor` for canonical read operations when the request is a direct read. Use the assignment gateway when it performs pagination, joins submissions with students, or normalizes Moodle-specific shapes. Preserve `courseid`, `assignmentid`, `userid`, `status`, `before`, and `after` semantics; never silently replace an explicit student or course.

## Pagination and evidence

Treat a response with `hasMore` or a Moodle paging cursor as incomplete. Continue only when the user asked for all results or the specialized flow owns continuation. Report the retrieved scope and any truncation. A missing submission is not evidence that a student did not submit unless the requested page/filter was exhausted.

## Fallback and ownership

If the primary function is unavailable, consult the capability snapshot and choose a registered fallback. Do not guess a function name or invoke an unknown operation. The skill owns intent resolution and interpretation; Registry, PolicyEngine, SafeRead, credential selection, and write confirmation remain platform-owned.

If assignment discovery succeeds but the submission capability is unavailable, report the missing capability and stop at discovery; do not infer that there are no submissions. For a request that combines submissions with student identity, keep the handoff explicit: Assignments owns submission state, Students owns participant identity, and Follow-up owns pedagogical prioritization.
