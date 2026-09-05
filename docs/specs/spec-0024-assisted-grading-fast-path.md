# SPEC-0024: Caminho direto e read model da correção assistida

## Status

Implementação concluída; homologação de desempenho e rollout pendentes.

> Decisão posterior de produto: o caminho rápido exposto ao MCP salva primeiro as correções
> internamente e então oferece dois destinos mutuamente exclusivos: CSV externo ou prévia de
> publicação no Moodle. A prévia não exige UI; a escrita requer o texto literal
> `CONFIRMAR_PUBLICACAO` e mantém os guardrails de integridade e confirmação humana.

## Objetivo

Reduzir a latência e a fragilidade de rede da correção assistida sem remover seus guardrails
acadêmicos. A listagem e a ingestão devem consultar somente os dados Moodle necessários; a
revisão deve operar exclusivamente sobre dados persistidos; e qualquer escrita deve continuar
idempotente, auditável e condicionada à confirmação humana.

O alvo inicial é reduzir o fluxo observado de 79,2 segundos para 25–40 segundos, sem contar a
geração da IA, e trazer a abertura da revisão para perto do custo-base do banco, sem chamadas
Moodle no request da interface.

## Contexto e evidência atual

### Medição controlada

A medição foi feita sobre a `main` no commit `bcef179`, no curso `33446 — Fundamentos da
Indústria 4.0`, atividade com `cmid=1108049` e `assignmentInstanceId=117487`.

| Etapa | Duração observada | Evidência principal |
|---|---:|---|
| Listar pendências | 18,2 s | Caminho composto com até seis leituras Moodle sequenciais. |
| Criar lote | 10,7 s | Nova leitura de submissões quando o snapshot não é utilizável. |
| Preparar pacote de IA | 6,4 s | Aproximadamente `2N + 2` consultas ao banco. |
| Salvar rascunho | 8,0 s | Processamento item a item e recálculo integral de contadores. |
| Abrir revisão, primeira leitura | 35,9 s | Aproximadamente `4 + 7N` consultas ao banco e enriquecimentos Moodle. |
| Abrir revisão, cache parcialmente aquecido | 20,2 s | Ainda inclui enriquecimentos Moodle e consultas N+1. |
| Status do lote | 6,3 s | Aproximação do custo-base atual de infraestrutura/banco. |

`Listar pendências` e `Abrir revisão` representam 68,3% do tempo medido. A revisão pode chegar
a aproximadamente 354 consultas ao banco em uma página de 50 itens.

### Evidência funcional

- `mod_assign_get_assignments` resolve `cmid=1108049` para
  `assignmentInstanceId=117487`.
- A atividade retorna `grade=0`, que representa ausência de avaliação numérica. A revisão e
  o lançamento devem preservar `finalGrade=null` e publicar somente feedback.
- `mod_assign_get_submissions` retornou 16 entregas em 3,8 segundos, todas com
  `gradingstatus=notgraded`.
- As entregas usam arquivos DOCX/PDF, sem texto on-line. `core_files_get_files` expõe
  metadados e URL, mas não fornece os bytes necessários para extração.
- O gateway interno `IMoodleSubmissionFileGateway` já realiza download autenticado e valida
  limites. Essa capacidade ainda não possui uma primitiva MCP genérica e controlada.
- `moodle_prepare_write` e `moodle_confirm_write` já existem no código, protegidos por
  `UniversalMoodleWriteEnabled`, conexão `CanWrite`, allowlist, pending action e confirmação.
  Indisponibilidade em um ambiente deve ser tratada como configuração/capability, não como
  autorização para usar `moodle_execute_read` em funções de escrita.
- `GradingContextSnapshot` já persiste nome da atividade, enunciado, escala, evidências,
  coverage e `ContextHash`. A interface de revisão ainda reconstrói parte desse contexto.

### Causa estrutural

1. `MoodleGradingReviewAppTools` consulta detalhe e contexto item por item e busca curso,
   participantes e configurações Moodle durante a abertura.
2. `get_batch_grading_ui_state` repete o mesmo fluxo pesado após salvar, confirmar ou trocar
   de página.
