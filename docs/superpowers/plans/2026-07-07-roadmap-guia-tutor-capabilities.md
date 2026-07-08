# Roadmap Orientado ao Guia do Tutor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Reestruturar `docs/roadmap.md` pelas jornadas de tutor, monitor e corpo pedagógico, classificando cada atividade conforme as capabilities e limitações reais do Moodle/MCP.

**Architecture:** O roadmap terá uma parte normativa curta, sete jornadas operacionais com fichas de atividades e apêndices técnicos para matriz de capabilities, migração das fases antigas e backlog. A evidência virá de `public/pedagogic`, gateways/tools/testes existentes e `docs/technical/moodle-webservice-setup.md`; nenhuma atividade será marcada como concluída sem implementação e teste localizáveis.

**Tech Stack:** Markdown, ripgrep, Git, documentação e testes .NET existentes como fonte de evidência.

---

## Estrutura de arquivos

- Modify: `docs/roadmap.md` — fonte canônica reorganizada por jornadas e públicos.
- Modify: `docs/technical/moodle-webservice-setup.md` — alinhar lista de funções e limitações transversais descobertas.
- Modify: `docs/technical/mcp-tools-catalog.md` — corrigir somente descrições que contradigam níveis de suporte ou gates reais.
- Modify: `README.md` — apontar para a nova organização do roadmap, se o README ainda descrever fases técnicas como eixo principal.
- Reference: `docs/superpowers/specs/2026-07-07-roadmap-guia-tutor-capabilities-design.md` — desenho aprovado.
- Reference: `public/pedagogic/*.md` — fundamentos pedagógicos.

### Task 1: Criar o esqueleto normativo e a legenda de suporte

**Files:**
- Modify: `docs/roadmap.md:1-169`
- Reference: `docs/superpowers/specs/2026-07-07-roadmap-guia-tutor-capabilities-design.md`

- [ ] **Step 1: Registrar a estrutura esperada antes da edição**

Run:

```powershell
rg -n "^# |^## |Nível A|Nível B|Nível C|Nível D|Nível H" docs/roadmap.md
```

Expected: o arquivo antigo contém fases por domínio e não contém os cinco níveis de suporte.

- [ ] **Step 2: Substituir a introdução por propósito, públicos e regras**

Escrever no início de `docs/roadmap.md`:

```markdown
# Roadmap de Apoio ao Tutor, Monitor e Corpo Pedagógico

## Propósito

O Moodle Connector apoia a execução das atividades previstas no Guia do Tutor. O produto organiza evidências, produz rascunhos e executa ações autorizadas; não substitui decisões pedagógicas.

## Públicos

- Tutor
- Monitor
- Corpo pedagógico e CTM

## Níveis de suporte

- **A — suportado:** função contratada, implementação e testes existentes.
- **B — assistido com limitações:** agregação local ou sinal que exige cobertura e revisão humana.
- **C — dependente de configuração/admin:** exige capability, permissão, plugin ou mapeamento institucional.
- **D — não suportado:** não existe fonte ou infraestrutura no contrato atual.
- **H — exclusivamente humano:** decisão acadêmica ou pedagógica não automatizável.
```

Incluir as regras de estados vazios: `zero_observado`, `dado_indisponivel`, `sem_permissao`, `nao_configurado`, `truncado` e `falha_parcial`.

- [ ] **Step 3: Validar a nova legenda**

Run:

```powershell
rg -n "A — suportado|B — assistido|C — dependente|D — não suportado|H — exclusivamente humano|zero_observado|falha_parcial" docs/roadmap.md
```

Expected: todos os níveis e estados aparecem uma vez na seção normativa.

- [ ] **Step 4: Commit**

```powershell
git add docs/roadmap.md
git commit -m "docs: orientar roadmap pelas jornadas pedagogicas"
```

### Task 2: Modelar preparação, ambientação e acompanhamento semanal

**Files:**
- Modify: `docs/roadmap.md`
- Reference: `public/pedagogic/Guia do Tutor - Com ISBN 1 (6).md`
- Reference: `src/MoodleConnector.Application/Monitor/Queries/AuditVirtualClassroomChecklistQuery.cs`
- Reference: `src/MoodleConnector.Application/Completion/Queries/GetStudentsWithoutRecentAccessQuery.cs`
- Reference: `src/MoodleConnector.Application/Forums/Queries/GetStudentsWithoutForumParticipationQuery.cs`
- Reference: `src/MoodleConnector.Application/Submissions/Queries/GetStudentsWithPendingSubmissionsQuery.cs`

- [ ] **Step 1: Escrever fichas da Jornada 1**

Adicionar fichas para:

```text
auditar organização da sala virtual
verificar materiais, fóruns e datas
apoiar ambientação e acesso
preparar mensagem de boas-vindas
```

Cada ficha deve conter público, referência pedagógica, resultado humano, evidências, funções Moodle, tool MCP, nível, cobertura, limitações, gate humano, status e testes.

