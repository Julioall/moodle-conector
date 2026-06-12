# Fluxo de Mensagens

Status: Planejado.

## Estado atual

Não existe tool implementada para envio real de mensagens no Moodle.

As feature flags existem:

```json
{
  "MessagesWriteEnabled": false,
  "ScheduledMessagesEnabled": false
}
```

## Fluxo planejado

1. Professor solicita preparação de mensagem.
2. Conector seleciona destinatários conforme curso/filtro.
3. Conector retorna prévia com:
   - curso;
   - destinatários;
   - assunto;
   - corpo;
   - impacto esperado.
4. Professor confirma com texto exato.
5. Conector executa envio.
6. Auditoria registra prepare, confirmação e resultado.

## Controles esperados

- Escopo: `moodle.write.messages`.
- Risco: `HumanConfirmedWrite`.
- Feature flag: `MessagesWriteEnabled`.
- Idempotência: segunda confirmação não deve reenviar.

## Fora do escopo atual

- Agendamento real de mensagens.
- Envio em lote por critérios complexos.
- Templates institucionais.
