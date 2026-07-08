# Roadmap de Apoio ao Tutor, Monitor e Corpo Pedagógico

## Propósito e autoridade

O Moodle Connector apoia atividades previstas no Guia do Tutor: coleta e organiza evidências disponíveis, produz rascunhos e executa ações explicitamente autorizadas. Funções Moodle e tools MCP delimitam o apoio possível; não definem o propósito pedagógico e não substituem tutor, monitor, corpo pedagógico, CTM, coordenação ou regras do Departamento Regional.

Este documento é a fonte canônica para prioridades do produto. O status técnico é baseado no código e nos testes do repositório em 7 de julho de 2026. Disponibilidade em execução continua condicionada ao catálogo do serviço, ao contexto, às permissões e à configuração do Moodle autorizado.

## Públicos e responsabilidades

- **Tutor:** acompanha participação e rendimento, orienta, corrige, oferece feedback, identifica sinais e propõe contatos ou recuperação.
- **Monitor:** apoia ambientação, acesso e navegação; verifica a organização inicial e encaminha ocorrências.
- **Corpo pedagógico e CTM:** supervisiona o processo, combina evidências EaD e presenciais, valida critérios e delibera sobre intervenções e resultados acadêmicos.

## Níveis de suporte

- **Nível A — suportado:** função contratada, implementação e teste localizável. Permissão e presença do dado ainda são verificadas em execução.
- **Nível B — assistido com limitações:** agregação local, chamadas por estudante ou sinal observável; exige declaração de cobertura, truncamento e revisão humana.
- **Nível C — dependente de configuração/admin:** exige capability, permissão, plugin, completion ou mapeamento institucional ainda não garantido.
- **Nível D — não suportado:** fonte ou infraestrutura ausente no contrato atual.
- **Nível H — exclusivamente humano:** julgamento ou decisão acadêmica/pedagógica que não pode ser automatizada.

## Semântica obrigatória de estados vazios

Toda resposta nova ou revisada deve distinguir explicitamente:

| Estado | Significado obrigatório |
| --- | --- |
| `zero_observado` | A fonte foi consultada com sucesso, dentro da cobertura declarada, e nenhum registro correspondente foi encontrado. |
| `dado_indisponivel` | A resposta não trouxe o campo ou a fonte necessária; não equivale a zero. |
| `funcao_indisponivel` | A função não consta ou não pôde ser usada no serviço autorizado. |
| `sem_permissao` | A função existe, mas o token não pode acessar o recurso/contexto. |
| `nao_configurado` | O recurso Moodle necessário, como completion, não está configurado. |
| `truncado` | Há resultados além do limite de estudantes, páginas, discussões, posts ou chamadas. |
| `falha_parcial` | Parte das fontes/chamadas falhou; resultados remanescentes não representam cobertura integral. |

Lista vazia sem estado é ambígua e não autoriza concluir ausência de atividade, aprendizagem, participação ou risco. A linguagem externa deve preferir “não encontramos registro nos dados visíveis” e informar fonte, período e cobertura.

## Regras transversais

- Acesso, entrega, completion e posts são registros técnicos, não medidas diretas de estudo, esforço, compreensão, motivação ou engajamento.
- Nota ou completion isolados não comprovam competência. Avaliação combina funções diagnóstica, formativa e somativa e requer critérios observáveis vinculados a capacidades.
- “Abaixo do mínimo” é um sinal numérico configurado; não prova que critério crítico ou capacidade não foi atingido.
- Dados ausentes reduzem a confiança e aparecem em `limitations`; nunca elevam risco automaticamente.
- Recuperação exige análise, orientação, período, nova oportunidade e acompanhamento humano.
- Toda escrita real exige conexão `CanWrite`, escopo aplicável, flag ativa, `PendingAction`, prévia, confirmação literal, idempotência e auditoria sanitizada.
- Nenhuma ação coletiva expõe nota, risco individual ou conteúdo sensível a outros estudantes.
- Paginação pública deve ser **1-based**. Cobertura deve informar elegíveis, analisados, excluídos, limites e falhas.

## Como ler as fichas

Cada atividade declara: público; referência pedagógica em `public/pedagogic`; resultado humano; evidências necessárias e disponíveis; funções Moodle; tool MCP; nível; cobertura e limites; limitações; gate humano; status; e evidência de conclusão. “Implementado” significa código e teste localizáveis, não disponibilidade universal no Moodle.

## Jornada 1 — Preparação e ambientação

### Auditar a organização da sala virtual

- **Público:** monitor; tutor.
- **Referência pedagógica:** `public/pedagogic/Guia do Tutor - Com ISBN 1 (6).md`, orientações sobre planejamento, organização do AVA e atuação do monitor.
- **Resultado humano:** verificar o checklist inicial e encaminhar lacunas ao responsável.
- **Evidências necessárias / disponíveis:** idealmente plano da unidade e padrão institucional; disponíveis seções, módulos, tipos, descrições, visibilidade e datas retornadas pelo curso.
- **Funções Moodle / tool MCP:** `core_course_get_contents`; `auditar_checklist_sala_virtual` e `auditar_estrutura_curso`.
- **Nível / cobertura e limites:** **Nível A** para presença técnica dos itens visíveis; um curso por chamada, limitado ao conteúdo retornado ao token.
- **Limitações:** não verifica qualidade, finalidade pedagógica, acessibilidade material nem aderência ao plano fora do Moodle.
- **Gate humano:** monitor/tutor interpreta cada `[ ]`/`[x]`, registra observação e confirma a adequação.
- **Status / evidência de conclusão:** **implementado**; `AuditVirtualClassroomChecklistQuery.cs` e `AuditVirtualClassroomChecklistQueryHandlerTests.cs`.

### Verificar materiais, fóruns, atividades e datas

