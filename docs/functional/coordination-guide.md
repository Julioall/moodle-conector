# Guia para Coordenação Pedagógica

## Objetivo

Apoiar a coordenação no entendimento do estado atual e do roadmap do MoodleConnector.

## Disponível hoje

- Autenticação via broker OAuth local e API key opcional.
- Leitura de cursos do usuário autenticado.
- Base de auditoria e pending actions.
- Demonstração de fluxo prepare/confirm sem escrita real.

## Não implementado ainda

- Painéis de coordenação.
- Consulta agregada de múltiplos professores.
- Relatórios institucionais.
- Identificação consolidada de alunos em risco.
- Envio de comunicados reais.
- Ações administrativas no Moodle.

## Planejado

O roadmap foi reorganizado por domínios funcionais. A ordem prática inicial é:

- Domínio Cursos: listar, buscar e consultar cursos.
- Domínio Participantes: estudantes, grupos e acessos.
- Domínio Conteúdos e estrutura: seções, módulos e recursos da sala.
- Domínio Atividades: tarefas, quizzes, SCORMs e prazos.
- Domínios Entregas, Avaliações, Progresso, Risco e Relatórios.

Observabilidade operacional continua planejada em fase própria:

- observabilidade operacional;
- consulta protegida de audit logs;
- troubleshooting por correlation id.

## Recomendações

- Validar primeiro as tools de leitura com um grupo pequeno de professores.
- Definir escopos por perfil antes de habilitar escritas.
- Exigir revisão pedagógica para qualquer fluxo de nota ou feedback em lote.
