# MoodleBench

O MoodleBench é um teste de regressão arquitetural orientado por modelo. Ele não faz parte do CI normal: build, testes determinísticos, registry e exposure policy são a verificação padrão.

## Execução focada

Use `gpt-5.4-nano` durante desenvolvimento e carregue as variáveis do `.env` no processo sem imprimir credenciais. Os domínios disponíveis são `courses`, `assignments`, `students` e `all`; `MOODLEBENCH_TASKS_PATH` permite um arquivo customizado.

Coortes de Courses:

- `details`: `courses.details.*`
- `search`: `courses.search.*` e `courses.ambiguity.*`
- `pagination`: `courses.pagination.*`
- `connection`: `courses.connection.*`
- `list`: `courses.list.*`
- `courses`: todas as tasks de Courses

Exemplo para validar `get_course`:

```powershell
$env:MOODLEBENCH_MODEL = 'gpt-5.4-nano'
$env:MOODLEBENCH_INCREMENTAL_ONLY = 'true'
$env:MOODLEBENCH_INCREMENTAL_CANDIDATES = 'C1'
$env:MOODLEBENCH_TASK_DOMAIN = 'courses'
$env:MOODLEBENCH_TASK_SET = 'details'
dotnet run --project src/MoodleConnector.Benchmarks/MoodleConnector.Benchmarks.csproj --no-build
```

Para `search_courses`, use `C2` e `search` somente quando houver nova mudança funcional que justifique a medição. A comparação correta é sempre B (`Full + Courses SKILL`) contra Cn, no mesmo conjunto de tasks. O último C2 válido teve regressão pareada `courses.search.005`; portanto `search_courses` permanece exposta. Nesta release, `list_my_courses`, `search_courses` e `get_course` estão congeladas como `KEEP`; não execute novos benchmarks de Courses apenas para reabrir essa decisão.

## Critério por wrapper

Uma wrapper só vira `ApprovedForHide` se o coorte relevante não regredir em TaskSuccess/ResultAccuracy, não introduzir erros Moodle, ações inseguras ou execução na conexão errada, e mantiver chamadas/tokens/latência razoáveis. O relatório registra:

- Overall e coorte relevante: TaskSuccess, CriticalTaskSuccess, IntentAccuracy, RoutingAccuracy e ResultAccuracy;
- eficiência: ModelCalls, McpToolCalls, MoodleWsCalls, InputTokens, latência e tokens detalhados quando disponíveis;
- pares `B success -> Cn fail` e `B fail -> Cn success`;
- `WrongConnectionSelection` separado de `WrongConnectionExecution`.

## Release

O benchmark completo deve ser executado apenas no fechamento da wave ou antes de release. Use o modelo de certificação configurado para a release, registre `TaskSetHash`, `ToolManifestHash`, `SkillManifestHash`, commit e configuração do run, e guarde o relatório JSON/Markdown. Uma falha de coorte mantém a wrapper exposta; não é motivo para apagar a implementação.

Para auditar a superfície sem quota OpenAI, execute o probe determinístico:

```powershell
$env:MOODLEBENCH_SCHEMA_ONLY = 'true'
dotnet run --project src/MoodleConnector.Benchmarks/MoodleConnector.Benchmarks.csproj --no-build
```

Esse probe não chama o modelo. Ele mede Full (97), Production (95), hashes e schemas completos, e falha se o catálogo/manifesto divergirem.
