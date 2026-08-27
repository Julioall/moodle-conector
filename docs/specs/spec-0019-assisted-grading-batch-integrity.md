# SPEC-0019: Integridade, autorização e lifecycle dos lotes de correção

## Status

Draft.

## Objetivo

Garantir que um lote de correção assistida seja processado integralmente, somente por
usuários autorizados, com contadores e estados coerentes e com controle de concorrência
explícito antes de qualquer lançamento no Moodle.

## Contexto e evidência atual

- `GradingReviewRepository.ListItemsByBatchAsync` limita uma página a 100 itens.
- O worker e a prévia de lançamento possuem caminhos que tentam carregar um lote inteiro
  em uma única chamada; um lote configurado com até 400 itens pode deixar itens `Pending`.
- `CancelAssistedGradingBatchCommandHandler` precisa aplicar o mesmo
  `GradingAccessControl.EnsureCanAccessBatch` usado pelas consultas e edições.
- `ProcessedItems`, `BlockedItems`, `FailedItems` e `PendingItems` não possuem uma
  definição única documentada; há risco de subtração dupla.
- `ReadyItems` mistura rascunhos que aguardam revisão com itens efetivamente lançáveis,
  tornando `CanLaunch` permissivo.
- O agregado possui `Completed`, mas a transição final após todos os itens terminais não é
  uniforme.
- A edição usa `ExpectedReviewStatus`, embora já exista `GradingDraftVersionHash` para
  controle otimista mais forte.
- `Priority` é aceito no contrato, mas a fila FIFO não demonstra que altera a ordem.

## Decisão e arquitetura-alvo

1. Toda operação de lote completo usará uma primitive central `LoadAllBatchItemsAsync`,
   paginando internamente até consumir o conjunto ou retornar uma falha de cobertura.
2. Cancelamento, consulta, edição, revisão, auditoria e lançamento aplicarão a mesma
   autorização por criador, papel administrativo e permissões de grading. Respostas de
   lote inexistente e sem acesso não revelarão a existência do lote.
3. O agregado definirá contadores mutuamente exclusivos: `Total`, `Pending`, `Processing`,
   `AwaitingReview`, `DraftReady`, `Launchable`, `Committed`, `Blocked`, `Failed`,
   `ExecutionUnknown` e `Cancelled`. `Pending = Total - Processed` somente quando
   `Processed` for a união terminal documentada.
4. `CanLaunch` será verdadeiro somente quando `Launchable > 0`; `DraftReady` continuará
   distinguindo itens que exigem revisão humana.
5. O lifecycle do lote será derivado dos estados dos itens: nenhum item processado mantém
   `Pending`; trabalho ativo usa `Processing`; itens aguardando decisão usam
   `ReadyForReview`; todos os itens em estado terminal bem-sucedido usam `Completed`;
   cancelamento explícito usa `Cancelled`; falha irrecuperável do lote usa `Failed`.
6. Edições aceitarão `ExpectedDraftVersionHash` e usarão comparação e troca atômica. O
   status esperado permanece apenas como compatibilidade transitória, com depreciação
   documentada.
7. `Priority` só permanecerá público se a fila durável da SPEC-0022 implementar ordenação
   observável, limites de justiça e teste de prioridade. Caso contrário, será marcado como
   deprecated e removido em uma versão posterior, sem fingir que altera o processamento.

## Escopo

- Paginação completa de itens em worker, prévias, auditoria e handlers de lote.
- Autorização de cancelamento e testes cross-user.
- Recalculo de contadores e `CanLaunch`.
- Transições completas do aggregate até `Completed`/`Cancelled`/`Failed`.
- Concorrência otimista por hash de versão do rascunho.
- Decisão implementável sobre o parâmetro `Priority`.

## Fora de escopo

- Escala de notas, contrato da IA e prompt de conteúdo, tratados na SPEC-0020.
- Contexto canônico e ingestão de evidências, tratados na SPEC-0021.
- Fila PostgreSQL, leases, retenção e execução assíncrona, tratados na SPEC-0022.
- Alteração do mecanismo seguro de confirmação e lançamento Moodle da SPEC-0011.

