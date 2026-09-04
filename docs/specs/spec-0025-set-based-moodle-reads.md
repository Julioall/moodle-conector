# SPEC-0025: Leituras amplas e snapshots analíticos por curso

## Status

Implementado (fases 1–3, rollout pendente). Depende de SPEC-0013, SPEC-0015 e SPEC-0017;
complementa o caminho de leitura da SPEC-0024 sem alterar suas garantias de correção assistida.

As primitivas set-based, o snapshot persistido de gradebook, o coordenador de leitura por
requisito e a migração dos consumidores analíticos já estão implementados. O payload novo do
gradebook persiste `Items[]` e `StudentGrades[]` deduplicados e reconstrói o índice por estudante
para compatibilidade; heads legados continuam legíveis. A promoção do bulk como caminho padrão
por conexão ainda depende do canário de capability/volume e das evidências de equivalência
descritas na fase 4.

## Objetivo

Reduzir chamadas Moodle repetidas nos fluxos de leitura e relatório, substituindo fan-out por
estudante ou atividade por leituras amplas, chunking controlado e cruzamento local. Em cache
quente, tools que consultam o mesmo curso devem reutilizar os mesmos heads de snapshot; em
cache frio, o custo normal deve ser proporcional ao número de datasets e chunks, não ao número
de estudantes.

Esta spec preserva os contratos MCP de alto nível. A mudança ocorre nos gateways, no
sincronizador e nos read models consumidos pelas tools.

## Veredito de viabilidade

A mudança é viável como evolução da arquitetura existente, com uma ressalva de capability e
volume para o gradebook coletivo.

Já existem no repositório:

- heads duráveis e atômicos em `moodle_snapshots`, cache L1, freshness, stale window e lineage;
- datasets `courses`, `activities`, `students`, `groups` e `submissions`;
- fila durável, leases, concorrência por conexão, deduplicação por hash e limite de payload;
- leitura snapshot-first nas tools de cursos, conteúdos, participantes e submissões;
- chamadas em lote para `mod_assign_get_submissions`, com até 50 assignments por request e
  fallback isolado quando um ID invalida o lote.

As lacunas verificadas são:

- `IMoodleGradebookGateway` expõe somente `GetStudentGradebookAsync` e usa
  `gradereport_user_get_grade_items` com um `userid`; relatórios fazem uma chamada por aluno;
- o cache atual do gradebook é apenas em memória, por curso e estudante, com duração de 24
  horas; não é compartilhado entre réplicas, não declara cobertura e não compõe um head do curso;
- o sincronizador de submissões consulta `mod_assign_get_grades` uma vez por assignment,
  embora a função aceite uma coleção de `assignmentids`;
- o snapshot de participantes lê uma página de até 1.000 registros e marca `HasMore`, mas não
  completa páginas adicionais;
- não há evidência MoodleBench versionada para o modo coletivo do gradebook nas conexões-alvo.

### Correção da hipótese sobre grades

`core_grades_get_gradeitems(courseid)` não é fonte de notas por estudante. No contrato oficial
ele devolve IDs, nomes e categorias dos itens avaliativos e exclui itens de categoria e total do
curso. Pode ser usado futuramente para metadados ou estimativa de volume, mas não substitui o
gradebook usado pelos relatórios.

O candidato correto é o endpoint já usado pelo conector:

```text
gradereport_user_get_grade_items(courseid, userid=0, groupid=0)
```

No contrato oficial, `userid=0` devolve os `usergrades` dos usuários visíveis no relatório. Esse
modo exige `gradereport/user:view` e `moodle/grade:viewall`, e o modo de grupos pode restringir a
população retornada. Portanto, disponibilidade da função não prova cobertura completa: o
sincronizador deve reconciliar os IDs retornados com o snapshot de participantes ativos.

Referências primárias:

- [Implementação oficial de `gradereport_user_get_grade_items`](https://github.com/moodle/moodle/blob/MOODLE_405_STABLE/grade/report/user/classes/external/user.php)
- [Implementação oficial de `core_grades_get_gradeitems`](https://github.com/moodle/moodle/blob/MOODLE_405_STABLE/grade/classes/external/get_gradeitems.php)
- [Contratos oficiais de `mod_assign_get_grades` e `mod_assign_get_submissions`](https://github.com/moodle/moodle/blob/MOODLE_405_STABLE/mod/assign/externallib.php)

## Contexto e evidência atual

### Cursos

`MoodleCoursesGateway` já carrega `core_enrol_get_users_courses` uma vez, pagina localmente e
protege o cold start com single-flight em memória. `MoodleCoursesTools` prefere o dataset
`courses` para listar, pesquisar e resolver curso. Assim, o caso dos 402 cursos já segue a
direção proposta: um refresh amplo e operações locais subsequentes. A spec torna esse
comportamento um gate explícito para evitar regressão.

### Estrutura, participantes e submissões

`MoodleSnapshotSyncQueue` já materializa conteúdo, participantes, grupos e submissões por curso.
`MoodleAssignmentSubmissionsGateway` envia vários `assignmentids` por request. O ponto ainda
proporcional ao número de assignments é a leitura de notas existentes em
`MoodleAssignmentGradeReadGateway`.

### Gradebook e relatórios

Os seguintes fluxos percorrem participantes e chamam `GetStudentGradebookAsync` dentro do loop:

- `GetStudentsBelowMinGradeQueryHandler`;
- `GenerateCourseGradesReportQueryHandler`;
- `GenerateWeeklyPerformanceReportQueryHandler`;
- `GenerateClassCouncilReportQueryHandler`;
- `GetStudentsAtRiskReportQueryHandler`;
- relatórios derivados e fallbacks de submissões que usam o mesmo gateway.

Para `S` estudantes, esses fluxos fazem hoje até `S` chamadas de gradebook, além da leitura de
participantes e de outros datasets. Falhas são frequentemente capturadas por estudante, mas os
resultados não compartilham um contrato uniforme de população solicitada, retornada e ausente.

## Decisão e arquitetura-alvo

### 1. Leitura ampla é set-oriented e orientada por requisito

Não será criado um objeto monolítico que sempre baixa todo o curso. O snapshot lógico de uma
operação será composto, sob demanda, por heads independentes:

```text
CourseReadSnapshot
├── courses       — resolução e metadados do curso
├── activities    — seções e módulos
├── students      — participantes ativos
├── submissions   — entregas e coverage de avaliação
├── gradebook     — itens e notas por estudante
└── groups        — somente quando o fluxo exigir
```

Cada head mantém `UpdatedAt`, `FreshUntil`, `StaleUntil`, `IsComplete`, `RecordCount`, hash e
`SnapshotRunId` próprios. O snapshot lógico declara a idade e a consistência de cada componente;
não inventa atomicidade entre chamadas Moodle independentes.

`completion` não entra no fast path desta spec. As funções nativas de completion usadas pelo
projeto recebem um estudante por chamada; quando um fluxo realmente exigir completion, o custo
deve ser declarado e limitado, sem prometer uma leitura ampla inexistente.

### 2. Novo dataset `gradebook`

Adicionar um head por `(ownerId, connectionId, gradebook, courseId)`. O payload canônico evita
duplicar a definição do item para cada aluno:

```text
CourseGradebookSnapshot
├── CourseId
├── Items[]
│   └── GradeItemId, ItemName, ItemType, ItemModule, ItemInstance,
│       CourseModuleId, CategoryId, GradeMin, GradeMax
├── StudentGrades[]
│   └── StudentId, GradeItemId, GradeRaw, GradeFormatted,
│       Percentage, Feedback, FeedbackFormat, dates, GraderId
└── Coverage
    └── SourceMode, RequestedStudentCount, ReturnedStudentCount,
        RequestedStudentIdsHash, MissingStudentIds, ErrorStudentIds,
        ReturnedStudentIds, WarningCount,
        IsComplete, Truncated, PayloadBytes
```

O índice local primário é `(StudentId, GradeItemId)`. O projetor recompõe `CourseGradebook` para
compatibilidade com handlers individuais. Item ausente, item presente sem nota e usuário não
retornado são estados distintos.

`MissingStudentIds` contém somente IDs Moodle necessários para diagnóstico e fallback; nomes,
emails e conteúdo de feedback não entram em logs ou métricas.

### 3. Estratégia capability-driven para o gradebook

Ordem de leitura no refresh:

1. Exigir um snapshot completo de participantes ativos ou completar sua paginação.
2. Estimar células como `participantes × itens` quando a contagem de itens estiver disponível;
   na ausência dela, aplicar ao menos o limite de participantes.
3. Se a função, as permissões e os limites permitirem, chamar
   `gradereport_user_get_grade_items` uma vez com `userid=0`.
4. Reconciliar `usergrades[].userid` com os participantes ativos visíveis.
5. Se houver bloqueio por capability, timeout, payload, grupos ou divergência de população,
   usar o caminho individual existente somente para os IDs não cobertos, com concorrência e
   deadline limitados.
6. Publicar o head como completo apenas quando todos os participantes solicitados estiverem
   classificados como retornados, legitimamente fora do escopo ou falhos de forma explícita.

O fallback não pode converter falha de permissão, timeout ou truncamento em “sem nota”. A origem
deve ser `bulk`, `individual_fallback` ou `mixed`.

Defaults iniciais, ajustáveis após benchmark:

- `BulkGradebookEnabled=true` nesta fase, após validação unitária; recomenda-se manter o
  canário operacional antes de ampliar os limites;
- `MaxBulkGradebookStudents=200`;
- `MaxBulkGradebookCells=20_000`;
- `IndividualGradebookConcurrency=4`;
- `GradebookFreshMinutes=15`;
- `GradebookStaleMinutes=120`;
- `MaxAnalyticalSnapshotSkewMinutes=15`.

Esses limites são proteção operacional, não critérios pedagógicos e não devem truncar uma
população silenciosamente.

### 4. Batching de notas de assignments

Evoluir `IMoodleAssignmentGradeReadGateway` com uma leitura que receba vários assignment IDs e
retorne resultado por assignment, incluindo warnings/falhas individualizadas. A implementação
envia `assignmentids[]` em chunks configuráveis, inicialmente 50, e filtra os estudantes
localmente.

Quando um lote falhar ou omitir um assignment, repetir somente os IDs não cobertos em lotes
menores ou individualmente, com concorrência limitada. O sincronizador de submissões deixa de
criar uma task Moodle por assignment no caminho normal.

Implementado: `MoodleAssignmentGradeReadGateway` envia `assignmentids[index]` em lotes de até 50
(configurável por `MoodleSnapshots:AssignmentGradeBatchSize`), reprocessa apenas assignments
omitidos e retorna `AssignmentGradesBatch` com erro por assignment.

### 5. Um coordenador de leitura para as tools analíticas

Adicionar uma porta de Application para solicitar um `CourseReadSnapshot` por conjunto de
requisitos, com adapter no host que resolve owner, conexão e curso usando o contexto já existente.
Esse coordenador:

- lê os heads necessários;
- agenda refresh quando ausentes ou stale;
- permite live fallback somente conforme a política da operação;
- cria índices locais uma vez por request;
- devolve freshness, coverage e skew junto aos dados.

Handlers de negócio continuam responsáveis pelos cálculos. Eles deixam de conhecer a mecânica de
chamadas Moodle por aluno e passam a receber um read model completo ou explicitamente parcial.

Nesta implementação, os handlers analíticos recebem heads persistidos pelo coordenador quando
disponíveis e usam uma chamada de gradebook por curso quando a capacidade bulk está disponível.
O gateway consulta o catálogo de funções por conexão antes de promover o bulk; ausência ou falha
na descoberta mantém o caminho individual. O gateway mantém fallback individual bounded e faz
retry apenas dos estudantes omitidos pelo bulk. Contratos públicos continuam compatíveis porque
os campos de prefetch são opcionais e o conversor do snapshot aceita o formato legado.
Os read models expõem ainda \`GradebookStatus\`/listas de cobertura aditivas para distinguir
gradebook coberto, vazio, item ausente, usuário não retornado e erro de leitura; nenhum desses
estados é convertido silenciosamente em “sem nota”.

## Orçamento de chamadas

Considere `S` estudantes, `A` assignments, `P` páginas de participantes e chunks de 50 IDs.
Retries e fallbacks por erro não entram no caminho normal e são medidos separadamente.

| Dataset/fluxo | Estado atual | Caminho normal alvo |
|---|---:|---:|
| Cursos | 1 leitura ampla por refresh; operações locais no snapshot | preservar 1; zero em cache quente |
| Conteúdo do curso | 1 por curso | preservar 1; zero em cache quente |
| Participantes | 1 página de até 1.000 no sync | `P`; zero em cache quente e sem truncamento silencioso |
| Configuração de assignments | 1 por curso | preservar 1 quando necessária |
| Submissões | `ceil(A/50)` no gateway em lote | preservar `ceil(A/50)` |
| Notas de assignments no sync | até `A` | `ceil(A/50)` |
| Gradebook nos relatórios | até `S` | 1 quando o bulk for suportado e seguro |
| Completion por turma | até `S` | fora do fast path; custo declarado |

Um relatório que combine participantes, gradebook e submissões não pode afirmar “4 chamadas” sem
considerar páginas, chunks, capability, fallback e refresh dos heads. A resposta deve expor a
cobertura real.

## Escopo

- Adicionar contrato e sincronização do dataset `gradebook` por curso.
- Validar o modo coletivo de `gradereport_user_get_grade_items` nas conexões-alvo.
- Adicionar batching multi-assignment a `mod_assign_get_grades`.
- Completar paginação do snapshot de participantes dentro de limites configurados.
- Criar projeções locais por estudante, item e assignment.
- Migrar tools individuais e coletivas para consumir o mesmo read model.
- Padronizar coverage, freshness, partial failure, call budget e telemetria.
- Preservar fallbacks capability-driven para instalações Moodle heterogêneas.

## Fora de escopo

- Alterar nomes, input schemas ou propósito das tools MCP públicas.
- Criar métricas pedagógicas novas ou mudar regras de risco, média e classificação.
- Usar `core_grades_get_gradeitems` como fonte de notas.
- Baixar arquivos, tentativas de quiz, SCORMs, logs ou todo o Moodle em um snapshot único.
- Tornar completion coletivo O(1) sem um endpoint que realmente ofereça esse contrato.
- Remover imediatamente o caminho individual de gradebook.
- Fazer escrita no Moodle a partir de snapshot stale.

## Dependências e decisões em aberto

- SPEC-0013: descoberta de função e exposição capability-driven.
- SPEC-0015: heads, freshness, fila, lineage, hash e publicação atômica.
- SPEC-0017: concorrência PostgreSQL e gates multi-réplica.
- SPEC-0024: reutilização de snapshots no caminho de correção assistida.
- Medir no canário o tempo e o payload de `userid=0` com turmas pequenas, médias e no maior
  curso autorizado disponível.
- Confirmar em cada conexão-alvo a combinação de `gradereport/user:view`,
  `moodle/grade:viewall` e grupos visíveis.
- Definir limites finais de estudantes, células, bytes e deadline a partir das evidências, sem
  elevar defaults apenas para fazer o benchmark passar.

## Contratos, compatibilidade e migração

- `IMoodleGradebookGateway` mantém `GetStudentGradebookAsync` e ganha uma operação coletiva com
  retorno de coverage; o método individual pode projetar um snapshot coletivo já carregado.
- `IMoodleAssignmentGradeReadGateway` mantém os métodos atuais e ganha operação em lote.
- `MoodleSnapshotDatasets` ganha `Gradebook = "gradebook"`.
- `MoodleSnapshotOptions` ganha opções de bulk, células, chunks, TTL, skew e concorrência.
- Respostas MCP preservam campos atuais e recebem metadados aditivos de freshness/cobertura no
  envelope comum; consumidores antigos continuam válidos.
- Snapshots antigos continuam legíveis. Não há migration destrutiva: o novo dataset começa vazio
  e é preenchido pela fila.
- A memória cacheada por estudante pode permanecer temporariamente como fallback, mas não é
  fonte de completude do curso e sua chave deve usar `ConnectionId`, não alias.
- Mudanças de nota e submissão invalidam ou priorizam refresh dos heads afetados depois de uma
  escrita confirmada; a confirmação de escrita continua baseada em leitura autoritativa e regras
  das SPEC-0011/0012.

## Segurança, privacidade e observabilidade

- O head de gradebook contém registros educacionais e permanece isolado por owner, conexão e
  curso, sujeito ao mesmo controle de acesso e exclusão de conta/conexão dos demais snapshots.
- Não registrar payload bruto, nomes, emails, feedback, notas ou token. Métricas usam contagens,
  bytes, duração, função, modo, resultado e hashes sem reversão prática.
- Feedback é persistido somente se necessário para manter paridade com o contrato atual; não será
  duplicado em runs, logs ou read models derivados.
- Medir por função: chamadas, duração, timeout, bytes, rows, warnings e fallback.
- Medir por tool: heads usados, idade, skew, coverage, cache hit e chamadas Moodle totais.
- Alertar para crescimento de payload, fallback individual frequente, snapshot incompleto e
  divergência persistente entre participantes e gradebook.

## Plano de execução

### Fase 0 — prova de contrato e baseline

1. Adicionar cenários MoodleBench sanitizados para:
   `gradereport_user_get_grade_items` individual e `userid=0`,
   `core_grades_get_gradeitems` e `mod_assign_get_grades` com múltiplos IDs.
2. Comparar bulk e individual por `(userid, gradeitem.id)`, incluindo total do curso, atividade,
   nota nula, item oculto, feedback, grupos e warnings.
3. Medir chamadas, p50/p95, bytes e células em turmas de referência.
4. Manter `BulkGradebookEnabled=false` se o modo coletivo não for permitido ou não respeitar o
   orçamento. O restante da spec continua viável com batching de assignments e snapshots com
   fallback limitado.

### Fase 1 — primitivas em lote

1. Adicionar resultados coletivos e coverage em Application.
2. Implementar parser único do payload `usergrades[]`, reutilizado pelos caminhos bulk e
   individual.
3. Implementar `mod_assign_get_grades` multi-assignment com chunks e falha por item.
4. Criar testes de request serialization, campos opcionais, warnings, item omitido e fallback.

Nesta implementação, a projeção canônica de gradebook também é serializada sem o dicionário
duplicado por estudante, com leitura compatível de heads legados e teste de round-trip.

### Fase 2 — snapshot de gradebook

1. Adicionar dataset, opções, DTO normalizado, projector e índices locais. **Implementado** com
   `Items[]`, `StudentGrades[]`, `Coverage` e reconstrução compatível por estudante.
2. Completar participantes em páginas antes da reconciliação.
3. Integrar o dataset à fila, runs, freshness, hash, payload budget e métricas existentes.
4. Não publicar um head novo quando o payload exceder o limite; preservar o último head válido e
   registrar a tentativa falha sanitizada.
5. Invalidar/priorizar refresh após escrita de nota confirmada.

### Fase 3 — consumo compartilhado

1. Criar o coordenador `CourseReadSnapshot` e política por requisito. **Implementado** em
   `IMoodleCourseReadSnapshotCoordinator` e `MoodleSnapshotToolContext`.
2. Migrar gradebook individual e notas por atividade para projeção local quando cobertos.
   **Implementado** nas tools de gradebook e nos handlers de notas.
3. Migrar abaixo da média, relatório de notas, semanal, conselho, risco e relatórios derivados.
   **Implementado** com prefetch opcional e fallback compatível.
4. Reusar submissions/participants existentes, evitando que uma tool recarregue o mesmo dataset
   por outro handler no mesmo request. **Implementado** nos fluxos MCP e nas rotas de relatório
   e detalhe do estudante.
5. Remover avisos que afirmam “uma chamada por estudante” somente após os gates de chamada e
   fallback estarem verdes. **Implementado** para os relatórios migrados; o rollout operacional
   ainda é pendente.

### Fase 4 — rollout e endurecimento

1. Habilitar bulk por conexão em canário, começando por turma pequena.
2. Observar fallback, timeout, payload e equivalência por pelo menos um ciclo de freshness.
3. Expandir por coortes de conexão; não habilitar globalmente por versão do Moodle apenas.
4. Atualizar roadmap e documentação técnica com a cobertura realmente homologada.

## Critérios de aceite

- [ ] Listar, pesquisar e resolver os 402 cursos usa uma leitura
  `core_enrol_get_users_courses` por refresh e zero chamadas por curso quando o head está válido.
- [ ] O modo bulk do gradebook prova equivalência com a leitura individual para todos os campos
  consumidos pelas tools, ou permanece desabilitado naquela conexão.
- [ ] `core_grades_get_gradeitems` não é usado como fonte de notas nem de total do curso.
- [ ] Em conexão homologada e dentro dos limites, um refresh completo de gradebook faz uma chamada
  coletiva, sem chamadas proporcionais a `S`.
- [ ] `mod_assign_get_grades` usa `ceil(A/chunkSize)` chamadas no caminho normal, não `A`.
- [ ] Participantes acima do page size são paginados ou o head fica explicitamente incompleto.
- [ ] Gradebook ausente, nota nula, estudante não retornado e erro de leitura permanecem estados
  distintos em todos os relatórios.
- [ ] Cache quente de gradebook, participantes e submissões produz zero chamadas Moodle nas tools
  migradas e retorna freshness/cobertura dos heads usados.
- [ ] Snapshot stale pode atender somente leitura não decisional com warning e refresh agendado;
  escrita e reconciliação não confiam nele como estado atual.
- [ ] Duas tools concorrentes para o mesmo curso não iniciam dois refreshes equivalentes.
- [ ] Payload acima do orçamento, timeout e erro de um assignment não apagam o último head válido
  nem transformam resultado parcial em completo.
- [ ] Métricas e evidências não contêm PII acadêmica, feedback ou credenciais.

## Validação e evidências

Testes automatizados previstos:

```powershell
dotnet test tests/MoodleConnector.Application.Tests --filter "FullyQualifiedName~Gradebook|FullyQualifiedName~Snapshot|FullyQualifiedName~Report|FullyQualifiedName~Risk|FullyQualifiedName~Submission"
dotnet test tests/MoodleConnector.Application.Tests --filter "FullyQualifiedName~MoodleAssignmentGradeReadGateway|FullyQualifiedName~MoodleSnapshotSync"
dotnet test tests/MoodleConnector.Postgres.IntegrationTests --filter "FullyQualifiedName~Snapshot|FullyQualifiedName~Sync"
```

Validação executada nesta implementação:

```text
dotnet build MoodleConnector.sln --no-restore                         # 0 erros, 0 avisos
dotnet vstest .../MoodleConnector.Application.Tests.dll `
  --TestCaseFilter:"FullyQualifiedName!~Integration"                  # 948 aprovados
dotnet vstest .../MoodleConnector.Application.Tests.dll `
  --TestCaseFilter:"FullyQualifiedName~MoodleGradebookGatewayCachingTests" # 7 aprovados após a projeção
dotnet test ... --filter "MoodleAssignmentGradeReadGatewayTests"       # 4 aprovados
dotnet test ... --filter "MoodleReportToolsTests|...ReportQuery..."     # 29 aprovados
dotnet vstest .../MoodleConnector.Application.Tests.dll `
  --TestCaseFilter:"FullyQualifiedName~McpJwtClaimsIntegrationTests.Deve_retornar_401_quando_api_key_estiver_ausente" # 1 aprovado
```

Os critérios que dependem de MoodleBench, permissões reais, duas réplicas ou PostgreSQL ainda
permanecem abertos até a homologação por conexão. Os testes locais cobrem serialização do lote,
cache, retry seletivo de alunos ausentes, limites, projeção canônica com round-trip legado e
preservação dos contratos dos handlers. Os relatórios também preservam estados de cobertura por
estudante e emitem warning agregado quando há erro ou usuário não retornado.

Evidências obrigatórias de homologação:

- relatório MoodleBench sanitizado com bulk ligado/desligado, mesma turma e mesmos campos;
- contador de chamadas por função e por tool;
- payload e duração para turmas pequena, média e limite;
- teste de grupos/permissões e reconciliação da população;
- teste de duas réplicas, refresh concorrente, stale-while-revalidate e preservação do último head;
- comparação estrutural dos outputs MCP antes/depois, ignorando apenas metadados aditivos.

## Rollout e rollback

Rollout em quatro passos: contratos e métricas sem mudança de comportamento; batching de
`mod_assign_get_grades`; criação do dataset com consumidores em shadow mode; bulk por conexão em
canário. A promoção exige paridade, cobertura completa e orçamento atendido.

Rollback global desabilita `BulkGradebookEnabled` e retorna ao gateway individual com
concorrência limitada; por conexão, a ausência/falha de capability já força esse mesmo caminho.
O head `gradebook` pode permanecer armazenado sem ser consumido; nenhuma migration destrutiva é
necessária. O batching de assignments pode voltar ao tamanho de chunk 1 sem alterar contratos
públicos. O último head válido nunca é apagado por rollback ou tentativa falha.