- **Público:** monitor; tutor.
- **Referência pedagógica:** `public/pedagogic/Guia do Tutor - Com ISBN 1 (6).md`, planejamento e preparação das atividades.
- **Resultado humano:** localizar ausências ou inconsistências técnicas antes do início da oferta.
- **Evidências necessárias / disponíveis:** materiais previstos e calendário oficial; disponíveis conteúdos, recursos, assignments, fóruns e datas visíveis.
- **Funções Moodle / tool MCP:** `core_course_get_contents`, `mod_assign_get_assignments`, `mod_forum_get_forums_by_courses`; `listar_conteudos_curso`, `listar_atividades_curso`, `listar_tarefas_curso`, `consultar_prazos_atividades`.
- **Nível / cobertura e limites:** **Nível A** para inventário técnico; paginação 1-based onde exposta e cobertura limitada ao catálogo visível.
- **Limitações:** calendário completo, coerência entre datas e plano e configuração pedagógica do fórum não são contratados.
- **Gate humano:** tutor valida intenção, sequência, prazo e canal adequado.
- **Status / evidência de conclusão:** **implementado**; handlers de Contents/Activities/Assignments e respectivos testes em `tests/MoodleConnector.Application.Tests`.

### Apoiar ambientação, acesso e navegação

- **Público:** monitor; tutor.
- **Referência pedagógica:** `public/pedagogic/Guia do Tutor - Com ISBN 1 (6).md`, ambientação e atribuições do monitor.
- **Resultado humano:** orientar o estudante e encaminhar impedimentos de acesso.
- **Evidências necessárias / disponíveis:** relato do estudante, situação cadastral e logs de autenticação; disponíveis matrícula, dados minimizados do participante e último acesso quando retornado.
- **Funções Moodle / tool MCP:** `core_enrol_get_enrolled_users`; `listar_participantes_curso`, `listar_alunos_curso`.
- **Nível / cobertura e limites:** **Nível B**; páginas 1-based de até 50 participantes nas tools; último acesso pode não ser retornado.
- **Limitações:** não há logs detalhados de login, sessão ou navegação e não se diagnostica a causa do acesso.
- **Gate humano:** monitor confirma a ocorrência com o estudante/suporte antes de encaminhar.
- **Status / evidência de conclusão:** **parcial**; listagem e acesso observado implementados, diagnóstico de acesso **Nível D**.

### Preparar mensagem de boas-vindas

- **Público:** tutor; monitor como apoio.
- **Referência pedagógica:** `public/pedagogic/Guia do Tutor - Com ISBN 1 (6).md`, acolhimento, apresentação e comunicação inicial.
- **Resultado humano:** revisar uma comunicação individual de acolhimento e orientação.
- **Evidências necessárias / disponíveis:** texto institucional, canal, turma e destinatários autorizados; participantes visíveis e template informado pelo usuário.
- **Funções Moodle / tool MCP:** `core_enrol_get_enrolled_users`, `core_message_send_instant_messages`; `preparar_mensagem_boas_vindas` / `confirmar_mensagem_boas_vindas`.
- **Nível / cobertura e limites:** **Nível B**; envio é composto por mensagens individuais, sem broadcast nativo ou agendamento.
- **Limitações:** `MessagesWriteEnabled=false` é o padrão, mas a flag ainda não controla efetivamente o registro/execução das tools; ver backlog P0.
- **Gate humano:** prévia com critérios, exclusões, destinatários e corpo sanitizado; confirmação literal antes do envio.
- **Status / evidência de conclusão:** **parcial**; par prepare/confirm e testes de preparação existem, porém o gate da flag está incompleto.

## Jornada 2 — Acompanhamento semanal

### Consultar o último acesso registrado

- **Público:** tutor; monitor.
- **Referência pedagógica:** `public/pedagogic/Guia do Tutor - Com ISBN 1 (6).md`, acompanhamento contínuo e comunicação com estudantes.
- **Resultado humano:** priorizar verificação de contato sem diagnosticar inatividade real.
- **Evidências necessárias / disponíveis:** sessões e contexto de acesso; disponível somente `lastaccess` retornado para participantes.
- **Funções Moodle / tool MCP:** `core_enrol_get_enrolled_users`; `listar_alunos_sem_acesso`.
- **Nível / cobertura e limites:** **Nível B**; padrão de até 100 estudantes por análise.
- **Limitações:** campo ausente é `dado_indisponivel`, não “nunca acessou”; não há histórico detalhado.
- **Gate humano:** confirmar com o estudante e considerar outras fontes.
- **Status / evidência de conclusão:** **implementado com limitações**; `GetStudentsWithoutRecentAccessQuery.cs` e testes dedicados.

### Identificar submissões não encontradas

- **Público:** tutor.
- **Referência pedagógica:** `public/pedagogic/Guia do Tutor - Com ISBN 1 (6).md`, acompanhamento e correção das atividades.
- **Resultado humano:** localizar pendências observáveis e orientar o estudante.
- **Evidências necessárias / disponíveis:** entrega esperada, prazo, exceções e tentativas; assignments e submissions visíveis.
- **Funções Moodle / tool MCP:** `mod_assign_get_assignments`, `mod_assign_get_submissions`, `mod_assign_get_submission_status`; `listar_entregas_pendentes`, `listar_alunos_pendentes_atividade`.
- **Nível / cobertura e limites:** **Nível B** para consolidação por estudante; padrão de até 100 estudantes, múltiplas chamadas e limites de assignments visíveis.
- **Limitações:** extensões de prazo, entrega externa e falha parcial podem alterar a interpretação.
- **Gate humano:** tutor verifica exceções antes de contatar ou intervir.
- **Status / evidência de conclusão:** **implementado com limitações**; `GetStudentsWithPendingSubmissionsQuery.cs` e testes dedicados.

### Identificar posts não encontrados em fórum visível

- **Público:** tutor.
- **Referência pedagógica:** `public/pedagogic/Guia do Tutor - Com ISBN 1 (6).md`, mediação e acompanhamento de fóruns.
- **Resultado humano:** reconhecer estudantes para os quais não foi encontrado post na amostra consultada.
- **Evidências necessárias / disponíveis:** fóruns esperados, grupos, discussões e posts; disponíveis apenas fóruns/discussões/posts visíveis ao token.
- **Funções Moodle / tool MCP:** `mod_forum_get_forums_by_courses`, `mod_forum_get_forum_discussions`, `mod_forum_get_discussion_posts`; `listar_alunos_sem_participacao_forum`.
- **Nível / cobertura e limites:** **Nível B**; padrão de até 100 estudantes e 20 discussões; leitura comum limita páginas a 25 discussões e posts por discussão conforme a chamada.
- **Limitações:** grupos, posts ocultos, paginação e discussões excedentes podem produzir truncamento; ausência de post não é ausência de estudo.
- **Gate humano:** tutor revisa cobertura e contexto do fórum antes de agir.
- **Status / evidência de conclusão:** **implementado com limitações**; `GetStudentsWithoutForumParticipationQuery.cs` e testes dedicados.

