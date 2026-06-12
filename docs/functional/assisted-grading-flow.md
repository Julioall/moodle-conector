# Fluxo de Avaliação Assistida

Status: Planejado.

## Estado atual

Não há tool implementada para lançar nota, alterar nota ou publicar feedback real no Moodle.

As flags existem, mas escritas estão desabilitadas por padrão:

```json
{
  "AssignmentFeedbackWriteEnabled": false,
  "AssignmentGradeWriteEnabled": false
}
```

## Feedback sem nota

Planejado para Fase 4:

1. Professor solicita preparação de feedback.
2. Conector gera ou organiza rascunho.
3. Professor revisa a prévia.
4. Professor confirma publicação.
5. Auditoria registra o texto aprovado.

## Notas

Planejado para Fase 5:

1. Professor solicita preparação de nota.
2. Conector valida limites e contexto.
3. Conector exige justificativa quando aplicável.
4. Professor confirma com texto exato.
5. Escrita é executada somente após confirmação.

## Controles esperados

- Feedback: `moodle.write.assignments.feedback`.
- Nota: `moodle.write.assignments.grade`.
- Nota usa risco mínimo `CriticalHumanConfirmedWrite`.
- Operações em lote de nota ficam fora do escopo até aprovação explícita.
