# Moodle Connector — estado técnico da release

## Decisão

**READY para Product / Skill Architecture e para a validação determinística.**

**DEFERRED — EXTERNAL QUOTA para otimização da superfície e remoção de wrappers.**

**BLOCKED BY EXTERNAL QUOTA para certificação cognitiva LLM.**

Isso não bloqueia a release conservadora: sem evidência cognitiva válida, as wrappers permanecem expostas. O run `gpt-5.5` não produziu um relatório final válido; seus traces parciais não são evidência de aprovação.

## Arquitetura final verificada

```text
USER
  -> SKILL / Router
  -> Operation Registry
  -> Connection Registry
  -> Capability Registry
  -> Policy Engine
  -> SafeReadExecutor ou serviço especializado
  -> ResponseNormalizer
  -> Moodle
```

Skills orientam intenção, ownership, fallback e interpretação. Registry, conexão, capability, policy, normalização, confirmação e auditoria permanecem determinísticos; nenhuma skill concede permissão ou escolhe credencial.

## Ownership das skills

| Skill | Responsabilidade | Handoff principal | Estado |
|---|---|---|---|
| `moodle-core` | conexão, identidade, capability e site info | base para todas as waves | implementada |
| `moodle-courses` | cursos, busca, detalhes, conteúdo e paginação | conteúdos para Classroom Audit | implementada |
| `moodle-assignments` | atividades, prazos e submissões | identidade para Students; risco para Follow-up | implementada |
| `moodle-students` | participantes, grupos e identidade | submissão para Assignments; outreach para Messaging | implementada |
| `moodle-follow-up` | sinais de inatividade, pendência e participação | candidatos para Messaging | implementada |
| `moodle-classroom-audit` | estrutura, cobertura e evidências | relatórios | implementada |
| `moodle-grading` | descoberta, preparo, revisão e lançamento confirmado | Assignment/Students | implementada |
| `moodle-messaging` | alvo, preview, confirmação e envio auditado | recebe candidatos de Follow-up | implementada |

## Gates determinísticos

- `dotnet build MoodleConnector.slnx --no-restore`: aprovado, 0 avisos e 0 erros.
- `dotnet test tests/MoodleConnector.Application.Tests/MoodleConnector.Application.Tests.csproj --no-restore`: 632 aprovados, 0 falhas, 0 ignorados.
- Shadow live de Assignments: FIEG e SENAI aprovados com 100% de paridade semântica para `mod_assign_get_submissions`.
- Shadow live de Students: FIEG e SENAI aprovados com 100% de paridade semântica para `core_enrol_get_enrolled_users`.
- A dependência transitiva vulnerável `System.Security.Cryptography.Xml` foi fixada em 10.0.10; restore, build e auditoria de vulnerabilidades não reportam mais `NU1903`.
- O run final de Courses com `gpt-5.5` foi tentado, mas a API respondeu `429 insufficient_quota / credit_balance_exhausted`; o harness agora falha rápido e gravou o resultado como `RUN INVALID / INCOMPLETE`, sem calcular gates.

## Superfície e segurança

- 97 métodos MCP são descobertos pelo registry.
- O perfil `Production` expõe 95 por padrão; os dois métodos de demonstração continuam desabilitados por feature flag.
- O inventário cobre 11 métodos estruturais, 65 especializados, 22 operações controladas e nenhum deprecated. `Structural` e operação controlada são dimensões independentes.
- Operações desconhecidas falham fechado no `SafeReadExecutor`.
- Writes continuam no fluxo `prepare/confirm`; o executor genérico de leitura não executa writes.
- Seleção de conexão, execução na conexão selecionada, capability retry e normalização estão cobertos por testes.

### Inventário de superfície e tokens

| Medida | Estado atual |
|---|---:|
| Baseline catalogada | 97 tools |
| Exposição `Production` | 95 tools |
| Wrappers ocultadas por aprovação | 0 |
| Tools condicionais desabilitadas por padrão | 2 |
| Tools demo feature-gated por padrão | 2 |
| Estruturais | 11 |
| Especializadas | 65 |
| Operações controladas | 22 |
| `Deprecated` | 0 |

O inventário de superfície é determinístico e não depende de quota. O artefato `schema-manifest` calcula o descriptor MCP completo enviado ao modelo:

| Superfície | Tools | ToolSchemaBytes | ToolSchemaTokens | ManifestHash |
|---|---:|---:|---:|---|
| Full (A) | 95 | 222585 | 55680 | `6c01f6ec32d7f66e` |
| Full + Courses SKILL (B) | 95 | 222585 | 55680 | `6c01f6ec32d7f66e` |
| Production | 95 | 222585 | 55680 | `6c01f6ec32d7f66e` |
| Courses optimized (C) | 92 | 215572 | 53925 | `257ba4c7b9f49939` |