### Consultar completion configurado

- **Público:** tutor; corpo pedagógico/CTM.
- **Referência pedagógica:** `public/pedagogic/Guia do Tutor - Com ISBN 1 (6).md`, acompanhamento da realização das atividades.
- **Resultado humano:** visualizar estados técnicos de conclusão sem convertê-los em aprendizagem.
- **Evidências necessárias / disponíveis:** critérios institucionais e registros de conclusão; disponíveis completion de atividades e do curso quando configurados.
- **Funções Moodle / tool MCP:** `core_completion_get_activities_completion_status`, `core_completion_get_course_completion_status`; `consultar_progresso_aluno`.
- **Nível / cobertura e limites:** **Nível C** quanto à disponibilidade; leitura individual implementada, agregações exigem chamadas por estudante.
- **Limitações:** indisponibilidade, falta de permissão e `nao_configurado` devem ser distintos de incompleto.
- **Gate humano:** interpretar junto às regras da atividade e demais evidências.
- **Status / evidência de conclusão:** **parcial**; leitura individual testada, semântica vazia ainda é P0.

### Acompanhar dúvidas e encaminhamentos

- **Público:** tutor; monitor.
- **Referência pedagógica:** `public/pedagogic/Guia do Tutor - Com ISBN 1 (6).md`, responsabilidades distintas de tutor e monitor na orientação.
- **Resultado humano:** localizar manifestações visíveis e encaminhá-las ao papel correto.
- **Evidências necessárias / disponíveis:** conteúdo, autoria, canal, prazo e histórico do atendimento; disponíveis posts em fóruns visíveis.
- **Funções Moodle / tool MCP:** funções de fórum; `ler_forum`.
- **Nível / cobertura e limites:** **Nível B**; paginação 1-based, até 25 discussões por página, posts limitados pela solicitação.
- **Limitações:** mensagens privadas, canais externos e classificação semântica de dúvida não são fontes contratadas.
- **Gate humano:** tutor/monitor lê, classifica e decide a resposta ou o encaminhamento.
- **Status / evidência de conclusão:** **parcial**; leitura de fórum implementada; fila integrada de atendimento é planejada.

## Jornada 3 — Avaliação e feedback

### Consultar gradebook individual

- **Público:** tutor; corpo pedagógico/CTM.
- **Referência pedagógica:** `public/pedagogic/METODOLOGIA SENAI DE EDUCACAO PROFISSIONAL.md`, avaliação da aprendizagem; `public/pedagogic/Guia de Desenvolvimento de Situação de Aprendizagem.md`, “Funções da avaliação” e “Resultados da avaliação”.
- **Resultado humano:** examinar os itens e registros de nota de um estudante como parte da avaliação.
- **Evidências necessárias / disponíveis:** critérios, instrumentos, capacidades e histórico; disponíveis itens, faixas, nota, percentual e feedback retornados por estudante.
- **Funções Moodle / tool MCP:** `gradereport_user_get_grade_items`; `consultar_boletim_aluno`.
- **Nível / cobertura e limites:** **Nível A** para leitura individual; um estudante por chamada.
- **Limitações:** item/nota ausente não comprova pendência; o gradebook não contém necessariamente o vínculo SA-capacidade-critério.
- **Gate humano:** tutor interpreta no contexto dos instrumentos; CTM valida regras de conversão.
- **Status / evidência de conclusão:** **implementado**; `GetStudentGradeItemsQuery.cs`, `GetStudentGradebookQuery.cs` e testes dedicados.

### Produzir visão coletiva de desempenho

- **Público:** tutor; corpo pedagógico/CTM.
- **Referência pedagógica:** mesmas seções de avaliação e resultados da atividade anterior.
- **Resultado humano:** comparar fatos observáveis da turma e identificar itens que pedem análise.
- **Evidências necessárias / disponíveis:** gradebook coletivo, critérios e população completa; disponível agregação local de leituras individuais.
- **Funções Moodle / tool MCP:** `core_enrol_get_enrolled_users`, `gradereport_user_get_grade_items`; `listar_alunos_abaixo_minimo`, reports da Jornada 6.
- **Nível / cobertura e limites:** **Nível B**; padrão de até 60–100 estudantes conforme a tool e uma ou mais chamadas por estudante.
- **Limitações:** não é snapshot atômico; custo, falhas parciais e truncamento podem alterar denominadores.
- **Gate humano:** revisar cobertura e critérios antes de comunicar ou intervir.
- **Status / evidência de conclusão:** **parcial**; agregações existem, contrato uniforme de cobertura é P0.

### Relacionar SA, capacidade, critério e instrumento

- **Público:** tutor; corpo pedagógico/CTM.
- **Referência pedagógica:** `public/pedagogic/Guia de Desenvolvimento de Situação de Aprendizagem.md`, “Funções da avaliação”, “Estratégias de avaliação”, “Instrumentos de avaliação” e modelo de capacidade/critério crítico.
- **Resultado humano:** avaliar desempenho observável contra critérios aprovados.
- **Evidências necessárias / disponíveis:** mapeamento explícito SA-capacidade-critério-rubrica-conversão; o Moodle fornece somente metadados e rubricas/configurações que estejam acessíveis.
- **Funções Moodle / tool MCP:** `mod_assign_get_assignments`, `gradereport_user_get_grade_items`; `consultar_contexto_item_correcao_assistida`.
- **Nível / cobertura e limites:** **Nível C**; depende de configuração/mapeamento institucional íntegro.
- **Limitações:** heurística de contexto não substitui mapeamento aprovado; nota não comprova competência.
- **Gate humano:** tutor e CTM aprovam critérios, vínculos e conversão.
- **Status / evidência de conclusão:** **planejado/parcial**; contexto assistido existe, mapeamento institucional explícito não.

