---
name: moodle-messaging
description: Prepare, review and confirm Moodle messages for targeted student follow-up with auditable safety controls.
---

# moodle-messaging

Use this skill when the requested outcome is to contact students, groups, or conversations. Reading conversations may use registered read operations; sending or changing message state always uses the controlled write workflow.

## Workflow

1. Resolve the Moodle connection and recipient identities from the current course/student scope.
2. Draft the message with recipient IDs, subject/context, body, and a reason for the outreach.
3. Validate recipient count, duplicates, opt-in/product limits, connection, and message capability.
4. Present a preview and parameter hash through the pending-action service.
5. Send only after explicit confirmation; record audit status per recipient.

## Safety

`core_message_send_instant_messages` and related write functions are controlled operations. They must never be exposed as generic reads or executed from a discovery guess. A failed or partial send is reported as such, with retry guidance and no duplicate retry without idempotency evidence.

## Boundaries

This skill owns message intent, targeting and interpretation. Credential resolution, operation registration, policy, confirmation, and audit persistence remain deterministic platform services. Follow-up analysis hands candidates to this skill but does not authorize sending.
