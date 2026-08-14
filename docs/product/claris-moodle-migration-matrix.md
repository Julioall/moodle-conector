# Claris → Moodle Connector: matriz de migração

## Decisão arquitetural

Fonte auditada das mensagens: C:/Users/Julio/Desktop/Repositorios/claris/src/features/messages (fora da pasta web do Claris). O runtime portado fica em src/MoodleConnector.Web/src/features/messages e usa os gateways internos do Connector.

O portal será construído dentro do Moodle Connector existente. Moodle é a fonte
acadêmica operacional; o Connector concentra autorização, PostgreSQL, REST do
Moodle, ações pendentes, auditoria, MCP e os workers internos. Integrações
externas não fazem parte do núcleo de conclusão.

| Área do Claris | Dependência no Connector | Estado | Próxima decisão |
| --- | --- | --- | --- |
| Shell, autenticação e navegação | `MOODLE_CONNECTOR_INTERNAL` | Portado | Manter o shell e a seleção de conexão Moodle. |
| Dashboard e indicadores | `MOODLE_NATIVE` + `MOODLE_CONNECTOR_INTERNAL` | Portado com lacunas explícitas | Evoluir métricas somente quando houver evidência Moodle ou dado local confiável. |
| Cursos e detalhe do curso | `MOODLE_NATIVE` | Portado | Continuar enriquecendo sem inventar estatísticas ausentes no gateway. |
| Escolas | `MOODLE_CONNECTOR_INTERNAL` + escopo Moodle | Portado | Preservar agrupamento local e vínculo com conexões. |
| Alunos, perfil e participantes | `MOODLE_NATIVE` | Portado | Priorizar acesso, conclusão, notas e evidências por curso. |
| Atividades e submissões | `MOODLE_NATIVE` | Portado | Contexto da entrega, arquivos, texto online, prazo, nota e feedback ficam no fluxo Moodle-first. |
| Pendências/correções | `MOODLE_NATIVE` + `MOODLE_CONNECTOR_INTERNAL` | Portado | Fila aguardando correção, acompanhamento, evidência e lançamento manual confirmado. |
| Mensagens individuais | `MOODLE_NATIVE` | Portado com aprovação | `prepare → preview → PendingAction → aprovação → Moodle → auditoria`. |
| Follow-up | `MOODLE_CONNECTOR_INTERNAL` + evidência Moodle | Portado | Amarrar registros de acompanhamento a evidências observáveis. |
| Tarefas e agenda | `MOODLE_CONNECTOR_INTERNAL` | Portado | Evoluir para lembretes e execuções idempotentes. |
| Relatórios e auditoria | `MOODLE_NATIVE` + `MOODLE_CONNECTOR_INTERNAL` | Portado | Histórico, sinais, decisões, falhas e ações confirmadas ficam preservados. |
| Automações acadêmicas | `MOODLE_CONNECTOR_INTERNAL` | Portado | Condições, ações, execução, retry, idempotência e histórico no runtime .NET. |
| Scheduler/workers internos | `MOODLE_CONNECTOR_INTERNAL` | Portado | Scheduler hospedado executa rotinas recorrentes com escopo de conta e conexão. |
| Campanhas Moodle-first | `MOODLE_NATIVE` + `MOODLE_CONNECTOR_INTERNAL` | Portado | Campanhas começam com mensagens Moodle preparadas e aprovação humana. |
| MCP e ferramentas existentes | `MOODLE_CONNECTOR_INTERNAL` | Preservado | Expandir somente mantendo escopos, PendingAction e auditoria. |
| Claris IA/chatbot interno | `EXTERNAL_DEPENDENCY` | Adiado | Não criar chatbot LLM interno para fechar o núcleo acadêmico. |
| WhatsApp/Evolution e provedores externos | `EXTERNAL_DEPENDENCY` | Adiado | Não adicionar API keys, containers ou secrets; retomar somente com decisão explícita. |

## Classificação de dependências

- `MOODLE_NATIVE`: dado ou ação disponível no Moodle REST, com o Connector
  controlando escopo, permissões e erros.
- `MOODLE_CONNECTOR_INTERNAL`: estado operacional, scheduler, auditoria,
  tarefas, campanhas e workers mantidos em PostgreSQL/.NET.

## Matriz revisada de prioridade