### Corrigir e elaborar feedback assistido

- **Público:** tutor.
- **Referência pedagógica:** `public/pedagogic/METODOLOGIA SENAI DE EDUCACAO PROFISSIONAL.md`, avaliação formativa; `public/pedagogic/Guia do Tutor - Com ISBN 1 (6).md`, correção e feedback.
- **Resultado humano:** revisar evidências e transformar um rascunho em feedback pedagógico próprio.
- **Evidências necessárias / disponíveis:** enunciado, entrega, anexos, rubrica, critérios e histórico; disponíveis tarefas, submissões, arquivos permitidos, contexto e rascunhos internos.
- **Funções Moodle / tool MCP:** funções `mod_assign_*`; `listar_entregas_corrigiveis`, `criar_lote_correcao_assistida`, `preparar_lote_correcao_ia`, `salvar_correcoes_ia_lote`, `revisar_feedbacks_lote`.
- **Nível / cobertura e limites:** **Nível B**; lote até 400 itens por configuração, arquivos/tamanho/texto limitados por `GradingLimits`.
- **Limitações:** extração e contexto podem ser incompletos; IA não decide nota nem publica automaticamente.
- **Gate humano:** revisão item a item antes da prévia; texto aprovado fica auditável.
- **Status / evidência de conclusão:** **implementado com limitações**; fluxo de grading e testes em `Tools/Grading` e `Grading`.

### Lançar nota e feedback individual

- **Público:** tutor.
- **Referência pedagógica:** referências de avaliação acima.
- **Resultado humano:** publicar o resultado revisado para uma tarefa autorizada.
- **Evidências necessárias / disponíveis:** nota atual, faixa, justificativa, feedback e autorização; dados da tarefa/grade e ação pendente.
- **Funções Moodle / tool MCP:** `mod_assign_get_grades`, `mod_assign_save_grade`; `preparar_lancamento_nota`, `confirmar_lancamento_nota` e fluxo em lote.
- **Nível / cobertura e limites:** **Nível A** para o fluxo individual quando todos os gates estão ativos; uma escrita por estudante/tarefa.
- **Limitações:** `AssignmentGradeWriteEnabled=true` no `appsettings.json` atual viola o default seguro requerido; disponibilidade da função não prova permissão contextual.
- **Gate humano:** justificativa, prévia, texto literal, escopo, `CanWrite`, flag, idempotência e auditoria.
- **Status / evidência de conclusão:** **implementado, configuração insegura pendente**; `IndividualGradeCommands.cs`, `MoodleIndividualGradeTools.cs` e testes; correção do default é P0.

## Jornada 4 — Recuperação e intervenção pedagógica

### Observar sinais configuráveis de atenção

- **Público:** tutor; corpo pedagógico/CTM.
- **Referência pedagógica:** `public/pedagogic/METODOLOGIA SENAI DE EDUCACAO PROFISSIONAL.md`, recuperação e avaliação formativa; `public/pedagogic/Guia do Tutor - Com ISBN 1 (6).md`, acompanhamento.
- **Resultado humano:** priorizar casos para análise, sem diagnóstico automático.
- **Evidências necessárias / disponíveis:** trajetória, critérios, contexto pessoal e fontes EaD/presenciais; disponíveis último acesso, notas e completion visíveis.
- **Funções Moodle / tool MCP:** enrolment, completion e gradebook; `gerar_relatorio_risco_estudantes`, `listar_alunos_sem_acesso`, `listar_alunos_abaixo_minimo`.
- **Nível / cobertura e limites:** **Nível B**; risco usa padrão de 50 estudantes, limiares configuráveis e chamadas por estudante.
- **Limitações:** dados ausentes e completion não configurado devem reduzir confiança; labels Alto/Médio/Baixo são sinais técnicos, não diagnósticos.
- **Gate humano:** tutor/CTM confronta outras fontes e decide se há necessidade de contato.
- **Status / evidência de conclusão:** **implementado com limitações**; queries/tools de Risk e testes dedicados.

### Produzir relatório de atenção acionável

- **Público:** tutor; corpo pedagógico/CTM.
- **Referência pedagógica:** referências de recuperação acima.
- **Resultado humano:** separar fatos, hipóteses, próximos passos e limitações para revisão.
- **Evidências necessárias / disponíveis:** fontes e cobertura completas; resposta de risco atual com estudantes/fatores e warnings.
- **Funções Moodle / tool MCP:** mesmas da atividade anterior; `gerar_relatorio_risco_estudantes`.
- **Nível / cobertura e limites:** **Nível B**; o contrato-alvo é:

```json
{
  "findings": [],
  "possibleRisks": [],
  "recommendedActions": [],
  "limitations": [],
  "suggestedAudience": [],
  "requiresHumanReview": true
}
```

- **Limitações:** o contrato implementado ainda não expõe uniformemente todos esses campos, cobertura e estados de ausência.
- **Gate humano:** `requiresHumanReview` permanece verdadeiro.
- **Status / evidência de conclusão:** **parcial**; relatório atual testado; contrato-alvo completo é backlog P0/P1.

### Recomendar e preparar contato de acompanhamento

- **Público:** tutor.
- **Referência pedagógica:** `public/pedagogic/Guia do Tutor - Com ISBN 1 (6).md`, comunicação e acompanhamento; MSEP, recuperação.
- **Resultado humano:** revisar uma mensagem individual contextualizada e não estigmatizante.
- **Evidências necessárias / disponíveis:** motivo observável, exclusões, consentimento/canal e histórico; sinais e participantes visíveis.
- **Funções Moodle / tool MCP:** `core_message_send_instant_messages`; pares `preparar_mensagem_*` / `confirmar_mensagem_*` de acesso, pendência e recuperação.
- **Nível / cobertura e limites:** **Nível B**; mensagens individuais, sem broadcast/scheduler.
- **Limitações:** não expor nota ou “risco” a terceiros; flag de mensagens ainda não efetiva.
- **Gate humano:** revisar linguagem epistêmica, destinatários e confirmação literal.
- **Status / evidência de conclusão:** **parcial**; seis pares prepare/confirm existem; flag efetiva é P0.