3. `ListAssignmentSubmissionsQuery` executa curso, conteúdo, participantes, submissões,
   configuração e notas em série no fallback ao vivo.
4. O snapshot de submissões não declara cobertura suficiente do estado de avaliação para
   responder `NeedsGrading` de forma autoritativa.
5. Preparação de IA e salvamento de rascunhos usam operações N+1 no repositório.
6. Uma gravação local bem-sucedida pode ser seguida por uma leitura Moodle falha, fazendo a
   interface apresentar a operação inteira como falha.

## Decisão e arquitetura-alvo

### 1. Fluxo especializado com caminho direto

A correção assistida continua sendo o fluxo canônico de produção. O executor genérico será
mantido para diagnóstico e operações controladas, mas não substituirá validação de escala,
contexto versionado, revisão, pending action, idempotência, reconciliação e auditoria.

O caminho canônico será:

```text
resolver curso/atividade uma vez
  → ler submissões diretamente
  → persistir seleção e job
  → baixar/extrair anexos no worker com concorrência limitada
  → publicar GradingContextSnapshot
  → gerar e salvar propostas em lote
  → revisar usando somente PostgreSQL
  → preparar escrita e congelar a prévia
  → confirmar
  → executar escrita Moodle sem reconstruir curso/participantes/contexto
  → atualizar a UI pelo read model local
```

### 2. Read model local da revisão

Será criado um contrato `IGradingReviewReadStore` com uma operação paginada que devolve, de
forma autoritativa:

- resumo e contadores do lote;
- itens da página e hashes de versão;
- último `GradingContextSnapshot` publicado por item;
- nome da atividade, escala e modo de avaliação;
- proposta, feedback e decisão humana atuais;
- evidências resumidas e bloqueios;
- nomes persistidos do curso e do estudante, quando autorizados.

A implementação PostgreSQL deverá usar no máximo cinco comandos por página, com alvo de três:

1. lote, contadores e total;
2. página de itens com último snapshot/proposta por `LATERAL JOIN`, CTE ou projeção
   equivalente;
3. evidências dos IDs da página em uma consulta set-based.

`review_batch_feedbacks` e `get_batch_grading_ui_state` serão adaptadores do mesmo query
handler. Eles não poderão resolver credenciais, chamar gateways Moodle nem usar mediators que
terminem em uma leitura Moodle.

### 3. Identidade e nomes persistidos

- `cmid` e `assignmentInstanceId` permanecem campos distintos e nunca são intercambiados
  implicitamente depois da resolução inicial.
- Nome do curso e nome autorizado do estudante serão capturados durante descoberta/ingestão e
  persistidos como metadados de exibição. Esses campos são opcionais e sujeitos à política de
  privacidade.
- Nome da atividade, enunciado e escala vêm do `GradingContextSnapshot`; a interface não volta
  ao Moodle para enriquecê-los.
- Lotes legados sem metadados usam IDs como fallback e não fazem enriquecimento síncrono.

### 4. Listagem direta e snapshot de avaliação

O snapshot de submissões será estendido para declarar, por atividade:

- `assignmentInstanceId` e `courseModuleId`;
- modo de avaliação: `numeric`, `scale`, `feedback_only` ou `unknown`;
- nota máxima quando numérica;
- `gradingStatus`, nota existente observável e data da leitura por submissão;
- cobertura e completude separadas para participantes, submissões, configuração e notas.

`NeedsGrading` poderá usar o snapshot somente quando a cobertura necessária estiver completa.
Caso contrário, o fallback ao vivo executará participantes, submissões e configurações em
paralelo depois de resolver curso/atividade uma única vez. Falha parcial nunca será convertida
em lista vazia ou `NoPendingSubmissions`.

### 5. Ingestão e download autenticado

O worker reutilizará `IMoodleSubmissionFileGateway` com concorrência limitada por conexão e
por lote. O default inicial será quatro downloads concorrentes, configurável, respeitando
limites de tamanho, timeout total e cancelamento.

Uma primitiva opcional `moodle_download_file` poderá expor a mesma capacidade para diagnóstico
e extração controlada. Seu contrato deverá:

