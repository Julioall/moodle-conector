# Auth And Scopes

O endpoint MCP aceita JWT OAuth emitido pelo broker local e API key opcional do conector, conforme `McpServerSecurity`.

Escopos planejados:

- `moodle.read` (somente primitives universais legadas)
- `moodle.write` (somente primitives universais/compatibilidade)
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
- `moodle.read.forums`
- `moodle.write.forums`

`moodle.admin` não é scope emitido pelo broker e não pode ser solicitado pelo cliente.

As policies ficam registradas em `MoodleScopePolicies`. O manifesto MCP anuncia os scopes específicos de cada tool; scopes não são permissões de plataforma.

O `/authorize` emite somente a interseção entre scopes pedidos, scopes permitidos pelo cliente, permissões efetivas dos grupos do usuário e a capacidade coarse da conexão ativa. A revogação de um grupo é recalculada no limite MCP, sem depender da expiração do JWT.

API keys antigas com `CanWrite=true` continuam emitindo `moodle.write` para compatibilidade. Quando a chave pertence a uma conta local, as platform permissions efetivas dos grupos dessa conta são aplicadas; clientes de serviço legados ainda usam o contrato explícito `CanWrite` até receberem uma política própria.

Escopos legados armazenados na associação de equipe não são mais copiados para tokens MCP.