- [ ] **Step 2: Escrever fichas da Jornada 2**

Adicionar fichas para:

```text
consultar último acesso registrado
identificar submissões não encontradas
identificar posts não encontrados em fórum visível
consultar completion configurado
acompanhar dúvidas e encaminhamentos
```

Usar linguagem factual: “não encontramos registro nos dados visíveis”. Registrar limites padrão de 100 estudantes e 20 discussões onde aplicável.

- [ ] **Step 3: Validar proibições semânticas**

Run:

```powershell
rg -n "detecta evasão|confirma evasão|não participou$|não estudou|sem engajamento" docs/roadmap.md
```

Expected: nenhum match em promessas do produto; ocorrências históricas devem ser removidas ou marcadas como proibidas.

- [ ] **Step 4: Commit**

```powershell
git add docs/roadmap.md
git commit -m "docs: mapear ambientacao e acompanhamento semanal"
```

### Task 3: Modelar avaliação, feedback, recuperação e intervenção

**Files:**
- Modify: `docs/roadmap.md`
- Reference: `public/pedagogic/METODOLOGIA SENAI DE EDUCACAO PROFISSIONAL.md`
- Reference: `public/pedagogic/Guia de Desenvolvimento de Situação de Aprendizagem.md`
- Reference: `src/MoodleConnector.Application/Gradebook/Queries`
- Reference: `src/MoodleConnector.Application/Risk/Queries`
- Reference: `src/MoodleConnector.Application/Grading`

- [ ] **Step 1: Escrever fichas da Jornada 3**

Cobrir gradebook individual, agregação local, tarefas/submissões, correção assistida, feedback e nota individual. Classificar gradebook coletivo como Nível B e mapeamento SA-capacidade-critério como Nível C quando não existir configuração explícita.

- [ ] **Step 2: Escrever fichas da Jornada 4**

Cobrir sinais configuráveis de inatividade/desempenho, relatório de atenção, recomendação de contato e acompanhamento de recuperação. Classificar decisão de recuperação, competência, aprovação, reprovação e evasão como Nível H.

- [ ] **Step 3: Registrar o contrato de saída pedagógica**

Adicionar o formato conceitual:

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

Marcar como backlog qualquer campo ainda ausente no contrato implementado.

- [ ] **Step 4: Validar linguagem pedagógica**

Run:

```powershell
rg -n "nota.*comprova competência|decide recuperação|aprovação automática|reprovação automática|evasão confirmada" docs/roadmap.md
```

Expected: nenhum match como capacidade automatizada.

- [ ] **Step 5: Commit**

```powershell
git add docs/roadmap.md
git commit -m "docs: delimitar avaliacao e intervencao pedagogica"
```

### Task 4: Modelar comunicação, relatórios e coordenação

**Files:**
- Modify: `docs/roadmap.md`
- Reference: `src/MoodleConnector.Application/Messages/TutorMessageCommands.cs`
- Reference: `src/MoodleConnector.Application/Reports/Queries`
- Reference: `src/MoodleConnector.Presentation/Tools/Messages/MoodleTutorMessageTools.cs`
- Reference: `src/MoodleConnector.Presentation/Tools/Reports/MoodleReportTools.cs`

- [ ] **Step 1: Escrever fichas da Jornada 5**

Documentar mensagens de boas-vindas, pendência, recuperação, encerramento e acompanhamento. Registrar `core_message_send_instant_messages`, envio individual, `PendingAction`, confirmação, idempotência e auditoria. Marcar broadcast nativo e agendamento como Nível D.

- [ ] **Step 2: Escrever fichas da Jornada 6**

Documentar relatórios semanais, conselho, pós-execução, monitor e coordenação. Registrar limites atuais de 60–100 estudantes e ausência de dados presenciais, SGE, satisfação e decisões oficiais.

- [ ] **Step 3: Explicitar fontes e cobertura obrigatórias**

Adicionar a todos os relatórios a exigência conceitual de:

```text
source
collectedAt
period
coveredCount
eligibleCount
isTruncated
missingCapabilities
limitations
```

Campos ainda não implementados devem constar no backlog, não como entrega concluída.

- [ ] **Step 4: Commit**

```powershell
git add docs/roadmap.md
git commit -m "docs: delimitar comunicacao e relatorios"
```

### Task 5: Criar jornada operacional e matriz de capabilities

**Files:**
- Modify: `docs/roadmap.md`
- Modify: `docs/technical/moodle-webservice-setup.md`
- Reference: `src/MoodleConnector.Presentation/Configuration/FeatureOptions.cs`
- Reference: `src/MoodleConnector.Presentation/appsettings.json`
- Reference: `src/MoodleConnector.Infrastructure/*Gateway.cs`

- [ ] **Step 1: Escrever a Jornada 7**

Cobrir autenticação, escopos, permissões, descoberta de funções, feature flags, confirmação, privacidade, auditoria, observabilidade, hardening, deploy e handoff.