- aceitar somente URL emitida pela conexão Moodle ativa;
- exigir host igual ao `BaseUrl` resolvido e caminho Moodle permitido, como
  `pluginfile.php`/`webservice/pluginfile.php`;
- rejeitar redirecionamento para outro host, URL com userinfo e esquemas não HTTPS em produção;
- aplicar allowlist de MIME, limite de bytes e timeout total;
- autenticar no servidor sem retornar ou persistir token;
- devolver metadados, SHA-256 e conteúdo como resource/blob MCP, nunca base64 dentro do JSON
  estruturado ou logs;
- auditar função, host sanitizado, tamanho, hash, duração e resultado, sem conteúdo acadêmico.

Falha de download ou extração bloqueia a avaliação daquele item. O sistema não gera feedback
como se tivesse lido um arquivo indisponível.

### 6. Preparação e salvamento em lote

- O pacote de IA será montado por `IGradingAiPackageReadStore`, carregando itens, snapshots e
  evidências em operações set-based.
- O salvamento de revisões validará todas as entradas primeiro, persistirá itens e auditoria
  em uma unidade de trabalho e recalculará contadores por agregação set-based.
- O retorno da gravação conterá as linhas atualizadas para a UI reconciliar imediatamente.
- Falha do refresh posterior será apresentada como falha de atualização da tela, sem reclassificar
  a gravação concluída como falha.

### 7. Escrita Moodle

As escritas específicas de correção permanecem sob as SPEC-0011, SPEC-0012, SPEC-0019 e
SPEC-0020. A prévia será preparada a partir do snapshot e da versão do rascunho persistidos.

- A confirmação não refaz leituras de curso, participantes, enunciado ou escala.
- Quando uma validação remota for indispensável, ela ocorrerá antes da pending action em uma
  operação em lote e sua idade máxima será declarada na prévia.
- Atividade `feedback_only` usa `finalGrade=null`; `0` não representa nota acadêmica.
- Escrita não recebe retry cego. Timeout/queda após envio produz `ExecutionUnknown` e exige
  reconciliação.
- `mod_assign_save_grades` poderá ser habilitado em chunks somente após testes de erro parcial,
  idempotência e reconciliação. Até lá, o executor específico individual permanece válido.
- O executor universal existente continua desabilitado por padrão. Quando habilitado, usa
  allowlist, schemas tipados por função, feature flag, conexão `CanWrite`, escopo, preview,
  confirmação literal e pending action. Ele não é fallback automático da correção assistida.

### 8. Observabilidade por fase

Cada operação registrará uma árvore de fases, sem PII:

- duração total e por fase;
- quantidade de comandos SQL;
- funções Moodle e quantidade de chamadas;
- tentativas e tempo gasto em retry;
- cache/snapshot hit, idade e coverage;
- itens e bytes processados;
- fila, download, extração, contexto, revisão, preview e escrita;
- resultado `success`, `partial_failure`, `error` ou `execution_unknown`.

Os contadores serão correlacionados por `batchJobId`, `auditId` e `correlationId`, sem registrar
texto de entrega, feedback, token ou URL autenticada.

## Escopo

- Read model PostgreSQL da interface de revisão.
- Queries set-based para revisão, pacote de IA e salvamento de rascunhos.
- Reuso do `GradingContextSnapshot` na UI.
- Persistência controlada de nomes de exibição.
- Caminho direto para atividade e submissões.
- Snapshot de submissões com cobertura de avaliação.
- Downloads autenticados concorrentes no worker.
- Primitiva controlada `moodle_download_file` como capacidade complementar.
- Ativação/hardening documentado das tools universais de escrita já existentes.
- Instrumentação interna e gates de desempenho.

## Fora de escopo

- Substituir a correção assistida por chamadas Moodle cruas.
- Escrita Moodle sem prévia e confirmação humana.
- Retry automático de função de escrita.
- Gerar feedback sem conteúdo legível da entrega.
- Armazenar token Moodle ou URL autenticada em snapshot, artifact, log ou payload da UI.
- Alterar critérios acadêmicos, política pedagógica ou autonomia da IA.
- Garantir SLA do tempo de geração do modelo de IA.
- Tornar `moodle_prepare_write` visível quando feature flag, conexão ou scope não autorizarem.

