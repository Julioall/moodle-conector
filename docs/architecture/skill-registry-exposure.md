# Skill, Registry e Exposure Policy

## Objetivo

As skills orientam intenção e roteamento; o código determinístico decide conexão, capability, policy, normalização e confirmação. Uma skill não concede permissão e o modelo não pode inventar uma operação.

## Fluxo de leitura

```text
prompt
  -> SKILL / roteamento
  -> IConnectionRegistry
  -> IOperationRegistry
  -> IPolicyEngine
  -> ICapabilityRegistry
  -> ISafeReadExecutor
  -> IMoodleRestClient
  -> IResponseNormalizer
```

`moodle_execute_read` usa `ISafeReadExecutor`. Operações desconhecidas são recusadas; operações `ControlledWrite` são bloqueadas no executor genérico e seguem o fluxo de confirmação. Capabilities são consultadas por conexão/credencial e invalidadas uma vez quando o Moodle responde access denied.

## Inventário atual

- 97 métodos MCP são descobertos nas classes de tools; o perfil `Production` expõe 95 por padrão porque os dois tools de demonstração são feature-gated e desabilitados por padrão. Os tools de lançamento de nota seguem a configuração `AssignmentGradeWriteEnabled`.
- Cada entrada do `ToolMetadataRegistry` possui `TechnicalClassification`, `ExposureStatus`, `ExposureReason` e `Evidence`. Cinco métodos de Courses têm metadata explícita; os demais recebem baseline determinístico por container em `ToolMetadataInference` e são cobertos por `ToolSurfaceInventory`.
- O catálogo de containers é declarado em `RegisteredMcpToolContainers`; não existe varredura global de assemblies. Reflection é usada somente uma vez, sobre tipos explicitamente registrados durante o startup.
- As funções de leitura conhecidas pela `MoodleReadFunctionPolicy` são registradas no `OperationRegistry`; funções não registradas não são executáveis pelo `SafeReadExecutor`.
- As funções de escrita controlada são registradas como `ControlledWrite` e não passam pelo executor genérico.
- O perfil padrão é `Production`: apenas metadata registrada é exposta, itens `Deprecated` são ocultados e metadata ausente falha fechado. `Full` e os perfis incrementais são ferramentas de diagnóstico/benchmark; o inventário schema-only habilita explicitamente flags condicionais em Full para medir as 97 entradas do catálogo, enquanto Production mede as 95 expostas.
- `list_my_courses`, `search_courses` e `get_course` permanecem expostas nesta release. A evidência histórica é preservada, mas nenhuma delas é `ApprovedForHide` ou `Deprecated`.

## Writes

`moodle_prepare_write` cria uma ação pendente e `moodle_confirm_write` exige o mesmo usuário, conexão, escopo e texto literal. Grading, mensagens, fórum e mudanças de estado continuam especializados; o caminho genérico de leitura não executa nenhum deles.

## Exposure e benchmark

`TechnicalClassification` descreve a implementação. `ExposureStatus` descreve a decisão do perfil. `BenchmarkEvidence` descreve a evidência empírica; nenhum desses campos substitui os outros.

### Classificação técnica R1–R6

- `R1`: wrapper direto de uma operação Moodle conhecida, com pouca ou nenhuma orquestração.
- `R2`: wrapper de leitura simples com transformação/seleção limitada, ainda substituível por uma operação canônica registrada.
- `R3`: leitura com validação, fallback ou composição que exige cuidado de compatibilidade.
- `R4`: agregação, paginação, joins ou relatório que depende de várias operações e preserva semântica de domínio.
- `R5`: regra pedagógica, ação controlada ou workflow que exige revisão humana, confirmação e auditoria.
- `R6`: infraestrutura, memória, discovery, arquivos ou diagnóstico que sustenta o host e não deve ser genericizada por otimização cognitiva.

