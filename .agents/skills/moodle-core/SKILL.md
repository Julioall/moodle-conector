---
name: moodle-core
description: Fundamental operations to interact with Moodle, including discovering available connections, site info, and user tokens. Always use this as the foundation before performing other specific domain operations.
---

# moodle-core

This SKILL provides the foundational cognitive layer for Antigravity to interact with Moodle instances through the Moodle Connector architecture.

## Responsibilities

1. **Connection Resolution**: Resolving requested Moodle environments (e.g. 'FIEG', 'SENAI', 'Nacional').
2. **Authentication & Identity**: Fetching user tokens and site info using SafeReadExecutor.
3. **Capability Discovery**: Fetching and understanding the available functions for a given token/connection before attempting complex operations.

## Architecture Guidelines

- **SafeReadExecutor**: You MUST route all your data retrieval through `SafeReadExecutor.ExecuteAsync`. Do NOT use legacy wrappers for operations categorized as R1 or R2.
- **NormalizationContext**: Be aware that responses will be returned by default in `Agent` mode, meaning large arrays will be truncated to prevent context flooding. If you require full payloads for a specific semantic task, switch to `NormalizationMode.Shadow`, but handle memory footprint with care.
- **Evidence Based Execution**: Only use Moodle Web Service operations that have been `LiveValidated` in the `OperationRegistry`.

## Typical Workflow

Do **not** call `core_webservice_get_site_info` before every workflow. Use the Capability Registry as the default source of current connection capabilities.

Request a capability refresh only when:
- No valid snapshot exists.
- The snapshot has expired.
- The connection/credential has changed.
- An expected function becomes unexpectedly unavailable.
- Diagnostics explicitly require a refresh.

1. Identify which Moodle alias the user wants to connect to (if not provided, default to 'fieg' or prompt the user).
2. Check capabilities if needed via `CapabilityRegistry`.
3. Proceed to use domain-specific skills (like `moodle-courses`) to perform real-world tasks.

## Known Supported Operations (LiveValidated)
- `core_webservice_get_site_info`
- `core_enrol_get_users_courses`
- `core_course_get_courses_by_field`
- `mod_assign_get_submissions`

> [!WARNING]
> Do not attempt to use destructive operations (writes/updates) through the SafeReadExecutor. They will be denied by the PolicyEngine. Wait for the SafeWriteExecutor implementation.
