---
name: moodle-follow-up
description: Identify students or activities requiring follow-up from observable Moodle activity, submissions and completion evidence.
---

# moodle-follow-up

Use this skill for operational follow-up: missing submissions, inactive students, incomplete activities, overdue work, and preparation of a follow-up list. It produces evidence and priorities; it does not send messages automatically.

## Workflow

1. Resolve connection, course, date window, and the population of students.
2. Gather the minimum evidence: assignment submission/status, completion, recent access, and—when requested—forum participation.
3. Apply explicit user criteria such as “late”, “not submitted”, or “no access in 14 days”. Do not invent a threshold.
4. Return each candidate with student ID, course/activity, evidence timestamp, source operation, and confidence.
5. If the caller wants outreach, hand off to `moodle-messaging` for prepare/confirm.

## Interpretation rules

“No submission” is valid only after the assignment and student scope are complete. “Inactive” requires a defined window and a connection that provides recent-access evidence. Missing optional data is reported as unknown, not as a negative finding.

## Safety and ownership

All reads use registered SafeRead or an owned specialized gateway. The skill does not select credentials, execute writes, or decide exposure. Large candidate sets must retain pagination metadata and may be summarized only after the source result is complete.