## Dependências e decisões em aberto

- Depende das SPEC-0011, SPEC-0012, SPEC-0014, SPEC-0015 e SPEC-0019–0022.
- Definir retenção dos nomes de exibição e se o nome do estudante deve integrar o contexto
  canônico ou somente o read model.
- Definir limite institucional de arquivo e MIME types permitidos na primitiva genérica.
- Validar suporte real e semântica de erro parcial de `mod_assign_save_grades` no Moodle alvo.
- Definir idade máxima da validação remota congelada na prévia; proposta inicial: cinco minutos.
- Calibrar concorrência de downloads por conexão com testes de carga; default inicial: quatro.

## Contratos, compatibilidade e migração

### Contrato de leitura da revisão

`GradingReviewAppData` será mantido de forma aditiva. Campos novos:

- `dataSource=local_read_model`;
- `readModelVersion`;
- `contextHash` e `draftVersionHash` por item;
- `gradingMode` e `maxGrade` anulável;
- `coverage` e `warnings` locais;
- `queryCount` somente em ambiente diagnóstico.

Clientes existentes continuarão recebendo os campos atuais. `maxGrade=0` deixa de ser usado
como sinal ambíguo em contratos novos; `gradingMode=feedback_only` e `maxGrade=null` serão a
representação canônica.

### Migração de dados

1. Adicionar metadados de exibição e versão do read model por migração aditiva.
2. Preencher novos lotes durante descoberta/ingestão.
3. Projetar lotes existentes a partir do último `GradingContextSnapshot` quando possível.
4. Marcar metadados ausentes como `unknown`; não consultar Moodle durante leitura da UI.
5. Ativar dual-read e comparar resposta antiga/nova em shadow mode.
6. Remover o read path antigo somente após os gates de equivalência e desempenho.

### Compatibilidade das tools universais

- `moodle_execute_read` continua rejeitando funções classificadas como escrita.
- `moodle_prepare_write`/`moodle_confirm_write` preservam nomes e confirmação em duas etapas.
- `moodle_download_file` será aditiva, desabilitada por flag e não aceitará URLs arbitrárias.
- Tools específicas continuam preferenciais nas skills e no catálogo cognitivo.

## Segurança, privacidade e observabilidade

- Toda leitura do read model aplica ownership/escopo administrativo do lote.
- Nomes de estudantes não aparecem em logs ou métricas e respeitam a capability/visibilidade
  da fonte Moodle.
- Conteúdo de arquivo permanece dentro do pipeline de extração e da política de retenção da
  SPEC-0022.
- URLs são normalizadas antes da persistência; token só é anexado em memória pelo gateway.
- Download impede SSRF por validação de conexão, host, esquema, porta, redirects e tamanho.
- Escritas exigem scopes de domínio, conexão `CanWrite`, allowlist, feature flag, preview,
  confirmação literal, idempotência e auditoria.
- Métricas de desempenho não contêm texto acadêmico, feedback ou identificadores nominais.

## Plano de execução

1. Congelar baseline, adicionar instrumentação de fases e contagem SQL/Moodle.
2. Implementar `IGradingReviewReadStore` e query paginada set-based.
3. Migrar as duas tools da interface para o read model local.
4. Persistir nomes de exibição e eliminar enriquecimentos Moodle da revisão.
5. Implementar package/save em lote e reconciliação local da UI.
6. Estender snapshot de submissões com modo/estado/cobertura de avaliação.
7. Implementar o caminho direto por MCP Resource.
8. Remover download/extraction do worker de correção; manter o gateway de resource como leitura lazy e segura.
9. Expor `moodle_download_file` atrás de feature flag e testes de segurança.
10. Certificar o executor universal de escrita existente sem torná-lo fallback automático.
11. Executar shadow mode, carga, fault injection, rollout gradual e remoção do read path antigo.

O detalhamento operacional está em
[Plano de implementação — caminho rápido da correção assistida](../plans/assisted-grading-fast-path.md).

