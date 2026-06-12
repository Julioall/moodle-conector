# Fluxo de Alunos em Risco

Status: Planejado.

## Estado atual

O conector ainda não possui tool dedicada para identificar alunos em risco.

A tool atual `listar_meus_cursos` retorna dados do usuário autenticado, não uma visão consolidada de alunos por curso.

## Fluxo planejado

1. Professor ou tutor informa o curso.
2. Conector busca indicadores como:
   - ausência de entrega;
   - atraso;
   - baixa participação;
   - tentativas esgotadas;
   - nota baixa quando disponível.
3. Conector retorna lista priorizada.
4. Professor pode preparar mensagem de acompanhamento quando a Fase 3 estiver implementada.

## Cuidados

- Indicadores são apoio à decisão, não diagnóstico definitivo.
- Não expor dados de alunos sem autorização.
- Evitar linguagem estigmatizante.
- Registrar auditoria para consultas sensíveis quando a funcionalidade for implementada.
