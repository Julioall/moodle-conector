---
name: moodle-messaging
description: Ler conversas e preparar, revisar e confirmar mensagens Moodle individuais para acompanhamento estudantil com limites e auditoria.
---

# moodle-messaging

Use quando o resultado desejado e contatar estudantes ou responder a uma conversa. A skill nao promete broadcast atomico, agendamento ou leitura da mensagem.

## Fluxo

1. Resolva alias e destinatarios pelo `userid` da conexao atual; uma lista de follow-up e apenas sugestao ate a identidade ser reconfirmada.
2. Consulte conversas somente quando necessario com as funcoes `core_message_get_*` registradas.
3. Antes de redigir cobranca, feedback ou recuperacao, consulte `moodle-pedagogy` e informe a evidencia que motivou o contato.
4. Use o par de prepare/confirm apropriado: `prepare_welcome_message`, `prepare_access_reminder`, `prepare_activity_reminder`, `prepare_recovery_message`, `prepare_closing_message` ou `prepare_followup_message`, seguido do confirm correspondente.
5. Valide destinatarios, duplicidade, quantidade, conexao, capability e flag. Mostre previa, motivo, escopo, hash, expiracao e texto literal.
6. Envie somente depois de confirmacao explicita e registre sucesso/falha por destinatario.

## Seguranca

`core_message_send_instant_messages` e demais escritas de mensagem sao controladas. Nao as invoque como leitura generica, nao descubra nome de funcao por tentativa e nao repita envio sem evidencia de idempotencia. Falha parcial permanece parcial.

Follow-up identifica candidatos; esta skill prepara comunicacao. Credenciais, policy, pending action, escopo, capability e auditoria pertencem aos servicos deterministicos.