### Planejar e acompanhar recuperação

- **Público:** tutor; corpo pedagógico/CTM.
- **Referência pedagógica:** `public/pedagogic/METODOLOGIA SENAI DE EDUCACAO PROFISSIONAL.md`, recuperação; `public/pedagogic/Guia de Desenvolvimento de Situação de Aprendizagem.md`, resultados e instrumentos de avaliação.
- **Resultado humano:** definir orientação, período, nova oportunidade, evidências e acompanhamento.
- **Evidências necessárias / disponíveis:** diagnóstico pedagógico, capacidades/critérios, plano e fontes presenciais/EaD; o conector só oferece registros Moodle visíveis.
- **Funções Moodle / tool MCP:** composição das leituras existentes; não há tool que delibere recuperação.
- **Nível / cobertura e limites:** apoio documental **Nível B/C**; decisão de recuperação, competência, aprovação, reprovação e evasão são **Nível H**.
- **Limitações:** fontes presenciais, SGE e decisão oficial não estão contratadas.
- **Gate humano:** tutor propõe; corpo pedagógico/CTM valida e acompanha.
- **Status / evidência de conclusão:** **planejado** para acompanhamento estruturado; deliberação permanece fora de automação.

## Jornada 5 — Comunicação

### Preparar e enviar mensagens do ciclo do tutor

- **Público:** tutor; monitor somente nos encaminhamentos autorizados.
- **Referência pedagógica:** `public/pedagogic/Guia do Tutor - Com ISBN 1 (6).md`, acolhimento, acompanhamento, orientação e encerramento.
- **Resultado humano:** revisar e enviar mensagens de boas-vindas, acesso, pendência, recuperação, acompanhamento ou encerramento.
- **Evidências necessárias / disponíveis:** finalidade, critério de seleção, exclusões, destinatários, corpo e canal; participantes e sinais observáveis das Jornadas 1–4.
- **Funções Moodle / tool MCP:** `core_message_send_instant_messages`; seis pares específicos `preparar_mensagem_*` / `confirmar_mensagem_*` em `MoodleTutorMessageTools.cs`.
- **Nível / cobertura e limites:** **Nível B**; cada destinatário recebe mensagem instantânea individual e o lote depende da seleção local.
- **Limitações:** não há broadcast nativo, preferências completas de contato, agendamento ou garantia de leitura; `MessagesWriteEnabled` ainda não bloqueia efetivamente o fluxo.
- **Gate humano:** prévia mostra curso, critérios, evidências, exclusões, quantidade/lista de destinatários e corpo sanitizado; confirmação literal, escopo, `CanWrite`, idempotência e auditoria.
- **Status / evidência de conclusão:** **parcial**; prepare/confirm e testes existem, mas flag efetiva e cobertura explícita são P0.

### Publicar em fórum com confirmação

- **Público:** tutor.
- **Referência pedagógica:** `public/pedagogic/Guia do Tutor - Com ISBN 1 (6).md`, mediação de fóruns.
- **Resultado humano:** publicar discussão ou resposta após revisar contexto e texto.
- **Evidências necessárias / disponíveis:** fórum/discussão autorizados, texto e público; metadados visíveis do fórum.
- **Funções Moodle / tool MCP:** `mod_forum_add_discussion`, `mod_forum_add_discussion_post`; `criar_previa_post_forum`, `confirmar_post_forum_moodle`.
- **Nível / cobertura e limites:** **Nível A** quando função/permissão/gates existem; uma publicação por confirmação.
- **Limitações:** visibilidade/grupos dependem da configuração Moodle; não substitui mediação posterior.
- **Gate humano:** prévia e confirmação literal, conexão com escrita, escopo e auditoria.
- **Status / evidência de conclusão:** **implementado**; queries/commands/tools de Forums e testes correspondentes.

### Broadcast e agendamento

- **Público:** tutor; corpo pedagógico/CTM como governança.
- **Referência pedagógica:** Guia do Tutor, comunicação planejada.
- **Resultado humano:** alcançar grupo/curso ou programar comunicação com governança.
- **Evidências necessárias / disponíveis:** grupo, consentimento, janela, cancelamento e execução; não há infraestrutura contratada suficiente.
- **Funções Moodle / tool MCP:** sem função de broadcast nativo; scheduler/worker e tools de agendamento não implementados.
- **Nível / cobertura e limites:** **Nível D**.
- **Limitações:** composição de mensagens individuais não deve ser descrita como broadcast atômico; não existe execução futura confiável.
- **Gate humano:** eventual desenho exigirá confirmação, cancelamento, idempotência, auditoria e aprovação institucional.
- **Status / evidência de conclusão:** **não suportado/planejado**, sem evidência de conclusão.

## Jornada 6 — Relatórios e coordenação

Todos os relatórios devem evoluir para declarar `source`, `collectedAt`, `period`, `coveredCount`, `eligibleCount`, `isTruncated`, `missingCapabilities` e `limitations`. A presença desses campos no contrato-alvo não significa que já estejam implementados.

### Gerar relatório semanal de desempenho

- **Público:** tutor; docente presencial; corpo pedagógico/CTM.
- **Referência pedagógica:** `public/pedagogic/Guia do Tutor - Com ISBN 1 (6).md`, acompanhamento e articulação; MSEP, avaliação formativa.
- **Resultado humano:** compartilhar um retrato descritivo para planejar apoio.
- **Evidências necessárias / disponíveis:** período, população, acesso, notas, completion e contexto; agregações Moodle visíveis.
- **Funções Moodle / tool MCP:** enrolment, gradebook e completion; `gerar_relatorio_semanal_desempenho`.
- **Nível / cobertura e limites:** **Nível B**; padrão de até 60 estudantes, chamadas por estudante.
- **Limitações:** não é censo sem `eligibleCount/coveredCount`; fontes presenciais e interpretação pedagógica ficam fora.
- **Gate humano:** tutor revisa cobertura, dados sensíveis e narrativa antes de compartilhar.
- **Status / evidência de conclusão:** **implementado com contrato parcial**; `GenerateWeeklyPerformanceReportQuery.cs` e testes; metadados uniformes são backlog.

