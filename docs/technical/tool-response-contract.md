# Tool Response Contract

Tools MCP devem retornar `StructuredContent` com dados previsiveis e texto curto em `Content`.

Consultas:

```json
{
  "status": "ok",
  "data": {},
  "warnings": [],
  "auditId": null,
  "timestamp": "2026-05-31T00:00:00Z"
}
```

Acoes pendentes:

```json
{
  "status": "pending_confirmation",
  "pendingActionId": "00000000-0000-0000-0000-000000000000",
  "toolName": "preparar_acao_demo",
  "riskLevel": "HumanConfirmedWrite",
  "preview": {},
  "confirmationText": "CONFIRMAR ...",
  "expiresAt": "2026-05-31T00:15:00Z"
}
```

Confirmacao:

```json
{
  "status": "confirmed",
  "pendingActionId": "00000000-0000-0000-0000-000000000000",
  "toolName": "preparar_acao_demo",
  "riskLevel": "HumanConfirmedWrite",
  "confirmedAt": "2026-05-31T00:01:00Z",
  "auditId": "correlation-id"
}
```
