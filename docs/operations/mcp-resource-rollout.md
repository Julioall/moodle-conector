# Rollout de MCP Resources para correção assistida

## Pré-requisitos de produção

- Aplicar as migrações `054` a `057` e confirmar o healthcheck `/health`.
- Executar build e a suíte determinística de testes.
- Executar, em modo somente leitura, a coorte de regressão FIEG e SENAI para atividade numérica e somente-feedback.
- Confirmar que logs e auditoria não contêm token, cookie, URL com query string nem conteúdo binário.

## Ordem de ativação

1. Manter todas as flags MCP desligadas em produção.
2. Em curso, atividade e usuário de teste, ativar `McpResourceSubmissionDeliveryEnabled`.
3. Validar PDF, DOCX, XLSX, PPTX, PNG/JPG, múltiplos anexos, arquivo inválido e ZIP; `McpResourceZipEnabled` só é ativada para a coorte ZIP.
4. Depois de uma leitura bem-sucedida de resources e revisão humana, ativar `McpGradingDraftEnabled` para a mesma coorte.
5. Ativar `McpGradingWriteEnabled` somente depois de confirmar preview, hash, confirmação humana e readback no ambiente de teste.

Nunca habilitar as três flags para toda a base no mesmo deploy. `LegacySubmissionExtractionEnabled` deve permanecer `true` durante toda a estabilização.

## Métricas e alertas

Monitorar os instrumentos `resource_register_count`, `resource_read_count`, `resource_read_duration_ms`, `resource_download_duration_ms`, `resource_download_bytes`, `resource_cache_hit`, `resource_cache_miss` e `resource_read_failure`.

Criar alerta de severidade alta para qualquer `RESOURCE_FORBIDDEN` inesperado, `RESOURCE_HASH_MISMATCH`, write sem confirmação ou falha de readback. Criar alerta de severidade média quando a taxa de `resource_read_failure` exceder 2% em 15 minutos, a latência p95 de leitura exceder o SLO acordado, ou o fallback legado aumentar de forma sustentada.

## Rollback

O rollback é somente de configuração:

1. Definir `McpGradingWriteEnabled=false` para interromper novos writes MCP.
2. Definir `McpGradingDraftEnabled=false` para retornar à criação de drafts legados.
3. Definir `McpResourceSubmissionDeliveryEnabled=false`; o lote passa a usar o pipeline legado disponível.
4. Preservar auditoria, drafts e resources até a expiração/retencão; não apagar evidências durante investigação.

## Critérios de expansão

Expandir a coorte somente sem vazamento de credenciais, sem `RESOURCE_FORBIDDEN` indevido, sem write não confirmado, sem divergência de hash e com fallback observável. Registrar o resultado das coortes FIEG e SENAI antes da ativação padrão.