### Gerar resumo executivo e relatório do monitor

- **Público:** monitor; tutor; corpo pedagógico/CTM.
- **Referência pedagógica:** Guia do Tutor, atribuições de monitoramento e acompanhamento.
- **Resultado humano:** comunicar situação operacional da turma e pendências que exigem encaminhamento.
- **Evidências necessárias / disponíveis:** participantes, estrutura, último acesso e fontes administrativas; disponíveis registros Moodle visíveis.
- **Funções Moodle / tool MCP:** `core_course_get_contents`, `core_enrol_get_enrolled_users`; `resumo_executivo_curso`, `gerar_relatorio_monitor_turma`.
- **Nível / cobertura e limites:** **Nível B**; resumo/monitor usam até 100 estudantes por padrão.
- **Limitações:** não inclui chamados externos, presença física, SGE ou causas de acesso.
- **Gate humano:** monitor valida ocorrências e encaminha; coordenação interpreta.
- **Status / evidência de conclusão:** **implementado com limitações**; queries de Reports/Monitor e testes, inclusive checklist.

### Apoiar conselho de classe

- **Público:** corpo pedagógico/CTM; tutor como fornecedor de evidências.
- **Referência pedagógica:** MSEP, avaliação e recuperação; Guia do Tutor, registros e articulação.
- **Resultado humano:** preparar evidências descritivas para deliberação colegiada.
- **Evidências necessárias / disponíveis:** EaD, presencial, SGE, critérios, recuperação e decisões oficiais; disponíveis apenas dados Moodle autorizados.
- **Funções Moodle / tool MCP:** agregações de enrolment/gradebook/completion; `relatorio_conselho_classe`.
- **Nível / cobertura e limites:** relatório técnico **Nível B**, padrão de até 60 estudantes; decisão é **Nível H**.
- **Limitações:** nomes de campos atuais como “concluintes”, “reprovados” ou “evadidos” não constituem decisão oficial e precisam ser tratados como categorias técnicas/hipóteses com limitações.
- **Gate humano:** CTM/conselho reconcilia todas as fontes e delibera.
- **Status / evidência de conclusão:** **parcial**; tool/query e testes existem, mas sem fontes externas nem autoridade decisória.

### Gerar relatório pós-execução e de coordenação

- **Público:** corpo pedagógico/CTM; coordenação; tutor.
- **Referência pedagógica:** Guia do Tutor, registro e avaliação da oferta; MSEP, resultados da avaliação.
- **Resultado humano:** analisar evidências da execução e orientar melhoria.
- **Evidências necessárias / disponíveis:** resultados oficiais, satisfação, qualidade, presencial e EaD; disponíveis gradebook/completion e auditoria de correção.
- **Funções Moodle / tool MCP:** funções de leitura e `mod_assign_*`; `relatorio_pos_execucao`, `exportar_relatorio_correcao_coordenacao`.
- **Nível / cobertura e limites:** **Nível B**; pós-execução usa até 60 estudantes; relatório de correção cobre o lote persistido.
- **Limitações:** satisfação, qualidade pedagógica e resultados oficiais não estão no Moodle contratado; ausência de snapshots impede tendências confiáveis.
- **Gate humano:** coordenação valida fontes, período, denominador e conclusões.
- **Status / evidência de conclusão:** **implementado com limitações**; queries/tools e testes existentes, contrato uniforme pendente.

## Jornada 7 — Operação e governança

### Descobrir capabilities, identidade e permissões efetivas

- **Público:** operação; administrador Moodle; responsável pelo conector.
- **Referência pedagógica:** transversal; protege todas as atividades das Jornadas 1–6.
- **Resultado humano:** saber qual conexão, usuário, curso, função e escopo autorizam cada leitura ou escrita.
- **Evidências necessárias / disponíveis:** catálogo do serviço, identidade do token, vínculo ao curso, `CanWrite`, escopos e flags; o conector descobre funções e resolve a identidade, mas a permissão contextual só é provada pela chamada no recurso.
- **Funções Moodle / tool MCP:** `core_webservice_get_site_info`, `core_enrol_get_users_courses`; descoberta técnica de grading e tools de cursos/conexões.
- **Nível / cobertura e limites:** **Nível A** para descoberta do catálogo e identidade implementadas; **Nível C** para permissão/capability administrada no Moodle.
- **Limitações:** função listada não garante capability no contexto; função ausente deve resultar em `funcao_indisponivel`, sem fallback silencioso.
- **Gate humano:** administrador concede menor privilégio e valida o serviço; operação confere ambiente e alias antes do uso.
- **Status / evidência de conclusão:** **implementado com limitações**; gateways de site info/cursos, auth e testes de integração existem.

### Governar escritas e dados sensíveis

- **Público:** operação; segurança; tutor/CTM como aprovadores da ação.
- **Referência pedagógica:** transversal; preserva autoridade humana, confidencialidade e rastreabilidade.
- **Resultado humano:** manter escritas desligadas por padrão e executar somente a ação revisada e autorizada.
- **Evidências necessárias / disponíveis:** conexão `CanWrite`, escopo, flag por domínio, `PendingAction`, prévia sanitizada, confirmação literal, expiração, idempotência e auditoria.
- **Funções Moodle / tool MCP:** escritas de mensagens, fórum e nota; pares `preparar_*` / `confirmar_*`.
- **Nível / cobertura e limites:** **Nível B** enquanto os defaults e flags não forem uniformes; uma confirmação cobre apenas a prévia persistida.
- **Limitações:** `MessagesWriteEnabled` não controla hoje o registro/execução real; `AssignmentGradeWriteEnabled=true` no arquivo padrão é inseguro.
- **Gate humano:** revisão e confirmação literal obrigatórias; segurança aprova flags e escopos de produção.
- **Status / evidência de conclusão:** **parcial**; gates de conexão, escopo, pending action e testes existem; correções de flags são P0.

### Operar, observar, endurecer e entregar

