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