| Feature | Fonte primária | Dependência externa | Prioridade | Decisão |
| --- | --- | --- | --- | --- |
| Dashboard e indicadores | Moodle REST + PostgreSQL do Connector | Nenhuma no núcleo | Alta | Manter indicadores com evidência; sinalizar lacunas quando o Moodle não fornecer o dado. |
| Cursos, escolas e participantes | Moodle REST + contexto interno | Nenhuma no núcleo | Alta | Usar Moodle para catálogo e participantes; usar PostgreSQL para agrupamentos locais. |
| Atividades, submissões e arquivos | Moodle REST | Nenhuma no núcleo | Alta | Priorizar prazo, status, arquivos, texto online e contexto da entrega. |
| Correções, notas e feedback | Moodle REST + PendingAction/auditoria | Nenhuma no núcleo | Crítica | Exigir revisão humana, confirmação exata e registrar resultado; sem correção automática por IA. |
| Conclusão, último acesso e fóruns | Moodle REST | Nenhuma no núcleo | Alta | Expor leitura com escopo de conexão e curso; manter incerteza explícita. |
| Mensagens e campanhas | Moodle REST + PendingAction/auditoria | Nenhuma no núcleo | Crítica | Preparar, pré-visualizar, aprovar e enviar pelo Moodle; campanhas começam nesse canal. |
| Mensagens individuais do Claris (`src/features/messages/MessagesPage.tsx`) | Moodle REST via Connector | Nenhuma no núcleo | P0 | PORT: preservar a composição inbox/conversa e substituir Supabase pelo gateway Moodle do Connector. |
| BulkSendTab e MessageTemplatesTab do Claris | Supabase Edge Functions e jobs externos ao Connector | Sim | P3 | DEFER_EXTERNAL_INTEGRATION: manter apenas como referência; campanhas atuais usam Moodle + PendingAction. |
| Follow-up, tarefas e agenda | PostgreSQL do Connector + evidências Moodle | Nenhuma no núcleo | Alta | Criar ou atualizar tarefas de forma idempotente e preservar a evidência observada. |
| Relatórios, evidências e histórico | PostgreSQL do Connector + Moodle REST | Nenhuma no núcleo | Alta | Guardar sinais, execuções, falhas, decisões e vínculo com a fonte. |
| Automações e scheduler/workers | PostgreSQL do Connector + workers .NET | Nenhuma no núcleo | Crítica | Executar internamente com retry, idempotência, histórico e aprovação para ações externas. |
| MCP existente | MCP do Connector + mesmas autorizações | Nenhuma no núcleo | Alta | Preservar ferramentas, escopos, PendingAction e auditoria antes de ampliar a superfície. |
| IA/chatbot interno | Fora do núcleo atual | Provedor de IA | Adiada | Não bloquear o núcleo Moodle-first com chatbot, chave ou serviço novo. |
| WhatsApp/Evolution e outros canais | Fora do núcleo atual | APIs, containers e secrets externos | Adiada | Não adicionar nesta tranche; reavaliar somente após decisão explícita. |

Status apos a tranche atual: o runtime interno de automacoes ja possui
definicoes, PostgreSQL, scheduler hospedado, execucao manual, retries,
idempotencia, historico e acoes Moodle-first. O portal expoe CRUD e historico;
acoes de mensagem continuam criando PendingAction para aprovacao humana.
- Validacao read-only real no Moodle passou para conexao, cursos, atividades e conversas; a conta de teste nao tinha `core_enrol_get_enrolled_users`, e o portal agora explicita essa capacidade ausente sem retornar erro 500.
- `EXTERNAL_DEPENDENCY`: fornecedor ou capacidade que não é necessária para o
  núcleo Moodle-first e deve permanecer fora da implementação atual.

## Ondas de execução

1. **Fundação:** matriz, design system, shell, conexões e contratos seguros.
2. **Acadêmica Moodle-first:** cursos, alunos, participantes, atividades,
   submissões, correções, notas, conclusão, acesso, fóruns e evidências.
3. **Mensagens Moodle:** leitura, preparação, prévia, confirmação, envio e
   auditoria; campanhas começam por esse canal.
4. **Automação interna:** condições, ações Moodle, scheduler, workers, retry,
   idempotência, execução, aprovação e histórico.
5. **MCP:** ampliar a superfície somente sobre os fluxos já auditáveis.
6. **Integrações externas:** avaliar depois, sem bloquear a conclusão do
produto Moodle-first.

## Status da tranche de integração Moodle-first

- A fila de pendências agora consulta sinais do Moodle, submissões por atividade, arquivos e texto online.
- A correção manual usa prévia, PendingAction, confirmação exata, escrita no Moodle e auditoria; não há correção automática por IA.
- O runtime interno registra evidências duráveis e pode gerar resumo semanal, além de manter scheduler, retries, histórico e idempotência.
- Campanhas usam sinais do Moodle para preparar mensagens Moodle; fóruns foram expostos em leitura no portal.
- IA interna, WhatsApp/Evolution, novas chaves e provedores externos continuam adiados.

## Definition of Done do núcleo

O núcleo estará concluído quando o portal Claris-like operar com Moodle,
Moodle Connector, PostgreSQL e Moodle REST; cobrir o fluxo acadêmico; enviar
mensagens pelo Moodle com aprovação e auditoria; executar automações internas
com scheduler/workers; oferecer campanhas Moodle-first; e preservar o MCP.
IA, WhatsApp e outros provedores externos não são pré-requisitos.
