# SPEC-0022: Jobs duráveis, ingestão assíncrona e retenção da correção assistida

## Status

In Progress.

## Objetivo

Transformar a correção assistida em um job recuperável e seguro para grandes lotes,
independente de uma única réplica, sem manter trabalho pesado no request e sem reter texto
acadêmico além da política aprovada.

## Contexto e evidência atual

- `CreateAssistedGradingBatch` baixa arquivos, extrai conteúdo e persiste artifacts de forma
  aguardada antes de devolver o lote; com 400 estudantes e vários arquivos, o request pode
  permanecer ocupado por milhares de downloads.
- `GradingBatchChannel` é um `Channel` em memória com `SingleReader=true`; a propriedade é
  por processo, não global.
- No startup, o reencaminhamento considera `Processing`, mas um processo pode cair após
  persistir `Pending` e antes de enfileirar o trabalho.
- Não há claim/lease transacional no repositório de grading para impedir duas réplicas de
  processar o mesmo lote.
- `FileDownloadWorkers`, `ExtractionWorkers` e `AiAnalysisWorkers` existem como knobs,
  porém o fluxo de criação ainda concentra trabalho no request.
- `RawFileRetentionDays` e `DraftRetentionDays` estão configurados, mas não há evidência
  de cleanup efetivo. `ExtractedTextRef` pode conter texto integral da entrega.
- A prioridade declarada agora é persistida e participa da ordem inicial de claim durável;
  aging/fairness e limites de starvation ainda ficam para a Fase 4.
- O processamento persiste estados em conjuntos grandes, ampliando a janela de perda e de
  duplicação após falha.

## Decisão e arquitetura-alvo

1. O request de criação resolverá submissões, validará acesso, persistirá `BatchConfiguration`
   e itens `Pending`, criará o registro de job e retornará `batchJobId`. Download, extração,
   contexto e pré-validação serão executados pelo worker.
2. O PostgreSQL será a fila durável: cada job/item terá `Status`, `LeaseOwner`, `LeaseUntil`,
   `AttemptCount`, `NextAttemptAt`, `Priority`, `LastErrorCode` e timestamps. Claim será
   transacional e compatível com duas réplicas (`FOR UPDATE SKIP LOCKED` ou equivalente).
3. Startup recuperará `Pending` e `Processing` com lease expirado. Um lease renovável e
   heartbeat evitarão que trabalho longo seja reclamado prematuramente.
4. Cada item será processado em chunks persistidos com operação idempotente. Repetição de
   job poderá repetir apenas trabalho interno não publicado; nenhum worker relança uma nota
   Moodle confirmada.
5. Limites de concorrência serão aplicados no worker, não no request. `Priority` terá
   ordenação observável, aging/fairness e testes; se isso não for aceito, será removida do
   contrato antes do rollout.
6. Um cleanup agendado aplicará `RawFileRetentionDays` e `DraftRetentionDays`, apagando
   bytes/texto bruto elegível e preservando hashes, estados, coverage, auditoria e
   referências mínimas necessárias. Falhas de cleanup serão observáveis e não apagarão
   evidência legalmente necessária.
7. A fila durável será complementar ao canal local: o canal pode reduzir latência, mas nunca
   será a única fonte de recuperação.

## Escopo

- Criação leve e enfileiramento persistido.
- Claim/lease/checkpoint de trabalho interno com limites e backoff.
- Recuperação de `Pending` e leases expirados no startup.
- Processamento em chunks com checkpoint por item.
- Concorrência multi-réplica e prioridade real.
- Cleanup de artifacts/texto conforme retenção.
- Métricas e alertas de backlog, idade, lease, tentativas e cleanup.

## Fora de escopo

- Retry ou reenvio automático de escritas Moodle.
- Contrato de escala/IA e contexto canônico das SPEC-0020/0021.
- Dashboard de desempenho de tutores.
- Substituição do mecanismo de confirmação e reconciliação da SPEC-0011.

## Dependências e decisões em aberto

- Depende de SPEC-0017, SPEC-0019, SPEC-0020 e SPEC-0021.
- Definir limites de tamanho, timeout e número máximo de tentativas por etapa.
- Definir storage externo para arquivos grandes quando PostgreSQL não for adequado.
- Confirmar política institucional/legal de retenção mínima de evidência de avaliação.