Redução determinística de C contra B: **3 tools, 7.013 bytes, 1.755 ToolSchemaTokens — 3,15%**. Isso é uma medida de superfície MCP, não uma afirmação sobre `InputTokens` reais ou qualidade cognitiva.

Artefato: [schema-manifest.json](../../.moodlebench/cognitive/reports/20260810_024009/schema-manifest.json) e [schema-manifest.md](../../.moodlebench/cognitive/reports/20260810_024009/schema-manifest.md).

### Surface e classificação

| Dimensão | Contagem/decisão |
|---|---:|
| Tools registradas | 97 |
| Tools expostas em `Production` | 95 |
| Estruturais | 11 |
| Especializadas | 65 |
| Operações controladas | 22 |
| `Deprecated` | 0 |

`TechnicalClassification`, `ExposureStatus` e `BenchmarkEvidence` são campos separados. Os dois tools de demonstração continuam feature-gated; wrappers de Courses não foram marcadas `Deprecated` porque não há aprovação empírica válida para hide.

## Courses — decisão por wrapper

| Wrapper | Decisão | Evidência | Ação |
|---|---|---|---|
| `list_my_courses` | **KEEP** | Paginação mostrou regressão quando a wrapper foi ocultada | Não rerodar sem mudança funcional de paginação |
| `search_courses` | **KEEP / não aprovada para hide** | C2 contra B em 10 tasks: TaskSuccess 70% → 60%; houve `courses.search.005` em `B success → C2 fail` | Permanecer exposta; só revalidar se houver mudança funcional |
| `get_course` | **KEEP** | A evidência histórica de C1 não é suficiente para aprovar remoção | Permanecer exposta nesta release |

O comparador correto continua sendo `B = Full + Courses SKILL` contra cada candidato `Cn`, no mesmo conjunto de tasks. A média global não substitui o coorte relevante. Nesta release não serão executados novos benchmarks pagos de Courses.

## MoodleBench após Courses

Durante desenvolvimento:

- `gpt-5.4-nano`;
- coorte específico por mudança (`details`, `search`, `pagination`, `connection` ou `list`);
- comparação pareada B → Cn;
- sem benchmark LLM no CI normal.

Antes de fechar uma wave ou de uma release importante:

- benchmark completo apenas para a combinação candidata;
- modelo de certificação configurado para a release;
- relatório JSON/Markdown com hashes do task set, skills e manifest de tools.

Os runs diagnósticos de Assignments e Students disponíveis no diretório `.moodlebench` não devem ser tratados como certificação: foram executados antes de correções do harness de domínio/skills e servem apenas como diagnóstico histórico.

O teste controlado de quota está em `.moodlebench/cognitive/reports/20260810_020821/`; ele confirma a invalidação por tarefas ausentes, mas não contém evidência de qualidade do modelo.

O benchmark usa a versão `1.1.0`, registra `RunId`, commit, hashes de task/skill/tool manifests e separa `WrongConnectionSelection` de `WrongConnectionExecution`. Candidatos incrementais são comparados contra B; C não é comparado diretamente contra A.

## Critério de release

**READY para Product / Skill Architecture e validação determinística** quando os gates locais/CI estiverem verdes. **DEFERRED — EXTERNAL QUOTA** para otimização da superfície e **BLOCKED BY EXTERNAL QUOTA** para certificação cognitiva LLM. O CI normal executa apenas restore/build/teste determinísticos; MoodleBench fica reservado para mudança arquitetural, mudança de skill, remoção de wrapper ou release.

## Próximo passo

Congelar a decisão de Courses e manter `list_my_courses`, `search_courses` e `get_course` expostas. Não ampliar a infraestrutura do benchmark nem executar novas medições pagas sem mudança funcional que as justifique.

Assignments e Students têm paridade live de 100% em FIEG e SENAI, com evidência persistida em `.moodlebench/evidence`. Follow-up declara roster/discussões parciais; Messaging valida IDs e duplicidades antes do pending action. A validação de routing cognitivo das waves continua separada e será executada apenas como coorte curto quando a superfície final estiver congelada.

## Evidências

- [MoodleBench e coortes focados](moodlebench.md)
- [Arquitetura de Skill, Registry e Exposure Policy](../architecture/skill-registry-exposure.md)
- [Relatório incremental C2](../../.moodlebench/cognitive/reports/20260810_003435/incremental-report.json)
- [Traces parciais do run de certificação — não são aprovação](../../.moodlebench/cognitive/reports/20260810_010545/)
- [Run final invalidado por quota — não é aprovação](../../.moodlebench/cognitive/reports/20260810_020821/report.md)