## Dependências e decisões em aberto

- Depende de SPEC-0011, SPEC-0012 e SPEC-0022.
- Definir com produto se `Priority` será implementada ou deprecada antes da migração do
  contrato MCP.
- Confirmar quais estados são terminais para cada tipo de item, incluindo
  `ExecutionUnknown`.

## Contratos, compatibilidade e migração

- A assinatura pública dos handlers existentes será preservada durante a migração.
- `ExpectedDraftVersionHash` será adicionado como campo opcional inicialmente; quando
  presente, terá precedência sobre `ExpectedReviewStatus`.
- O retorno de status manterá os campos antigos e acrescentará contadores nomeados, com
  `CanLaunch` recalculado pela definição nova.
- A paginação pública continuará 1-based; a primitive interna poderá usar cursor/offset
  privado.
- Nenhum consumidor poderá cancelar lote de outro usuário apenas conhecendo o ID.

## Segurança, privacidade e observabilidade

- Falhas de autorização não expõem `CreatedBySubject`, estudantes, conteúdo ou existência
  de lote de terceiros.
- Registrar auditoria sanitizada para cancelamento permitido, negado e conflito de versão.
- Medir itens carregados, páginas percorridas, conflitos de versão e transições inválidas
  por `batchId` pseudonimizado ou `CorrelationId`, sem conteúdo de submissão.

## Plano de execução

1. Centralizar `LoadAllBatchItemsAsync` e substituir chamadas de página única nos fluxos de
   lote completo.
2. Aplicar `GradingAccessControl` ao cancelamento e cobrir proprietário, administrador,
   usuário sem acesso e usuário cross-user.
3. Formalizar o cálculo de contadores em uma função pura do aggregate e atualizar respostas
   MCP/portal.
4. Implementar transições terminais e atualizar o lifecycle depois de cada chunk persistido.
5. Adicionar CAS por `ExpectedDraftVersionHash`, mantendo compatibilidade durante a
   depreciação do status esperado.
6. Decidir e testar a semântica de `Priority` junto à SPEC-0022.

## Critérios de aceite

- [ ] Um lote de 400 itens é carregado e processado sem deixar itens `Pending` por causa do
      limite interno de 100.
- [ ] Prévia, revisão, auditoria e lançamento usam a mesma paginação completa.
- [ ] Usuário sem autorização não cancela lote de outro usuário e recebe erro estruturado
      sem vazamento de existência ou identidade.
- [ ] `Pending = Total - Processed` quando os conjuntos estão completos e não há dupla
      subtração de bloqueados/falhos.
- [ ] `CanLaunch` é falso para itens apenas `DraftReady` aguardando revisão e verdadeiro
      somente quando existe item `Launchable`.
- [ ] Um lote cujos itens chegaram a estados terminais bem-sucedidos chega a `Completed`.
- [ ] Duas edições concorrentes com o mesmo hash resultam em uma atualização aceita e um
      conflito explícito.
- [ ] `Priority` tem efeito testado ou está marcada como deprecated; não permanece como
      promessa sem implementação.

## Validação e evidências

```powershell
dotnet test tests/MoodleConnector.Application.Tests --filter "FullyQualifiedName~AssistedGradingBatch|FullyQualifiedName~GradingLaunch|FullyQualifiedName~PendingGradingRun|FullyQualifiedName~BackgroundGrading"
dotnet test MoodleConnector.slnx --configuration Release --no-build --no-restore
```

Evidências esperadas: testes unitários do aggregate, testes de autorização cross-user,
teste de lote acima de 100, teste de conflito de hash e contrato MCP/portal para contadores.

## Rollout e rollback

Publicar primeiro com contadores novos observáveis e escrita de hash compatível. Habilitar
transições de `Completed` e cancelamento protegido após os testes de homologação. Em caso de
regressão, desabilitar a edição por hash via configuração compatível e preservar os registros;
rollback nunca reabre ou relança automaticamente itens já enviados ao Moodle.
