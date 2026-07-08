# Roadmap Orientado ao Guia do Tutor e às Capabilities

## Objetivo

Reestruturar o roadmap do Moodle Connector para refletir sua finalidade institucional: apoiar tutores, monitores e corpo pedagógico na execução das atividades previstas no Guia do Tutor. As funções Moodle e os serviços MCP deixam de definir o propósito do produto e passam a delimitar, com transparência, o nível de apoio tecnicamente possível.

## Princípio central

O produto organiza seu trabalho pela jornada pedagógica e pelas responsabilidades humanas. Para cada atividade, o roadmap declara:

- qual público realiza ou supervisiona a atividade;
- qual orientação do Guia do Tutor fundamenta a atividade;
- quais evidências são necessárias;
- quais evidências o Moodle autorizado consegue fornecer;
- como o MCP pode apoiar a atividade;
- quais limitações impedem conclusões automáticas;
- qual decisão ou confirmação permanece humana.

O conector não substitui tutor, monitor, CTM, coordenação ou regras do Departamento Regional. Ele coleta evidências disponíveis, organiza informações, produz rascunhos e executa ações explicitamente autorizadas.

## Públicos

### Tutor

- acompanhar participação e rendimento;
- orientar estudos e atividades;
- responder dúvidas de conteúdo;
- corrigir entregas e oferecer feedback;
- identificar sinais que merecem atenção;
- apoiar recuperação e novas oportunidades;
- comunicar-se com estudantes mediante revisão humana.

### Monitor

- apoiar ambientação e acesso ao AVA;
- verificar a organização inicial da sala virtual;
- orientar sobre navegação e questões administrativas;
- acompanhar registros operacionais de acesso;
- encaminhar ocorrências ao tutor ou corpo pedagógico.

### Corpo pedagógico e CTM

- supervisionar o processo formativo;
- analisar evidências do presencial e do EaD;
- deliberar sobre intervenções e recuperação;
- validar critérios, instrumentos e conversões de resultados;
- acompanhar relatórios e limitações das fontes;
- tomar decisões acadêmicas que não podem ser automatizadas.

## Estrutura do roadmap

### Jornada 1 — preparação e ambientação

Abrange checklist da sala, materiais, fóruns, datas, canais de apoio, apresentação, boas-vindas e orientação inicial.

O Moodle atual permite inspecionar conteúdos, atividades, fóruns e datas visíveis. A verificação de finalidade pedagógica, qualidade do material, separação adequada dos canais e coerência com o plano permanece humana.

### Jornada 2 — acompanhamento semanal

Abrange acesso registrado, entregas, participação observável, dúvidas, progresso e pendências.

O conector pode apresentar fatos retornados pelo Moodle, como último acesso registrado, submissões visíveis, posts encontrados e completion configurado. Não pode inferir frequência real de estudo, esforço, compreensão, motivação ou evasão.

### Jornada 3 — avaliação e feedback

Abrange critérios, evidências, correção, feedback, registros de nota e novas oportunidades.

O conector pode ler itens e notas visíveis, tarefas, submissões e feedback registrado. Só pode falar em desempenho por Situação de Aprendizagem ou capacidade quando existir mapeamento explícito para SA, capacidades, critérios, rubrica e regra de conversão. Nota isolada nunca comprova competência.

### Jornada 4 — recuperação e intervenção pedagógica

Abrange identificação de sinais, análise humana, orientação, nova oportunidade e acompanhamento.

O conector pode apontar estudantes “em atenção” com base em limiares configurados e evidências observáveis. Dados ausentes reduzem a confiança e produzem limitações; nunca aumentam automaticamente o risco. A decisão de recuperação e o plano de intervenção pertencem ao tutor e à equipe pedagógica.

### Jornada 5 — comunicação

Abrange mensagens de ambientação, orientação, pendência, acompanhamento, encerramento e recuperação.

O conector pode sugerir destinatários, criar rascunhos e enviar mensagens individuais após confirmação humana. A prévia precisa mostrar critérios, evidências, exclusões, destinatários e corpo sanitizado. Não há broadcast nativo de turma no contrato atual.

