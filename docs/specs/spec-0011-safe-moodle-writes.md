# SPEC-0011: Execução segura de escritas Moodle

## Status

Draft.

## Objetivo

Impedir repetição automática de escritas Moodle e tornar explícito quando uma falha de transporte deixa o resultado remoto desconhecido.

## Contexto e evidência atual

`IMoodleRestClient` é registrado com uma única política resiliente em `src/MoodleConnector.Infrastructure/DependencyInjection.cs`. Ela aplica retry a falhas transitórias, e `MoodleRestClient` usa POST para leituras e escritas. `CallWriteAsync` permite resposta vazia, mas ainda usa a mesma política.

## Decisão e arquitetura-alvo

- Leituras: timeout, circuit breaker e retry limitado.
- Escritas: timeout e circuit breaker, com retry zero por padrão.
- Retry de escrita exige classificação `IdempotentWrite`, mecanismo de idempotência ou reconciliação e teste específico.
- Falha ambígua depois da confirmação cria estado `execution_unknown`, auditável e reconciliável; nunca reenvia cegamente.

## Escopo

- Separar DI/pipelines HTTP read e write.
- Classificar cada função de escrita por idempotência e estratégia de reconciliação.
- Persistir resultado conhecido, falha conhecida ou execução desconhecida em `PendingMoodleAction`.

## Fora de escopo

- Criar novas ações acadêmicas ou automatizar decisões pedagógicas.

## Segurança, privacidade e observabilidade

- Não registrar token ou payload sensível durante reconciliação.
- Medir tentativas, execuções desconhecidas, reconciliações e latência por função.

## Plano de execução

1. Inventariar chamadores de escrita e sua idempotência.
2. Registrar políticas HTTP independentes e migrar gateways de escrita.
3. Criar estado `execution_unknown` e reconciliadores por família.
4. Criar alertas para estado desconhecido vencido.

## Critérios de aceite

- [ ] Falha transitória após aplicação simulada da escrita produz exatamente uma requisição.
- [ ] Timeout, circuit breaker e telemetria continuam ativos para escrita.
- [ ] Confirmação duplicada é bloqueada e resultado ambíguo não aparece como sucesso.
- [ ] Nenhuma escrita usa o retry de leitura.

## Validação e evidências

```powershell
dotnet test tests/MoodleConnector.Application.Tests --filter "FullyQualifiedName~MoodleRestClient|FullyQualifiedName~PendingAction"
```

## Rollout e rollback

Publicar em homologação primeiro. Rollback restaura configuração, mas nunca reexecuta uma ação `execution_unknown`.
