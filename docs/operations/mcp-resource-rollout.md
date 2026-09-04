# Rollout de MCP Resources para correção assistida

## Pré-requisitos de produção

- Aplicar as migrações `054` a `057` e confirmar o healthcheck `/health`.
- Executar build e a suíte determinística de testes.
- Executar, em modo somente leitura, a coorte de regressão FIEG e SENAI para atividade numérica e somente-feedback.
- Confirmar que logs e auditoria não contêm token, cookie, URL com query string nem conteúdo binário.

## Ordem de ativação

1. Ativar `McpResourceSubmissionDeliveryEnabled=true` no ambiente de correção.
2. Validar PDF, DOCX, XLSX, PPTX, PNG/JPG, múltiplos anexos, arquivo inválido e ZIP; `McpResourceZipEnabled` permanece `false` por padrão.
3. Depois de uma leitura bem-sucedida de resources e revisão humana, ativar `McpGradingDraftEnabled` para a mesma coorte.
4. Ativar `McpGradingWriteEnabled` somente depois de confirmar preview, hash, confirmação humana e readback no ambiente de teste.

O fluxo direto por MCP Resource é o único caminho de correção. `LegacySubmissionExtractionEnabled` deve permanecer `false`; a flag é mantida somente para compatibilidade de configuração.

## Métricas e alertas

Monitorar os instrumentos `resource_register_count`, `resource_read_count`, `resource_read_duration_ms`, `resource_download_duration_ms`, `resource_download_bytes`, `resource_cache_hit`, `resource_cache_miss` e `resource_read_failure`.

Criar alerta de severidade alta para qualquer `RESOURCE_FORBIDDEN` inesperado, `RESOURCE_HASH_MISMATCH`, write sem confirmação ou falha de readback. Criar alerta de severidade média quando a taxa de `resource_read_failure` exceder 2% em 15 minutos ou a latência p95 de leitura exceder o SLO acordado.

## Rollback

O rollback é somente de configuração:

1. Definir `McpGradingWriteEnabled=false` para interromper novos writes MCP.
2. Definir `McpGradingDraftEnabled=false` para interromper novos drafts.
3. Definir `McpResourceSubmissionDeliveryEnabled=false`; novas correções devem permanecer bloqueadas até a restauração do MCP Resource.
4. Preservar auditoria, drafts e resources até a expiração/retencão; não apagar evidências durante investigação.

## Critérios de expansão

Expandir a coorte somente sem vazamento de credenciais, sem `RESOURCE_FORBIDDEN` indevido, sem write não confirmado e sem divergência de hash. Registrar o resultado das coortes FIEG e SENAI antes da ativação padrão.