### Jornada 6 — relatórios e coordenação

Abrange relatórios descritivos para tutor, monitor, docente presencial, CTM e conselho.

Todo relatório deve declarar fonte, período, cobertura, denominador, itens excluídos, truncamento e limitações. O Moodle não contém necessariamente dados presenciais, SGE, satisfação ou decisões oficiais; portanto o conector não produz conselho de classe, aprovação, reprovação ou evasão como conclusão automática.

### Jornada 7 — operação e governança

Abrange capabilities, permissões, feature flags, confirmação humana, privacidade, auditoria, observabilidade, hardening, deploy e documentação.

Esta jornada sustenta as demais e garante que capabilities ausentes desabilitem ou degradem funcionalidades de forma explícita.

## Ficha obrigatória de cada atividade

Cada item do novo roadmap deve conter:

- **Atividade do guia:** nome orientado à tarefa humana.
- **Público:** tutor, monitor, corpo pedagógico ou combinação.
- **Referência pedagógica:** arquivo e seção de `public/pedagogic`.
- **Resultado humano esperado:** o que a pessoa precisa conseguir fazer.
- **Evidências necessárias:** dados ideais para apoiar a atividade.
- **Evidências disponíveis:** dados efetivamente retornados pelo serviço Moodle.
- **Funções Moodle:** funções obrigatórias e opcionais.
- **Tool MCP:** tool existente ou planejada.
- **Nível de suporte:** classificação definida abaixo.
- **Cobertura:** limites de estudantes, páginas, discussões, posts e chamadas.
- **Limitações:** permissões, configuração, fontes ausentes e ambiguidades.
- **Gate humano:** revisão, confirmação ou decisão obrigatória.
- **Status técnico:** implementado, parcial, planejado, bloqueado ou fora do escopo.
- **Evidência de conclusão:** testes, documentação e operação verificável.

## Níveis de suporte

### Nível A — suportado pelo serviço atual

A função Moodle necessária está no contrato atual, a tool possui implementação e os testes demonstram o comportamento. Ainda assim, permissões e presença dos dados devem ser verificadas em execução.

### Nível B — assistência com limitações

A atividade pode ser apoiada por agregação local, chamadas por estudante ou inferência cautelosa sobre fatos observáveis. A resposta deve informar cobertura, truncamento e limitações, sem transformar sinal em diagnóstico.

### Nível C — dependente de configuração ou administrador

A atividade exige função Moodle adicional, capability, plugin, configuração de completion, permissão institucional ou mapeamento pedagógico ainda não disponível. O roadmap registra a dependência e não promete execução no ambiente padrão.

### Nível D — não suportado pelo contrato atual

Não existe fonte ou infraestrutura suficiente. Exemplos atuais incluem histórico detalhado de navegação, broadcast nativo de turma, agendamento e tendências sem armazenamento de snapshots.

### Nível H — exclusivamente humano

Decisões de competência, recuperação, aprovação, reprovação, evasão, sanção, qualidade pedagógica e deliberação de conselho permanecem humanas mesmo que existam dados técnicos relacionados.

## Capabilities e limitações atuais

### Leitura individual e agregações

- `gradereport_user_get_grade_items` lê gradebook por estudante, não o gradebook coletivo de forma atômica.
- `core_completion_get_activities_completion_status` e `core_completion_get_course_completion_status` dependem de completion configurado e podem produzir ausência ambígua.
- `core_enrol_get_enrolled_users` fornece participantes e último acesso quando o campo estiver disponível.
- `mod_assign_get_assignments`, `mod_assign_get_submissions`, `mod_assign_get_submission_status` e `mod_assign_get_grades` cobrem tarefas, submissões e notas visíveis.
- `mod_forum_get_forums_by_courses`, `mod_forum_get_forum_discussions` e `mod_forum_get_discussion_posts` cobrem somente fóruns, discussões e posts visíveis ao token.

