# Limites do Conector

## Limites implementados

- `/mcp` exige JWT, API key ou ambos conforme configuração.
- JWT válido sem vínculo Moodle é rejeitado com `403`.
- API key inválida é rejeitada.
- Tools de escrita real no Moodle não estão disponíveis.
- Pending actions expiram conforme `MoodleConnector:PendingActionExpirationMinutes`.
- Confirmação de pending action exige texto exato.
- Confirmação por usuário diferente do criador exige `moodle.admin`.

## Limites funcionais atuais

- A leitura implementada é limitada a cursos do usuário Moodle resolvido.
- Não há catálogo docente completo de turmas/alunos.
- Não há envio real de mensagem.
- Não há feedback real.
- Não há lançamento ou ajuste de nota.
- Não há endpoint administrativo para consultar auditoria.

## Limites técnicos

- O schema inicial é aplicado por script SQL versionado no startup.
- Migrations EF formais ainda não fazem parte do fluxo; novos passos de schema devem ser adicionados como scripts versionados ou migrações antes de produção ampla.
- Rate limiting está implementado para portal/admin e `/mcp` por usuário/conector.
- Payloads de auditoria passam por sanitização centralizada.
- Rollback automatizado não está implementado.

## Recomendações

- Habilitar escritas reais apenas após revisão de escopos, auditoria e testes de carga.
- Usar contas Moodle com menor privilégio possível.
- Manter `MCP_REQUIRE_JWT=true` em produção.
- Manter `MCP_REQUIRE_API_KEY=true` apenas quando necessário para compatibilidade.