## Critérios de aceite

- [x] `review_batch_feedbacks` e `get_batch_grading_ui_state` executam zero chamadas Moodle.
- [x] Uma página de 50 itens usa no máximo cinco comandos SQL; alvo de três comandos.
- [ ] Abertura da revisão possui p95 menor ou igual a 10 s no ambiente da medição e p50 menor
      ou igual ao custo-base mais 2 s.
- [x] Falha de rede após salvar/confirmar não transforma gravação concluída em falha da operação.
- [x] Pacote de IA para 50 itens não executa N+1 e usa no máximo cinco comandos SQL, exceto
      escrita explícita de métricas.
- [x] Salvamento de 50 revisões é atômico por item, set-based, idempotente e não recarrega o
      lote item a item.
- [ ] Listagem direta de uma atividade possui p95 menor ou igual a 6 s quando não há retry.
- [x] Snapshot responde `NeedsGrading` somente com coverage completo; incompletude retorna
      `partial_failure`/`unknown`, nunca lista vazia autoritativa.
- [ ] `cmid` e `assignmentInstanceId` são resolvidos uma vez e persistidos separadamente.
- [x] Downloads de DOCX/PDF usam concorrência limitada, validam host/tamanho e não expõem token.
- [x] Item cujo arquivo não foi lido não recebe proposta de feedback apresentada como avaliação.
- [x] Atividade sem nota persiste `finalGrade=null`, exibe `feedback_only` e não publica nota 0.
- [ ] Confirmação não repete consultas de curso, participantes, enunciado ou escala.
- [x] Nenhuma escrita usa retry cego; resultado remoto desconhecido entra em `ExecutionUnknown`.
- [ ] Fluxo completo, sem geração da IA, fica entre 25–40 s no cenário de referência.
- [ ] Métricas expõem fase, query count, funções Moodle, retries, cache/snapshot hit e coverage,
      sem PII ou conteúdo acadêmico.

## Validação e evidências

```powershell
dotnet test tests/MoodleConnector.Application.Tests --filter "FullyQualifiedName~GradingReviewReadStore|FullyQualifiedName~MoodleGradingReviewApp|FullyQualifiedName~GradingBatch"
dotnet test tests/MoodleConnector.Application.Tests --filter "FullyQualifiedName~MoodleSubmissionFileGateway|FullyQualifiedName~MoodleUniversalWrite"
dotnet test tests/MoodleConnector.Application.Tests --filter "FullyQualifiedName~MoodleSnapshotPostgresIntegration|FullyQualifiedName~GradingBatchJobPostgresIntegration"
dotnet test MoodleConnector.slnx --configuration Release --no-build --no-restore
```

Além dos testes, a homologação deve anexar:

- trace de abertura de uma página de 50 itens comprovando zero chamadas Moodle e query count;
- comparação shadow do DTO antigo e do read model;
- medição p50/p95 das etapas no cenário de referência;
- teste com cache frio, retry transitório e falha permanente;
- teste de redirect para host não autorizado, arquivo acima do limite e token sanitizado;
- teste de timeout de escrita com reconciliação e ausência de reenvio automático;
- verificação manual de atividade numérica com nota 0 e atividade `feedback_only` com
  `finalGrade=null`.

## Rollout e rollback

Flags propostas:

- `GradingFastReadModelEnabled`;
- `GradingDirectDiscoveryEnabled`;
- `GradingParallelDownloadsEnabled`;
- `UniversalMoodleFileDownloadEnabled`;
- `UniversalMoodleWriteEnabled` já existente.

O rollout começa em shadow mode, comparando DTOs e métricas sem alterar a resposta. Depois,
habilita-se o read model para usuários internos, seguido do caminho direto e dos downloads
paralelos. As primitives genéricas permanecem desabilitadas até certificação de segurança.

Em rollback, desabilitar as flags de novos caminhos, preservar jobs, snapshots, artifacts,
pending actions e auditoria. Nunca reexecutar uma escrita durante rollback. Migrações serão
aditivas; colunas novas permanecem até um release posterior.