- **Público:** operação; desenvolvimento; segurança.
- **Referência pedagógica:** transversal; disponibilidade e privacidade condicionam o apoio pedagógico.
- **Resultado humano:** implantar, diagnosticar e transferir a operação sem expor segredos ou dados acadêmicos.
- **Evidências necessárias / disponíveis:** healthcheck, logs sanitizados, métricas, correlação, alertas, backup/restauração, checklist de release, runbooks e registro de mudanças.
- **Funções Moodle / tool MCP:** não depende de função Moodle específica; `GET /health`, auditoria e documentação operacional sustentam as tools.
- **Nível / cobertura e limites:** base de deploy e documentação **Nível A**; observabilidade, alertas, hardening e handoff completos permanecem **Nível B/C** conforme ambiente.
- **Limitações:** sucesso do healthcheck não prova disponibilidade de cada capability Moodle; logs não podem conter tokens, senhas, links privados ou payload acadêmico desnecessário.
- **Gate humano:** checklist de release, aprovação de segurança, validação de restore e aceite do handoff.
- **Status / evidência de conclusão:** **parcial**; Docker/Caddy/CI, runbooks, modelo de auditoria e checklist existem; alertas e validação operacional integral não estão comprovados.

## Matriz de capabilities atuais

`Implementada` abaixo significa gateway/caso de uso e teste localizáveis. Em execução, catálogo, permissão, contexto e configuração continuam obrigatórios.

| Função Moodle | Finalidade atual | Nível | Dependência / permissão | Comportamento requerido quando ausente |
| --- | --- | --- | --- | --- |
| `core_webservice_get_site_info` | Resolver usuário do token e descobrir funções do serviço. | A | Serviço habilitado e token válido. | `funcao_indisponivel`; bloquear descoberta/fluxo dependente. |
| `core_enrol_get_users_courses` | Listar cursos vinculados ao usuário atual. | A | Capability de ver cursos do próprio usuário. | `sem_permissao` ou `funcao_indisponivel`; não interpretar como zero cursos. |
| `core_enrol_get_enrolled_users` | Listar participantes, grupos e último acesso quando retornado. | A/B | Capability de ver participantes no curso; campos variam. | Distinguir `dado_indisponivel` de `zero_observado`; declarar truncamento. |
| `core_course_get_contents` | Ler seções, módulos, recursos, atividades e datas visíveis. | A | Acesso ao curso e conteúdo. | `sem_permissao`/`funcao_indisponivel`; checklist fica inconclusivo. |
| `core_completion_get_activities_completion_status` | Ler completion das atividades por estudante. | A/C | Completion habilitado e permissão contextual. | `nao_configurado`, `sem_permissao` ou `funcao_indisponivel`; nunca lista vazia ambígua. |
| `core_completion_get_course_completion_status` | Ler completion geral por estudante. | A/C | Completion do curso configurado. | Mesmo tratamento explícito de completion; não inferir conclusão. |
| `mod_forum_get_forums_by_courses` | Listar fóruns visíveis. | A | Acesso aos fóruns do curso. | `funcao_indisponivel`/`sem_permissao`; não afirmar que não há fórum. |
| `mod_forum_get_forum_discussions` | Ler discussões paginadas. | A/B | Acesso ao fórum, grupos e paginação. | Declarar falha, página e `truncado`; não concluir ausência de participação. |
| `mod_forum_get_discussion_posts` | Ler posts de discussões visíveis. | A/B | Acesso à discussão/grupo. | `falha_parcial` quando apenas parte das discussões falhar; informar cobertura. |
| `mod_assign_get_assignments` | Ler configuração, identificação e nota máxima de tarefas. | A | Acesso ao curso/tarefa. | `funcao_indisponivel`/`sem_permissao`; não inferir que não há tarefa. |
| `mod_assign_get_submissions` | Ler submissões visíveis e compor pendências/correção. | A/B | Capability de ver submissões. | Distinguir `zero_observado`, `sem_permissao` e `falha_parcial`. |
| `mod_assign_get_submission_status` | Ler tentativa, entrega e feedback de um estudante. | A | Capability para o estudante/tarefa. | `dado_indisponivel` ou erro explícito; não declarar não entrega. |
| `mod_assign_get_grades` | Ler notas existentes de uma tarefa. | A | Capability de ver notas. | `sem_permissao`/`dado_indisponivel`; não tratar como sem nota. |
| `gradereport_user_get_grade_items` | Ler gradebook individual por estudante. | A/B | Capability de grade report no curso. | Marcar indisponibilidade/falha por estudante; agregação declara cobertura. |
| `core_message_send_instant_messages` | Enviar mensagens instantâneas individuais confirmadas. | B | `CanWrite`, escopo, permissão Moodle e flag efetiva. | Bloquear confirmação com `funcao_indisponivel`; nunca simular envio/broadcast. |
| `mod_assign_save_grade` | Gravar nota e feedback individual confirmados. | A | `CanWrite`, escopo, flag desligada por padrão e capability da tarefa. | Bloquear preparação/confirmação; preservar pending action/auditoria como falha. |

As escritas de fórum usam ainda `mod_forum_add_discussion` e `mod_forum_add_discussion_post`, ambas condicionadas a `CanWrite`, escopo, permissão contextual, prévia, confirmação e auditoria.

### Capabilities não contratadas

| Capability / fonte | Nível | Consequência atual |
| --- | --- | --- |
| Logs detalhados de login, sessão e navegação | D | Não diagnosticar acesso, tempo de estudo ou engajamento. |
| Gradebook coletivo eficiente/atômico | D | Agregação local por estudante, com custo, truncamento e falha parcial. |
| Calendário completo e coerência com plano institucional | D/C | Somente datas retornadas por conteúdos/tarefas; validação humana. |
| Quiz/SCORM: tentativas e desempenho detalhado | D | Não produzir diagnóstico desses instrumentos. |
| Pesquisas e satisfação | D | Relatórios pós-execução não medem satisfação. |
| Broadcast nativo por grupo/curso | D | Mensagens atuais são envios individuais compostos. |
| Scheduler/worker/cancelamento de comunicações | D | Não prometer agendamento nem execução futura. |
| Fontes presenciais, SGE e decisões oficiais | D/H | Conselho, competência, recuperação e resultado acadêmico permanecem humanos. |
| Snapshots históricos confiáveis | D | Não afirmar tendência com leituras pontuais. |