Agregações coletivas exigem múltiplas chamadas e hoje usam limites locais entre 60 e 100 estudantes. Participação em fórum pode limitar discussões e posts. Esses limites precisam aparecer no contrato e no resultado.

### Escrita

- `core_message_send_instant_messages` envia mensagens instantâneas individuais; não agenda nem realiza broadcast nativo por turma.
- `mod_assign_save_grade` permite escrita individual de nota quando função, escopo, conexão e feature flag autorizarem.
- Escritas reais devem permanecer desabilitadas por padrão e exigir `PendingAction`, prévia, confirmação humana, idempotência e auditoria.

### Capabilities não contratadas

- logs detalhados de navegação e sessões;
- gradebook coletivo eficiente;
- calendário completo;
- tentativas e desempenho detalhado de quiz;
- pesquisas e satisfação;
- broadcast nativo para grupo ou curso;
- scheduler e worker de mensagens;
- fontes presenciais, SGE e decisões acadêmicas oficiais.

## Semântica obrigatória de dados ausentes

As respostas devem distinguir:

- zero observado;
- dado não retornado;
- função indisponível;
- falta de permissão;
- recurso Moodle não configurado;
- resultado truncado;
- falha parcial da API.

Uma lista vazia não pode representar silenciosamente todos esses estados.

## Regras pedagógicas transversais

- Avaliação combina funções diagnóstica, formativa e somativa.
- Critérios devem representar desempenho observável e estar vinculados às capacidades.
- Nota ou completion isolados não comprovam competência.
- “Abaixo do mínimo” é um sinal numérico configurado, não prova automática de critério crítico não atingido.
- Recuperação exige análise, orientação, período, nova oportunidade e acompanhamento humano.
- Acesso, entrega e posts são registros técnicos, não medidas diretas de aprendizagem ou engajamento.
- Mensagens usam linguagem epistêmica: “não encontramos registro”, “nos dados visíveis” e “necessita verificação humana”.
- Nenhuma ação coletiva expõe nota, risco individual ou conteúdo sensível.

## Estratégia de reestruturação

1. Inventariar as atividades do Guia do Tutor por jornada e público.
2. Mapear cada atividade para evidências, funções Moodle e tools existentes.
3. Classificar o nível de suporte e o gate humano.
4. Corrigir status históricos que contradizem o código e os testes.
5. Rebaixar promessas incompatíveis com o contrato atual para dependência, não suportado ou exclusivamente humano.
6. Priorizar lacunas que completam jornadas humanas, não contagem de tools.
7. Criar um backlog técnico separado para capabilities, escala, segurança e operação.

## Critérios de aceite da revisão do roadmap

- O roadmap é navegável pelas sete jornadas e pelos três públicos.
- Toda atividade possui nível de suporte, funções Moodle, limitações e gate humano.
- Itens implementados apontam para código e testes.
- Itens sem evidência não aparecem como concluídos.
- Limites de paginação, cobertura e chamadas por estudante estão explícitos.
- Estados vazios e falhas parciais têm semântica documentada.
- Decisões pedagógicas não são apresentadas como automação.
- O backlog técnico distingue melhoria local de dependência do administrador Moodle.
- A configuração padrão respeita feature flags de escrita.

## Entrega posterior à revisão

Depois de aprovado o novo roadmap, a implementação deve começar pelos bloqueadores transversais já identificados:

1. fazer `MessagesWriteEnabled` controlar efetivamente o registro ou execução das mensagens;
2. manter escrita de notas desabilitada por padrão;
3. corrigir a inconsistência de paginação iniciada em zero;
4. tornar cobertura e truncamento explícitos nas agregações;
5. distinguir indisponibilidade de ausência de atividade ou conclusão;
6. completar testes dos fluxos que o roadmap classificar como Nível A.

## Fora do escopo desta especificação

- Implementar novas funções Moodle antes da aprovação do roadmap revisado.
- Criar scheduler, plugins Moodle ou armazenamento histórico.
- Automatizar decisões acadêmicas ou substituir deliberação pedagógica.
- Alterar configurações do Moodle institucional sem autorização administrativa.
