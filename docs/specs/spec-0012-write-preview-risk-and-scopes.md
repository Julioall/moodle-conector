# SPEC-0012: Confirmação semântica, impacto e escopos de escrita

## Status

Draft. Depende de SPEC-0011.

## Objetivo

Fazer com que toda confirmação humana descreva a alteração, o alvo, o impacto e a autorização específica necessária.

## Contexto e evidência atual

O preview universal contém função, nomes de parâmetros, hash e risco genérico. Fluxos especializados já apresentam mais contexto. A categoria `ControlledWrite` mistura mensagens, notas, calendário e ações de submissão; a autorização tende a usar o escopo amplo `moodle.write`.

## Decisão e arquitetura-alvo

- `IWritePreviewBuilder` cria prévias por família, mantendo payload bruto e hash somente internamente.
- Prévia traz alvo resolvido, diff anterior→novo quando disponível, afetados, cobertura, limitações e alertas.
- Registry usa `UserStateWrite`, `CommunicationWrite`, `AcademicWrite`, `BulkAcademicWrite` e `DestructiveWrite`, além de idempotência.
- Escopos são específicos por domínio; `moodle.write` é apenas compatibilidade temporária.

## Escopo

- Builders e schemas para notas, mensagens, calendário, conteúdo e ações de submissão habilitadas.
- Validação por função antes de criar a ação pendente.
- Mapeamento de impacto e escopo por função.

## Fora de escopo

- Eliminar `PendingAction` ou confirmação literal.

## Critérios de aceite

- [ ] Nota individual mostra estudante, atividade e valor anterior/novo quando disponível.
- [ ] Ação em lote mostra quantidade, critérios de inclusão e alerta de impacto.
- [ ] Função sem schema/builder não cria escrita universal.
- [ ] Token sem scope de domínio não prepara nem confirma a escrita.

## Validação e evidências

```powershell
dotnet test tests/MoodleConnector.Application.Tests --filter "FullyQualifiedName~Grading|FullyQualifiedName~Messages|FullyQualifiedName~MoodleUniversalWrite"
```

## Rollout e rollback

Habilitar por família. Falha em um builder bloqueia apenas sua família e preserva os fluxos especializados estáveis.
