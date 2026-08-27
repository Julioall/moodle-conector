# SPEC-0015: Base sólida de snapshots operacionais

## Status

Implementing. Depende de SPEC-0013.

## Objetivo

Transformar os snapshots atuais em uma base operacional estável, performática e auditável. A entrega preserva apenas estado sincronizado e sua proveniência técnica; não calcula, armazena ou expõe métricas, carga, capacidade, `AnalysisBatch`, dashboard ou GPT.

## Contexto e evidência atual

`moodle_snapshots` sobrescreve por owner, alias, tipo e curso, sendo um cache materializado de estado corrente. `moodle_sync_states`, fila e locks usam alias mutável. Há controle de lease no PostgreSQL, mas também fluxos de leitura seguida de inserção que podem conflitar entre instâncias. `dashboard_access_snapshots` não será estendido nesta mudança.

## Decisão e arquitetura-alvo

Separar claramente:

1. **Conexão:** `ConnectionId` é a identidade imutável; alias só é metadado de apresentação.
2. **Estado corrente:** um único head publicado por `(ownerId, connectionId, dataset, resourceId)`, usado pelas consultas operacionais.
3. **Execução de sincronização:** `SnapshotRun` e seus itens registram o ciclo técnico que tentou produzir estado, sem copiar payloads históricos.

Um run possui ID, owner, connection ID, início/fim, estado, versão de schema, versão do sincronizador, gatilho, contagens, cobertura, erros sanitizados e timestamps de freshness. Um item de run possui dataset/recurso, estado, tentativas, hash do payload, contagem, duração e erro sanitizado. Ele é evidência técnica, não fato analítico.

Publicação é atômica por recurso: a sincronização valida e grava o payload; em seguida atualiza o head no mesmo commit. Leitores usam somente heads publicados, portanto nunca recebem payload parcial de uma tentativa em andamento. Consultas com vários datasets devem declarar a consistência por recurso e timestamps de cada head; consistência global não é prometida implicitamente.

## Escopo

- Migrar aliases de chave para `ConnectionId` em snapshots, sync states, fila, locks e referências associadas.
- Criar `SnapshotRun` e `SnapshotRunItem` somente como diário técnico de sincronização.
- Padronizar estados: `pending`, `running`, `succeeded`, `partial`, `failed`, `cancelled` e `superseded`.
- Implementar upsert PostgreSQL seguro e leases distribuídos; o lock em memória permanece somente como otimização local.
- Definir freshness, retenção, limites de concorrência, paginação/chunking e telemetria.
- Preservar JSONB para payload de fonte, com `PayloadHash`, tamanho e `RecordCount`; indexar somente chaves de acesso.

## Fora de escopo

- `AnalysisBatch`, métricas de desempenho, carga de trabalho, capacidade, pesos, relatórios, dashboard, GPT ou simulação.
- Novos datasets Moodle que não sejam necessários para manter snapshots existentes.
- Cópias históricas completas de payloads Moodle ou alterações em `dashboard_access_snapshots`.

## Contratos, compatibilidade e migração

- **Expand:** adicionar `ConnectionId` nullable, `SnapshotRun`/itens e novos índices; backfill apenas quando owner e conexão forem comprovados.
- **Dual-read/write:** writers persistem ID e alias de apresentação; readers preferem ID, com fallback temporário ao alias apenas para registros migráveis.
- **Validação:** bloquear a fase contract diante de linha órfã, alias ambíguo ou divergência de contagem/hash; gerar relatório para correção humana.
- **Contract:** tornar `ConnectionId` obrigatório e retirar índices por alias somente após observação e backup validado.
- APIs atuais preservam `snapshotAt`, freshness e alias; podem ganhar `connectionId` e `snapshotRunId` opcionais. Não haverá contrato de métrica.

## Desempenho e confiabilidade

- A fila durável é fonte de trabalho; o channel em memória apenas acelera o processo local.
- Aplicar limite global configurável e limite por conexão/host, expondo profundidade de fila, latência, erro e throttling.
- Usar UPSERT ou recuperação explícita de unique violation para eliminar corrida read-then-insert.
- Renovar lease por heartbeat e recuperar somente após expiração; auditar worker por ID de instância.
- Definir orçamento de payload, paginação/chunking e cancelamento cooperativo; não duplicar payloads grandes em memória.
- Deduplicar por hash: payload inalterado atualiza freshness/run, sem reserialização ou escrita completa redundante.
- Limpar runs e erros técnicos por retenção configurável; head corrente só sai por regra explícita.

## Segurança, privacidade e observabilidade

- Runs não armazenam token, parâmetros secretos ou payload bruto; erros são sanitizados.
- Payload corrente permanece sujeito a menor privilégio, escopo da conexão e retenção de dados pessoais.
- Medir idade/profundidade da fila, duração/resultados de run/item, lease recuperado, bytes de payload, cache hit/stale, heads publicados, conflito de upsert e erro por conexão/dataset.

## Plano de execução

1. Inventariar datasets, contratos de freshness, chaves estáveis e orçamento por dataset.
2. Criar migrations expand e entidades de run/item com escrita transacional.
3. Migrar fila/store/locks para `ConnectionId`, dual-write e fallback controlado.
4. Substituir read-then-insert por UPSERT/retry limitado; publicar head atomicamente.
5. Implementar concorrência, heartbeat, deduplicação por hash e retenção configurável.
6. Fazer backfill, auditoria de órfãos e observação de desempenho antes da fase contract.

## Critérios de aceite

- [x] Renomear ou reutilizar alias não torna snapshot, sync state ou lease inacessível nem o associa à conexão errada.
- [x] Cada tentativa gera run/item técnico auditável, sem gravar payload histórico ou métricas de negócio.
- [x] Leitores nunca retornam head parcialmente escrito ou pertencente a run falho.
- [x] Duas instâncias convergem para um único head e estado de fila, sem duplicação ou erro não tratado.
- [x] Payload inalterado não causa escrita completa redundante; freshness e run são atualizados.
- [x] Limites globais e por conexão impedem fan-out ilimitado e são observáveis.
- [x] Migração não avança para contract enquanto existirem aliases ambíguos ou registros órfãos.

Implementado nesta onda: lineage `LastRunId`, diário de runs/itens, publicação transacional,
leases com recuperação somente após expiração, deduplicação por SHA-256, orçamento de payload,
paginação/chunking configuráveis, retenção e métricas exclusivamente operacionais. Nenhuma
métrica de desempenho/carga de tutores ou monitores foi adicionada.

## Validação e evidências

```powershell
dotnet test tests/MoodleConnector.Postgres.IntegrationTests --filter "FullyQualifiedName~Snapshot|FullyQualifiedName~Sync"
dotnet test tests/MoodleConnector.Application.Tests --filter "FullyQualifiedName~Snapshot|FullyQualifiedName~Sync"
```

## Rollout e rollback

Publicar em quatro releases: expand, dual-write/read, backfill com observação e contract. Cada fase conserva os heads existentes. Rollback desabilita readers/writers novos e preserva tabelas adicionadas; não apaga estado corrente nem executa migration destrutiva antes de validação operacional.
