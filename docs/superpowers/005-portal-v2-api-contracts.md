# Portal v2 API Contracts

## Endpoints

```text
GET /api/portal/session
GET /api/portal/connections
GET /api/portal/courses
GET /api/portal/courses/{connectionRef}/{courseId}
GET /api/portal/courses/{connectionRef}/{courseId}/students
GET /api/portal/courses/{connectionRef}/{courseId}/activities
GET /api/portal/students/{connectionRef}/{studentId}
GET /api/portal/pending
GET /api/portal/dashboard
```

## Envelope

Single resource:

```json
{"data": {}, "meta": {"generatedAt":"2026-08-10T03:53:00Z","connectionRef":"fieg"}}
```

List:

```json
{"data":[],"meta":{"page":1,"pageSize":20,"returned":20,"total":124,"hasMore":true,"generatedAt":"2026-08-10T03:53:00Z"}}
```

Errors use a stable code, human-safe message, and server-generated correlation ID. The browser may send `X-Correlation-ID`, but the server accepts only a valid format or generates a new ID; the browser value is never trusted as the audit identity.

## Contract rules

DTOs are presentation contracts, not serialized domain entities. Every Moodle resource preserves `connectionRef`; list endpoints paginate; all Moodle-derived responses expose freshness; secrets are never returned. Dashboard aggregation has an operational budget and must not synchronously fan out into dozens of expensive gradebook calls. Expensive data belongs in focused endpoints or projections/cache after measurement.
