# SPEC-0021: Contexto canônico e evidência versionada da correção assistida

## Status

In Progress.

## Objetivo

Fazer com que pré-validação, pacote para IA, revisão humana, preview, auditoria e decisão
consumam o mesmo contexto de correção versionado, com cobertura e origem preservadas do
artifact até a proposta final.

## Contexto e evidência atual

- O worker (`GradingContextBuilder`), a correção individual e o pacote de lote IA
  reconstruem contexto por caminhos diferentes.
- Critérios gerados pelo builder ficam em memória e não chegam necessariamente como
  `GradingEvidence` ao próximo estágio.
- `includeRubric` e `includeCourseMaterials` compartilham coleta de contexto, permitindo
  baixar materiais vizinhos quando apenas a rubrica foi solicitada.
- Metadados de origem úteis ao ranking (`sourceModuleType`, IDs, seção, título e distância)
  são achatados ou substituídos por posição do array.
- O extrator conhece `Truncated`, chunks e contagens, mas `GradingArtifact` não preserva
  cobertura suficiente para as etapas seguintes.
- `teacherInstructions` chega à tool/opções, mas não é persistido no lote e se perde antes
  do worker.
- O pacote IA limita texto a aproximadamente 3.000 caracteres, mesmo quando o extrator
  produziu chunks maiores; o estado de truncamento não bloqueia adequadamente a nota.

## Decisão e arquitetura-alvo

1. Após coleta de artifacts, um único `CanonicalGradingContextBuilder` produzirá e
   persistirá `GradingContextSnapshot` por item. Esse snapshot é um artefato operacional
   versionado de correção; não é histórico analítico de Moodle nem substitui a SPEC-0015.
2. O snapshot conterá, no mínimo:

   - `ItemId`, assignment ID/cmid tipados e versão;
   - nome da atividade e enunciado;
   - critérios com proveniência e pontuação;
   - rubrica e `RubricSource`;
   - `GradingScale`;
   - evidências de submissão e referências de artifacts;
   - status de extração, chunks, contagens e `EvidenceCoverage`;
   - metadados de origem, seção, módulo, título e distância;
   - `TeacherInstructions` persistidas;
   - warnings, blockers, `ReviewRequired` e `ContextHash`.

3. `AssignmentStatementCollector`, `FormalRubricCollector`,
   `NearbyCourseMaterialCollector` e `SubmissionCollector` terão responsabilidades e
   flags independentes. `includeRubric` não coletará materiais auxiliares por efeito
   colateral.
4. O snapshot será imutável após publicação; correções/novas coletas gerarão nova versão e
   novo hash. Worker, IA, UI, preview e auditoria referenciarão exatamente a versão usada.
5. A seleção heurística receberá metadados ricos, preservando a distinção entre
   `assignmentId`/`instanceId` e `cmid`/`ModuleId`. Fallback entre eles será removido ou
   ficará explícito em tipos diferentes.
6. A montagem do pacote IA usará chunks e referências por critério. Não haverá truncamento
   silencioso para 3.000 caracteres; limites serão declarados em `EvidenceCoverage` e
   poderão bloquear sugestão numérica.

## Escopo

- Modelo persistido e hash de `GradingContextSnapshot`.
- Unificação dos três builders existentes.
- Persistência de `teacherInstructions` e opções relevantes do lote.
- Separação de rubrica, enunciado, materiais próximos e submissão.
- Preservação de chunks, truncamento, coverage e metadados de origem.
- Identificadores Moodle tipados e seleção contextual determinística.
- Pacote IA e revisão apontando para a mesma versão do contexto.

## Fora de escopo

- Histórico analítico, métricas de tutores ou dashboard.
- Fila durável e processamento assíncrono, tratados na SPEC-0022.
- Contrato de segurança da proposta IA, tratado na SPEC-0020.
- Lançamento e reconciliação de nota, preservados pelas SPEC-0011 e SPEC-0012.

## Dependências e decisões em aberto

- Depende de SPEC-0012, SPEC-0015, SPEC-0020 e do schema atual de grading.
- Definir o limite máximo de chunks/tokens por modelo e a política de revisão quando a
  cobertura for parcial.
- Definir se snapshots de contexto serão mantidos por item ou compactados por lote com
  referências imutáveis.

## Contratos, compatibilidade e migração

- `GradingContext` atual será adaptado para leitura do snapshot durante uma janela de
  compatibilidade; novos fluxos não reconstruirão contexto fora do builder canônico.
- Lotes já existentes sem snapshot receberão `ContextStatus=legacy_unversioned` e exigirão
  nova preparação antes de uma proposta IA ou lançamento.
- `teacherInstructions` e opções serão adicionados ao `BatchConfiguration` de forma
  versionada, sem mudar o significado de lotes antigos.
- `ContextHash` será incluído em proposta, revisão, preview e auditoria; divergência entre
  versões bloqueará a confirmação.

