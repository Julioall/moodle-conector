# SPEC-0017: Garantias PostgreSQL, concorrência e gates de release

## Status

Implementing. Depende de SPEC-0011 e SPEC-0015.

## Objetivo

Validar no banco de produção os contratos de unicidade, JSONB, lease, confirmação e concorrência; fazer do CI a barreira antes do deploy.

## Contexto e evidência atual

Produção usa Npgsql, índices únicos, JSONB, `ExecuteUpdateAsync`/`ExecuteDeleteAsync` e leases. A suíte usa principalmente EF InMemory. Inserções em fila e snapshots possuem leitura seguida de inserção, suscetível a conflito entre instâncias. A aplicação do schema agora usa advisory lock de sessão no PostgreSQL, evitando que duas réplicas inicializem os mesmos objetos simultaneamente.

## Decisão e arquitetura-alvo

- Executar a integração PostgreSQL em um job com serviço PostgreSQL efêmero no CI; o teste também aceita uma connection string local para diagnóstico sem depender do daemon Docker do desenvolvedor.
- Usar upsert ou recuperação explícita de unique violation onde instâncias concorrentes possam inserir a mesma chave.
- CI de PR é obrigatório antes do merge; deploy mantém certificação de release e não substitui a proteção de branch.

## Escopo

- Testes de JSONB, migrations, índices, `SnapshotRun`, duas instâncias disputando lease, enqueue/save concorrente e double confirmation.
- Teste de timeout pós-escrita da SPEC-0011.
- Documentar checks obrigatórios e política de branch para administradores.

## Critérios de aceite

- [x] Em duas instâncias, apenas uma conquista um lease ou confirma ação.
- [x] Inserções simultâneas convergem sem duplicação ou erro não tratado, incluindo recuperação por savepoint em transação.
- [x] Testes PostgreSQL executam localmente quando `MOODLE_CONNECTOR_POSTGRES_TEST_CONNECTION` é definido e no CI com serviço efêmero.
- [ ] Merge em `main` requer CI aprovado, conforme configuração administrativa documentada.

O último item é deliberadamente externo ao repositório: falta aplicar/verificar a regra de
branch protection no GitHub. Também permanecem gates manuais de MCP Inspector, Moodle real e
certificação de deploy/homologação.

## Validação e evidências

```powershell
dotnet test tests/MoodleConnector.Application.Tests --filter "FullyQualifiedName~MoodleSnapshotPostgresIntegrationTests|FullyQualifiedName~GradingBatchJobPostgresIntegrationTests"
dotnet test MoodleConnector.slnx
```

## Rollout e rollback

Executar inicialmente como check informativo e promovê-lo a obrigatório após estabilidade. Falha de infraestrutura de container é distinguida de falha funcional antes de relaxar o gate.
