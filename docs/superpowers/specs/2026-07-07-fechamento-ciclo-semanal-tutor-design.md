# Fechamento do Ciclo Semanal do Tutor

## Objetivo

Concluir as fases 7, 8, 9 e 11 do roadmap com comportamento demonstrável, cobertura automatizada e documentação coerente, permitindo que o tutor identifique estudantes que precisam de atenção e prepare mensagens personalizadas com confirmação humana.

## Escopo

### Fase 7 — avaliações e desempenho

- Expor desempenho individual por atividade ou Situação de Aprendizagem, preservando itens sem nota.
- Permitir um mínimo configurável para sinalização, sem converter automaticamente nota em diagnóstico de competência.
- Representar ausência de nota como dado ausente, não como nota zero.
- Cobrir curso sem gradebook, item sem nota, estudante sem nota e ausência de permissão.

### Fase 8 — progresso e participação

- Consolidar as tools existentes de estudantes sem acesso recente, sem participação em fórum e com atividade pendente.
- Completar testes de fórum e submissões para dados ausentes, filtros, prazos e permissões.
- Expor limitações quando conclusão, acesso ou participação não estiverem disponíveis no Moodle.
- Manter os resultados como sinais de acompanhamento, nunca como confirmação de evasão.

### Fase 9 — risco e acompanhamento

- Evoluir o relatório de risco para separar explicitamente:
  - achados observáveis;
  - possíveis riscos;
  - ações recomendadas;
  - limitações dos dados;
  - público sugerido para mensagem de acompanhamento.
- Manter limiares de inatividade e desempenho configuráveis.
- Não classificar definitivamente aprovação, reprovação, competência ou evasão.
- Cobrir estudante sem nota, dados parciais, limiares customizados e falhas parciais dos gateways.

### Fase 11 — comunicação

- Preservar os seis fluxos existentes de preparação e confirmação de mensagens.
- Demonstrar por testes: criação de `PendingAction`, confirmação, envio, expiração, idempotência, divergência do texto de confirmação, auditoria e validação de destinatários.
- Garantir que a prévia informe curso, critérios de seleção, quantidade de destinatários e corpo sanitizado.
- Não incluir nota ou risco individual em mensagens coletivas.
- Para recuperação, orientar período, atividade esperada e canal de apoio sem sentenciar reprovação.

### Hardening e documentação

- Alterar `AssignmentGradeWriteEnabled` para `false` na configuração padrão; ambientes autorizados continuam podendo habilitá-la explicitamente.
- Reconciliar o roadmap com as evidências reais das fases 6–11, 14 e 20, sem marcar critérios sem teste ou implementação.
- Registrar pendências posteriores, como agendamento, feedback individual e escrita estrutural, sem incorporá-las nesta entrega.

## Fundamentos pedagógicos

As decisões devem ser validadas contra `public/pedagogic`.

- Avaliação é contínua e combina funções diagnóstica, formativa e somativa.
- Critérios representam desempenho observável e precisam ser objetivos e vinculados às capacidades.
- Nota isolada não comprova competência.
- Recuperação oferece nova oportunidade e explicita critérios, período, atividade e orientação.
- Participação, acesso e nota são sinais para acompanhamento humano, não diagnósticos definitivos.
- Feedback e mensagens devem comunicar evidências, potencialidades, fragilidades e próximos passos com linguagem cautelosa.

Referências principais:

- `public/pedagogic/METODOLOGIA SENAI DE EDUCACAO PROFISSIONAL.md`
- `public/pedagogic/Guia do Tutor - Com ISBN 1 (6).md`
- `public/pedagogic/Guia de Desenvolvimento de Situação de Aprendizagem.md`
- `docs/roadmap.md`, seções de contexto pedagógico, fases 7–11 e política de privacidade.

## Arquitetura

O trabalho seguirá as fronteiras existentes:

- queries e handlers em `MoodleConnector.Application` agregam e classificam dados;
- gateways existentes fornecem gradebook, participantes, conclusão, fóruns e submissões;
- tools em `MoodleConnector.Presentation` apenas validam entrada, enviam comandos/queries e formatam o contrato MCP;
- `PendingAction` continua sendo a fronteira obrigatória antes de qualquer envio de mensagem;
- testes unitários exercitam handlers e contratos das tools sem depender de um Moodle real.

Não haverá reescrita ampla dos domínios. Tipos novos serão adicionados somente quando necessários para tornar explícita a separação pedagógica do relatório de risco.

## Fluxo de dados

1. O tutor consulta desempenho, acesso, participação ou pendências.
2. Os handlers retornam fatos observáveis e limitações de fonte.
3. O relatório de risco combina os fatos usando limiares informados, mantendo achados separados de hipóteses e recomendações.
4. O resultado oferece IDs de destinatários sugeridos, sem enviar mensagens automaticamente.
5. Uma tool de preparação recebe destinatários e conteúdo, cria uma ação pendente e devolve a prévia.
6. Somente uma confirmação textual válida executa o envio; repetição retorna o resultado idempotente.

## Erros e segurança

- Dados ausentes produzem limitações explícitas e listas vazias válidas quando aplicável.
- Falha parcial de uma fonte não apaga achados obtidos de outras fontes.
- Ausência de escopo ou permissão retorna erro seguro, sem dados pessoais desnecessários.
- Logs e auditoria não armazenam tokens, notas coletivas, conteúdo sensível integral ou identificadores além do necessário.
- Escrita de notas permanece desabilitada por padrão.

## Estratégia de testes

Cada lacuna será implementada por TDD:

- testes de contrato e handler para desempenho individual e mínimos configuráveis;
- testes dedicados para ausência em fórum e atividades pendentes;
- testes de estrutura pedagógica e dados incompletos no relatório de risco;
- testes completos do handler de confirmação de mensagens e das tools críticas;
- teste da configuração segura por padrão;
- suíte completa `dotnet test MoodleConnector.slnx --no-restore`.

## Critérios de aceite

- As fases 7, 8, 9 e 11 possuem implementação e testes correspondentes aos critérios marcados como concluídos.
- O tutor consegue obter destinatários sugeridos a partir de fatos observáveis e preparar uma mensagem sem envio automático.
- O relatório diferencia fatos, possíveis riscos, recomendações e limitações.
- Mensagens exigem confirmação humana, são idempotentes e auditáveis.
- A configuração padrão não habilita escrita real de notas.
- O roadmap diferencia claramente concluído, parcial e planejado.
- Toda a suíte automatizada passa sem testes ignorados.

## Fora do escopo

- Agendamento e cancelamento de mensagens da fase 12.
- Feedback individual fora de lote da fase 13.
- Escrita de conteúdo e administração de sala das fases 15 e 16.
- Aprovação institucional de governança ou mudanças no Moodle de produção.