- [ ] **Step 2: Criar matriz das funções atuais**

Incluir ao menos:

```text
core_webservice_get_site_info
core_enrol_get_users_courses
core_enrol_get_enrolled_users
core_course_get_contents
core_completion_get_activities_completion_status
core_completion_get_course_completion_status
mod_forum_get_forums_by_courses
mod_forum_get_forum_discussions
mod_forum_get_discussion_posts
mod_assign_get_assignments
mod_assign_get_submissions
mod_assign_get_submission_status
mod_assign_get_grades
gradereport_user_get_grade_items
core_message_send_instant_messages
mod_assign_save_grade
```

Para cada função, registrar finalidade, nível de suporte, permissão/configuração e comportamento quando ausente.

- [ ] **Step 3: Registrar capabilities não contratadas**

Adicionar logs detalhados, gradebook coletivo eficiente, calendário, quiz, pesquisas, broadcast, scheduler e fontes externas como dependências ou Nível D.

- [ ] **Step 4: Alinhar o setup do webservice**

Atualizar `docs/technical/moodle-webservice-setup.md` para incluir `core_message_send_instant_messages` e declarar que disponibilidade no catálogo não prova permissão contextual.

- [ ] **Step 5: Commit**

```powershell
git add docs/roadmap.md docs/technical/moodle-webservice-setup.md
git commit -m "docs: documentar capabilities e limites do moodle"
```

### Task 6: Migrar fases antigas para backlog rastreável

**Files:**
- Modify: `docs/roadmap.md`
- Modify: `docs/technical/mcp-tools-catalog.md`
- Modify: `README.md`

- [ ] **Step 1: Criar tabela de migração**

Mapear as fases 0–20 antigas para jornadas, preservando histórico e indicando `concluído`, `parcial`, `planejado`, `bloqueado` ou `fora do escopo`.

- [ ] **Step 2: Corrigir contradições conhecidas**

Reconciliar:

```text
Fase 6 versus listar_alunos_pendentes_atividade
Fase 8 versus tools de acesso/fórum/submissões existentes
Fase 11 versus seis pares prepare/confirm existentes
Fase 14 versus nota individual implementada parcialmente
Fase 20 versus checklist/relatório do monitor existentes
```

Não marcar como concluído critério sem teste dedicado.

- [ ] **Step 3: Criar backlog priorizado por jornada**

Prioridade 0:

```text
feature flag de mensagens efetiva
escrita de notas desabilitada por padrão
paginação iniciada em 1
cobertura/truncamento explícitos
estado vazio não ambíguo
testes dos fluxos Nível A
```

Separar melhoria local de dependência do administrador Moodle.

- [ ] **Step 4: Atualizar referências externas**

Corrigir README e catálogo apenas onde apontarem para fases antigas como organização canônica ou fizerem promessas incompatíveis com o novo roadmap.

- [ ] **Step 5: Commit**

```powershell
git add docs/roadmap.md docs/technical/mcp-tools-catalog.md README.md
git commit -m "docs: migrar fases para backlog orientado ao guia"
```

### Task 7: Verificar consistência e preparar o próximo ciclo

**Files:**
- Modify: `docs/roadmap.md` only if verification reveals issues

- [ ] **Step 1: Verificar estrutura obrigatória**

Run:

```powershell
rg -n "Jornada 1|Jornada 2|Jornada 3|Jornada 4|Jornada 5|Jornada 6|Jornada 7|Nível A|Nível B|Nível C|Nível D|Nível H" docs/roadmap.md
```

Expected: sete jornadas e cinco níveis presentes.

- [ ] **Step 2: Verificar rastreabilidade técnica**

Run:

```powershell
rg -n "gradereport_user_get_grade_items|core_completion_get_activities_completion_status|core_message_send_instant_messages|mod_assign_save_grade|isTruncated|requiresHumanReview" docs/roadmap.md
```

Expected: todas as funções e contratos críticos documentados.

- [ ] **Step 3: Verificar links e diff**

Run:

```powershell
git diff --check
git status --short
```

Expected: nenhum erro de whitespace e apenas arquivos documentais planejados alterados.

- [ ] **Step 4: Rodar testes como controle de integridade**

Run:

```powershell
dotnet test MoodleConnector.slnx --no-restore
```

Expected: todos os testes passam; a reestruturação documental não altera comportamento.

- [ ] **Step 5: Revisão final**

Comparar cada critério de aceite de `docs/superpowers/specs/2026-07-07-roadmap-guia-tutor-capabilities-design.md` com uma seção do roadmap. Corrigir lacunas antes de concluir.

- [ ] **Step 6: Commit final se necessário**

```powershell
git add docs/roadmap.md docs/technical/moodle-webservice-setup.md docs/technical/mcp-tools-catalog.md README.md
git commit -m "docs: validar roadmap orientado ao guia do tutor"
```
