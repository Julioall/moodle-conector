# Auth And Scopes

O endpoint MCP aceita JWT OAuth emitido pelo broker local e API key opcional do conector, conforme `McpServerSecurity`.

Escopos planejados:

- `moodle.read.courses`
- `moodle.read.students`
- `moodle.read.groups`
- `moodle.read.access`
- `moodle.read.contents`
- `moodle.read.resources`
- `moodle.read.activities`
- `moodle.read.assignments`
- `moodle.read.submissions`
- `moodle.read.quizzes`
- `moodle.read.scorms`
- `moodle.write.messages`
- `moodle.write.assignments.feedback`
- `moodle.write.assignments.grade`
- `moodle.write.course_content`
- `moodle.admin`

As policies ficam registradas em `MoodleScopePolicies`. A confirmacao de pending action tambem pode receber um `requiredScope`; se o usuario nao possuir esse escopo, a confirmacao falha antes de qualquer escrita.

API keys antigas com `CanWrite=true` continuam emitindo `moodle.write` para compatibilidade. As novas tools devem migrar gradualmente para escopos especificos.
