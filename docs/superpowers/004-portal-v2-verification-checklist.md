# Portal v2 Verification Checklist

## Frontend

- [ ] `npm run lint`
- [ ] `npm run typecheck`
- [ ] `npm test`
- [ ] `npm run build`
- [ ] guard rejects Supabase, OpenAI, MCP, chat, suggestion, and grading imports
- [ ] route smoke test covers login, dashboard, course, student, pending, and connection
- [ ] loading, empty, error, pagination, multi-Moodle, and freshness states verified

## Backend

- [ ] endpoint authorization and session behavior tested
- [ ] DTO, envelope, pagination, `connectionRef`, errors, secrets, and freshness contract tests pass
- [ ] dashboard budget is measured and documented
- [ ] pending correction remains read-only
- [ ] CSRF and cookie protections are active before first mutation
- [ ] existing MCP, OAuth, capability, shadow, and Moodle tests remain green

## Delivery

- [ ] feature flag switches legacy and Portal v2 safely
- [ ] Docker multi-stage build passes without Node in final runtime
- [ ] CI runs frontend and .NET checks
- [ ] same-origin SPA fallback does not capture `/api`, `/mcp`, OAuth, health, or well-known routes