## Contratos, compatibilidade e migração

- `create_assisted_grading_batch` mantém o retorno `batchJobId`, mas passa a retornar antes
  da ingestão pesada, com `status=Pending` e cobertura inicial declarada.
- O canal atual permanecerá como adaptador durante a migração; publicação do job no banco
  ocorrerá antes do enqueue em memória.
- Jobs antigos sem lease receberão claim seguro na primeira leitura; jobs órfãos serão
  reprocessados somente em etapas internas idempotentes.
- Cleanup não remove registros de auditoria ou ações pendentes e não toca snapshots da
  SPEC-0015.

## Segurança, privacidade e observabilidade

- Claim exige conexão, capability, autorização de lote e ownership; lease não substitui
  autorização.
- Nunca registrar conteúdo de arquivo, token Moodle, prompt ou feedback integral em logs.
- Métricas: backlog por estado/prioridade, idade do item, duração por etapa, tentativas,
  leases expirados, duplicações evitadas, bytes retidos e cleanup falho.
- Alertas para backlog envelhecido, jobs repetidamente falhos, leases órfãos e retenção
  vencida.

## Plano de execução

1. Modelar estado/lease/checkpoint e migration PostgreSQL.
2. Implementar claim transacional e recuperação no startup.
3. Tornar criação do lote leve e mover download/extração/contexto ao worker.
4. Persistir por chunks, limitar concorrência e implementar prioridade/fairness.
5. Adicionar teste de duas réplicas e de queda entre `Pending` e enqueue.
6. Implementar cleanup real, dry-run, métricas e alertas.
7. Migrar gradualmente do channel como fonte primária para o job store.

### Incremento inicial implementado

O lote passou a ter estado durável de execução (`LeaseOwner`, `LeaseUntil`,
`AttemptCount`, `NextAttemptAt`, `LastErrorCode` e `CheckpointItemId`). O repositório possui
claims condicionais com ordenação por prioridade, renovação, liberação, checkpoint e
recuperação de leases expirados em migração aditiva. O channel permanece como acelerador; o worker faz polling do job store e
continua aceitando itens enfileirados pelo fluxo legado.

Este incremento ainda não move a ingestão pesada para fora do request, não implementa leases
por item nem cleanup de retenção; essas entregas permanecem nas etapas seguintes.

## Critérios de aceite

- [ ] Criar um lote de 400 itens não executa download/extraction no request e retorna um
      `batchJobId` recuperável.
- [ ] Queda entre persistência `Pending` e enqueue não deixa o lote órfão após restart.
- [ ] Duas réplicas não processam o mesmo item simultaneamente; lease expirado é retomado
      com tentativa contabilizada.
- [ ] Processamento parcial retoma do último checkpoint sem duplicar artifacts ou propostas.
- [ ] `Priority` altera a ordem de claim sob carga e mantém fairness; ou o parâmetro é
      deprecado/removido antes do release.
- [ ] Cleanup remove dados elegíveis conforme retenção, preserva auditoria e é testado com
      dry-run e falha parcial.
- [ ] Backlog, idade, tentativas, leases e retenção possuem métricas sem PII.
- [ ] Testes PostgreSQL cobrem constraints, claim concorrente, restart, cleanup e
      idempotência.

## Validação e evidências

```powershell
dotnet test tests/MoodleConnector.Application.Tests --filter "FullyQualifiedName~BackgroundGrading|FullyQualifiedName~GradingBatchChannel|FullyQualifiedName~GradingReviewRepository|FullyQualifiedName~MoodleSnapshotPostgresIntegration"
dotnet test MoodleConnector.slnx --configuration Release --no-build --no-restore
```

No CI, executar a suíte PostgreSQL da SPEC-0017 com serviço efêmero e anexar evidência de
duas instâncias concorrentes, recuperação após restart e limpeza por retenção.

## Rollout e rollback

Executar inicialmente em shadow mode: persistir claims/checkpoints sem mover todos os lotes
para o worker durável. Habilitar por feature flag para novos lotes, mantendo o canal como
fallback de leitura. Em rollback, pausar novos claims, deixar leases expirarem e drenar o
canal; não apagar jobs, artifacts ou auditoria e não reenviar escritas Moodle.
