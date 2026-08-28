---
name: moodle-localizar-contexto-curso
description: Resolver com segurança a conexão, o curso, a unidade curricular e os identificadores Moodle antes de qualquer análise ou ação.
---

# moodle-localizar-contexto-curso

Use esta skill como preflight quando o pedido mencionar uma turma, curso, UC, código, atividade ou conexão Moodle, especialmente antes de correção, auditoria, recuperação ou publicação.

## Objetivo

Produzir um contexto verificável para as demais skills:

- conexão/alias efetivamente selecionado;
- curso encontrado e evidência usada para a resolução;
- nome, short name, idnumber e courseId;
- estrutura de seções e módulos quando necessária;
- UC, atividade, questionário ou recurso alvo;
- cmid, instanceId, assignmentId e quizId, sem intercambiá-los;
- capacidades e limites de paginação relevantes.

## Sequência

1. Se o usuário forneceu um alias exato, preserve-o. Se não forneceu, use a conexão padrão; não deduza alias por instituição, região ou histórico.
2. Para resolver o curso, use `search_courses`, `list_my_courses`, `get_course` ou os formatos canônicos `search`/`fetch`.
3. Se houver mais de um resultado plausível, não escolha silenciosamente: apresente as opções e peça desambiguação.
4. Consulte `moodle_list_available_flows` quando houver mais de uma estratégia ou quando a capability da conexão puder alterar a rota.
5. Só leia conteúdos, atividades e prazos depois de confirmar o curso; use `list_course_contents`, `list_course_activities`, `list_course_assignments`, `list_course_quizzes`, `get_course_activity` e `list_activity_deadlines` conforme o objetivo.
6. Entregue um resumo de contexto para a skill seguinte, incluindo origem, timestamps, paginação, `hasMore`/truncamento e capabilities ausentes.

## Regras de identificação

- Nunca invente ou derive `courseId`, `assignmentId`, `quizId`, `cmid`, `instanceId` ou `userid`.
- Um `cmid` identifica um módulo do curso; não o trate automaticamente como instância da atividade.
- Não substitua um curso explicitamente solicitado por um curso “parecido”.
- Resultado vazio, acesso negado, capability ausente e página incompleta são estados diferentes.
- Não baixe entregas ou arquivos quando o pedido exige apenas resolver a estrutura.

## Limite

Esta skill é somente leitura. Ela não corrige, cria, edita, publica, envia mensagens, lança notas nem remove objetos. Depois de resolver o contexto, encaminhe para a skill de domínio adequada.
