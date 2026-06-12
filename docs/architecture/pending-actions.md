# Pending Actions

O conector deve separar escrita em duas etapas:

1. `prepare`: valida a intencao, monta uma pre-visualizacao sanitizada, grava `PendingMoodleAction` e registra auditoria.
2. `confirm`: exige o texto exato de confirmacao, valida usuario/escopo/expiracao e marca a acao como confirmada.

Nenhuma tool voltada a escrita deve executar uma mudanca Moodle diretamente na primeira chamada. A primeira chamada retorna `pendingActionId`, `preview`, `confirmationText` e `expiresAt`.

O servico `IPendingActionService` cria a acao pendente. O servico `IActionConfirmationService` confirma de forma idempotente: uma segunda confirmacao da mesma acao ja confirmada retorna `confirmed` sem criar nova execucao.

Estados suportados:

- `PendingConfirmation`
- `Confirmed`
- `Executing`
- `Executed`
- `Expired`
- `Cancelled`
- `Failed`

Por padrao, a expiracao vem de `MoodleConnector:PendingActionExpirationMinutes`.
