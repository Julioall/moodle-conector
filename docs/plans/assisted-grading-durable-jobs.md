# Plano de implementação — jobs duráveis de correção assistida

Referência: [SPEC-0022](../specs/spec-0022-durable-assisted-grading-jobs.md).

Este plano cobre a evolução do lote para execução recuperável e de baixa latência. Não
implementa histórico analítico de snapshots, métricas de tutores, dashboard ou cálculo de
carga; esses itens pertencem a uma fase posterior.

## Etapas

| Etapa | Estado | Entrega |
|---|---|---|
| 0. Contratos e limites | Concluída | Estados do lote/item, limites de arquivo, retenção e prioridade documentados. |
| 1. Persistência durável | Concluída | Claims, leases, tentativas, checkpoint do lote e migrations 041–047. |
| 2. Coordenação multi-réplica | Concluída | Claim de item, recuperação de leases expirados, fairness por aging e testes PostgreSQL. |
| 3. Criação leve | Concluída | Flag `DeferHeavyIngestion`, artifacts com referência normalizada e migration 048. |
| 4. Worker de ingestão | Concluída | Recuperação de conexão fora de HTTP, descoberta de referências MCP, claims por janela e checkpoint por estágio. |
| 5. Retenção e operação | Parcial | Worker de retenção e redaction implementados; métricas de backlog/idade e alertas ainda pendentes. |
| 6. Certificação | Em andamento | Cenário de 400 itens, restart, duas réplicas, idempotência e rollout gradual; o gate PostgreSQL precisa ser executado no CI/ambiente efêmero. |

## Especificação operacional da etapa 3/4

1. O handler cria o lote e seus itens antes de qualquer trabalho pesado. Arquivos de submissão
   são registrados como `pending`; materiais de curso são descobertos pelo worker.
2. A referência de arquivo guarda somente esquema/host/caminho sem query, fragmento ou user
   info. O gateway Moodle continua validando o endpoint e injeta o token apenas no momento do
   download.
3. O worker entra em `IConnectorExecutionContext` usando identidade não secreta persistida no
   lote e limpa o contexto em `finally`.
4. Claims são obtidos antes da validação das referências. O worker salva `Ingestion`, permite checkpoints
   de `Context`/`Analysis` no processor e só libera os leases após a persistência final.
5. O worker não baixa nem extrai arquivos. O chat registra e lê o resource original; falhas de
   registro/leitura bloqueiam o item sem inventar conteúdo ou disparar escrita Moodle.
6. A flag `LegacySubmissionExtractionEnabled=false` não possui fallback operacional. Se o MCP
   Resource estiver desligado, novas correções devem permanecer bloqueadas.

## Gates de aceite

- Teste unitário prova que criação diferida não chama download, extraction ou contents.
- Teste unitário prova que o worker preserva a referência e não executa download ou extração.
- Schema test prova a migration 048 e sua idempotência.
- Suíte completa .NET, validadores de documentação/skills e PostgreSQL efêmero passam.
- Antes do rollout final: teste de 400 itens, queda entre `Pending` e enqueue, restart no meio
  de cada estágio, duas réplicas e verificação de ausência de tokens nos logs/DB.