`Structural`, `Specialized` e `Controlled Write` são dimensões de implementação/exposição e não substituem a classificação R1–R6. A classificação nunca é alterada para favorecer um benchmark.

O MoodleBench não faz parte do CI normal. Durante desenvolvimento, use coortes curtos e compare Cn contra B. Antes de release, execute o conjunto completo apenas para as combinações candidatas aprovadas. A telemetria separa `WrongConnectionSelection` de `WrongConnectionExecution`, registra tokens em cache/raciocínio quando disponíveis e identifica ações inseguras.

## Como alterar a superfície

1. Adicione ou altere a skill somente para intenção, roteamento, ownership, fallback e interpretação; ela não concede permissão.
2. Registre a operação em `OperationRegistry` e em `MoodleReadFunctionPolicy` apenas quando houver contrato Moodle conhecido. Leitura usa `SafeReadExecutor`; escrita usa `prepare/confirm`.
3. Registre metadata da tool ou ajuste a inferência do container. Preencha classificação técnica, motivo de exposição e evidência da implementação.
4. Atualize testes de registry, policy, exposure e contrato da tool. Verifique paginação, alias explícito, capability e normalização.
5. Para remoção de wrapper, execute a coorte específica comparando `Cn` contra `B`; só depois execute o conjunto completo de release. Não altere `Production` com base apenas na média global.

O MoodleBench mede `ToolSchemaTokens` pela serialização completa de cada descriptor retornado por `tools/list` (nome, descrição, `inputSchema`, `outputSchema`, annotations e demais campos públicos), usando a mesma superfície que é enviada ao modelo. Essa medida é determinística e não depende de quota. Um run LLM inválido não pode fornecer métricas cognitivas nem aprovação para hide.

O filtro de exposure é aplicado ao catálogo antes da serialização MCP. Não existe reescrita posterior de JSON/SSE para esconder tools.

Os contratos host `search` e `fetch` permanecem registrados, com nomes, schemas e exposição `Production` cobertos por testes contratuais.

## Waves e ownership

| Wave | Skill | Ownership principal | Estado |
|---|---|---|---|
| Courses | `moodle-courses` | cursos, busca, detalhes, conteúdos e paginação | implementada; wrappers mantidas conforme evidência |
| Assignments | `moodle-assignments` | atividades, submissões, prazos e contexto de correção | implementada; shadow live 100% em FIEG e SENAI para submissions |
| Students | `moodle-students` | participantes, grupos, identidade e atividade | implementada; shadow live de participantes 100% em FIEG e SENAI |
| Follow-up | `moodle-follow-up` | sinais observáveis para acompanhamento | implementada; parcialidade de roster/discussões declarada |
| Classroom/Reports | `moodle-classroom-audit` | estrutura, cobertura e evidências reportáveis | implementada |
| Grading | `moodle-grading` | descoberta, preparo, revisão e escrita confirmada | implementada |
| Messaging | `moodle-messaging` | preparação, revisão e confirmação de mensagens | implementada; duplicidades/IDs inválidos bloqueados antes do envio |
| Infrastructure | `moodle-core` | conexão, site info, capabilities e token | implementada |

As skills não duplicam Registry, Policy, credentials, SafeRead, normalização ou confirmação de escrita.

### Handoffs entre skills

| Origem | Destino | Fronteira |
|---|---|---|
| `moodle-assignments` | `moodle-students` | submissões usam identidade/participantes resolvidos por Students |
| `moodle-assignments` | `moodle-follow-up` | evidência de entrega alimenta sinais de acompanhamento |
| `moodle-follow-up` | `moodle-messaging` | Follow-up entrega candidatos; Messaging prepara e confirma o envio |
| `moodle-courses` | `moodle-classroom-audit` | Courses fornece estrutura/conteúdo; Audit agrega cobertura e evidência |
| `moodle-grading` | `moodle-assignments` + `moodle-students` | Grading usa atividade e estudante, mas mantém o workflow de escrita/confirmação |