## Migração das fases técnicas 0–20

Esta tabela preserva rastreabilidade histórica; as jornadas são a organização canônica. Status foi revisto contra código e testes em 7 de julho de 2026.

| Fase antiga | Jornada(s) | Status comprovado | Evidência / lacuna principal |
| --- | --- | --- | --- |
| 0 — Base de segurança e contrato MCP | 7 | concluído | Contratos, pending actions e testes de segurança existentes. |
| 1 — Autenticação, escopos e identidade | 7 | concluído | OAuth/JWT/API key, resolução de conexão e testes de integração. |
| 2 — Cursos | 1, 7 | concluído | Queries/tools e testes de cursos. |
| 3 — Participantes | 1, 2 | concluído | Participantes/estudantes/grupos implementados e testados. |
| 4 — Conteúdos e estrutura | 1 | concluído | Conteúdos, estrutura e checklist com testes. |
| 5 — Atividades | 1, 2 | parcial | Inventário/tarefas/prazos existem; quiz/SCORM detalhados não contratados. |
| 6 — Entregas e submissões | 2, 3 | concluído | `listar_alunos_pendentes_atividade` e fluxos de submissão possuem implementação/testes; status antigo estava desatualizado. |
| 7 — Avaliações e notas em leitura | 3 | parcial | Gradebook individual testado; coletivo segue agregação Nível B. |
| 8 — Progresso, conclusão e participação | 2 | parcial | Completion, acesso, fórum e submissões existem; semântica uniforme de vazio/cobertura falta. |
| 9 — Risco e acompanhamento | 4 | parcial | Relatório de risco testado; contrato pedagógico e recuperação estruturada faltam. |
| 10 — Relatórios | 6 | parcial | Cinco famílias de relatório existem; fontes externas e cobertura uniforme faltam. |
| 11 — Comunicação confirmada | 5 | parcial | Seis pares prepare/confirm existem; `MessagesWriteEnabled` ainda não é efetiva. |
| 12 — Agendamento | 5 | planejado | Sem scheduler/worker/cancelamento; Nível D no contrato atual. |
| 13 — Feedback assistido | 3 | parcial | Fluxo em lote substancialmente testado; qualidade/contexto dependem de revisão. |
| 14 — Notas e avaliação crítica | 3, 7 | parcial | Nota individual prepare/confirm existe; default seguro da flag e decisão pedagógica permanecem pendentes. |
| 15 — Conteúdo com escrita | 1, 5 | planejado | Escrita geral de conteúdo não implementada; publicação de fórum é capability separada. |
| 16 — Administração de sala | 1, 7 | planejado | Leitura/checklist existem; mutação administrativa não. |
| 17 — Auditoria, observabilidade e suporte | 7 | parcial | Auditoria e healthcheck existem; métricas/alertas completos não comprovados. |
| 18 — Hardening de produção | 7 | parcial | Controles e checklist existem; validação integral depende do ambiente. |
| 19 — Documentação e handoff | 7 | parcial | Runbooks/documentação existem; aceite operacional de handoff não é comprovado pelo repositório. |
| 20 — Monitor | 1, 2, 6 | parcial | Checklist e relatório do monitor testados; fontes administrativas externas e diagnóstico de acesso faltam. |

## Backlog priorizado por jornada

### P0 — bloqueadores transversais

| Jornada(s) | Melhoria local comprovadamente pendente | Critério de conclusão |
| --- | --- | --- |
| 5, 7 | Tornar `MessagesWriteEnabled` efetiva no registro e/ou na execução de todas as tools reais de mensagem. | `false` impede preparo/confirmação e envio; testes cobrem ambos os estados sem registrar segredo. |
| 3, 7 | Alterar o default de `AssignmentGradeWriteEnabled` para `false` e verificar overrides de deploy. | Configuração versionada segura; teste prova bloqueio por default e liberação explícita. |
| 1, 2, 5, 6 | Uniformizar paginação pública **1-based** e rejeitar página menor que 1. | Contratos/tools/testes não expõem página zero. |
| 2, 3, 4, 6 | Expor elegíveis, analisados, excluídos, limites, falhas e `isTruncated` em agregações. | Cada resposta coletiva declara denominador e cobertura, inclusive falha parcial. |
| 1, 2, 3, 4, 6 | Implementar estados vazios não ambíguos (`zero_observado`, indisponibilidade, permissão, configuração, truncamento e falha). | Testes distinguem todos os estados; lista vazia isolada não sustenta conclusão. |
| 1, 3, 5, 7 | Completar testes dedicados dos fluxos classificados como Nível A, inclusive função ausente, permissão e todos os gates de escrita. | Cada ficha Nível A aponta para teste de sucesso e degradação/bloqueio. |

### Dependências do administrador Moodle e institucionais

- **Jornadas 1–7:** publicar um perfil de menor privilégio por função/contexto e validar capabilities em ambiente alvo; catálogo não substitui o teste contextual.
- **Jornadas 2, 4 e 6:** decidir e configurar completion; quando ausente, retornar `nao_configurado`.
- **Jornada 3:** aprovar o mapeamento SA-capacidade-critério-rubrica-conversão e as regras de nota.
- **Jornadas 5 e 7:** autorizar canal, escopos e política de retenção/auditoria para mensagens e demais escritas.
- **Jornadas 4 e 6:** integrar fontes presenciais/SGE somente após contrato, base legal, autoridade e reconciliação de identidade definidos.

### P1/P2 — evolução após os bloqueadores

- **P1, Jornadas 4 e 6:** adotar o contrato pedagógico com `findings`, `possibleRisks`, `recommendedActions`, `limitations`, `suggestedAudience` e `requiresHumanReview=true`.
- **P1, Jornada 3:** estruturar acompanhamento de recuperação sem automatizar deliberação.
- **P1, Jornada 7:** completar métricas, correlação, alertas, restore testado e evidência de handoff.
- **P2, Jornada 5:** estudar scheduler e cancelamento somente com infraestrutura persistente, idempotência e governança aprovadas.
- **P2, Jornadas 2, 3 e 6:** avaliar APIs/armazenamento para gradebook coletivo e snapshots; até lá manter Nível B/D explícito.