## Segurança, privacidade e observabilidade

- Snapshot conterá somente o mínimo necessário, com referências a artifacts e retenção
  controlada; não duplicar payloads Moodle sem necessidade.
- Logs registram hash, versão, cobertura e status, nunca texto integral, anexos ou
  instruções do professor.
- Métricas agregadas devem distinguir contexto completo, parcial, bloqueado e legado.

## Plano de execução

1. Mapear os três pipelines e congelar um contrato comum de contexto.
2. Criar schema/entidade versionada para `GradingContextSnapshot` e `BatchConfiguration`.
3. Extrair collectors independentes e preservar metadados de ranking.
4. Persistir critérios e `teacherInstructions` junto ao contexto.
5. Migrar worker, prepare individual, prepare AI batch, UI, preview e auditoria para o
   snapshot por `ContextHash`.
6. Implementar seleção por chunks/coverage e bloquear truncamento não declarado.
7. Remover reconstruções legadas após evidência de equivalência e janela de compatibilidade.

### Incremento inicial implementado

O lote agora persiste `TeacherInstructions`, prioridade e as flags de inclusão de contexto
em migração aditiva; o worker/orquestrador local repassa essa configuração ao
`GradingContextBuilder`.
Isso fecha a perda de contexto entre criação e processamento sem antecipar o snapshot
canônico, a fila durável ou a coleta assíncrona previstos nas próximas etapas.
Além disso, a opção `includeCourseMaterials=false` agora impede a coleta e o download de
módulos vizinhos; a descrição da própria atividade continua disponível quando a rubrica foi
solicitada.

### Incremento de fundação implementado

Foi adicionado o contrato de `GradingContextSnapshot` como modelo de domínio imutável,
aditivo e independente do snapshot operacional da SPEC-0015. O contrato já representa
identificadores Moodle tipados (`assignmentId`, `cmid`, submissão e estudante), critérios
com proveniência, rubrica, escala, evidências, referências de artifacts, estado de extração,
cobertura, flags de coleta, warnings e bloqueadores. A publicação calcula um `ContextHash`
SHA-256 determinístico sobre o payload canônico; o timestamp operacional fica fora do hash e
as coleções são copiadas defensivamente.

Este incremento já publica um documento operacional append-only em
`grading_context_snapshot` e registra no item a identidade correspondente. Ainda não troca
os três consumidores legados para leitura exclusiva do snapshot; essa integração será feita
em etapa separada, com dual-read/dual-write e validação de equivalência, para não introduzir
divergência ou bloquear lotes antigos.

O worker e o orquestrador local agora adaptam o contexto montado para essa identidade e
persistem no item somente `ContextVersion`, `ContextHash` e `ContextStatus`. O texto da
submissão não é duplicado nessa coluna; continua nos artifacts sujeitos à retenção. Isso
permite detectar qual contexto foi usado sem antecipar a migração completa dos consumidores.

## Critérios de aceite

- [ ] Para um mesmo item, pré-validação, pacote IA, UI, preview e auditoria referenciam o
      mesmo `ContextHash` e versão.
- [ ] `teacherInstructions` persistem da criação do lote até worker, IA e revisão.
- [ ] `includeRubric=false` não coleta rubrica; `includeCourseMaterials=false` não baixa
      materiais vizinhos.
- [ ] Critérios gerados e seus pontos/proveniência aparecem no snapshot ou são declarados
      como não pontuáveis.
- [ ] O ranking preserva tipo/ID/seção/distância do módulo, sem confundir `cmid` com
      `assignmentId`.
- [ ] Truncamento, chunks, contagens e coverage sobrevivem à persistência e aparecem na
      revisão/auditoria.
- [ ] Conteúdo superior ao limite do modelo é particionado ou resulta em `ReviewRequired`,
      nunca em nota com cobertura omitida.
- [ ] Lote legado sem contexto versionado não pode gerar lançamento sem nova preparação.

## Validação e evidências

```powershell
dotnet test tests/MoodleConnector.Application.Tests --filter "FullyQualifiedName~GradingContextBuilder|FullyQualifiedName~HeuristicAssignmentContext|FullyQualifiedName~GradingAnalysis|FullyQualifiedName~AssistedGradingContextDiagnostics|FullyQualifiedName~MoodleGradingTools"
dotnet test MoodleConnector.slnx --configuration Release --no-build --no-restore
```

Evidências esperadas: teste de hash estável, equivalência entre pipelines, flags
independentes, propagação de instruções, metadados de origem, chunks/coverage e migração de
lote legado.

## Rollout e rollback

Introduzir snapshots em modo dual-read/dual-write, comparando hashes sem alterar lançamento.
Após equivalência validada, tornar o snapshot obrigatório para novas propostas. Rollback
retorna à leitura legada apenas para lotes antigos; propostas novas sem hash permanecem
bloqueadas, evitando decisões sem rastreabilidade.
