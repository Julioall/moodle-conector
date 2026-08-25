# Certificação de release

## Objetivo e autoridade

Este procedimento operacional executa a SPEC-0010. Ele transforma os controles automatizados
em uma decisão de publicação rastreável; não substitui a aprovação humana do proprietário do
plugin nem o processo de revisão da OpenAI.

## Pré-requisitos

- Uma versão e um commit de release identificados.
- `APP_DOMAIN` público, HTTPS funcional e OAuth configurado no ambiente de homologação.
- Uma conexão MCP criada no modo desenvolvedor do ChatGPT e seu identificador
  `plugin_asdk_app_...` disponível para o responsável pelo produto.

## Evidência automatizada

Execute, no commit que será publicado:

```powershell
./scripts/validate-plugin.ps1
./scripts/generate-chatgpt-app-submission.ps1 -Check
python plugins/moodle-connector/skills/moodle-core/scripts/check_skill_catalog.py --repo-root .
./scripts/check-documentation.ps1
dotnet build MoodleConnector.slnx --no-restore --configuration Release
dotnet test MoodleConnector.slnx --no-build --no-restore --configuration Release
docker run --rm -v "${PWD}:/repo:ro" -w /repo rhysd/actionlint:1.7.7
$env:POSTGRES_PASSWORD = 'validation-password'
docker compose config --quiet
docker run --rm -e APP_DOMAIN=example.test -v "${PWD}/Caddyfile:/etc/caddy/Caddyfile:ro" caddy:2.10-alpine caddy validate --config /etc/caddy/Caddyfile --adapter caddyfile
docker build --tag moodle-connector-release-validation .
./scripts/verify-production-container.ps1 -Image moodle-connector-release-validation
./scripts/verify-production-endpoint.ps1 -AppDomain <APP_DOMAIN> -RequireStrictTransportSecurity
```

No portal, execute também `npm run lint`, `npm run typecheck`, `npm test` e `npm run build` a
partir de `src/MoodleConnector.Web`. O workflow de CI mantém essas verificações e valida o
container de produção e o Caddy; anexe o URL da execução bem-sucedida à release.
No deploy, o workflow também confirma que o domínio público responde com a versão esperada,
OAuth discovery, JWKS, inicialização MCP e headers de segurança.
O verificador público deve falhar a promoção se o proxy ainda expuser `Server: Kestrel` ou se
`Strict-Transport-Security` estiver ausente.

## Validação externa obrigatória

1. No ambiente HTTPS de homologação, conecte o MCP em um cliente real. Use o MCP Inspector para
   verificar discovery, OAuth, `tools/list` e as chamadas read-only permitidas para uma conta de
   teste vinculada.
2. No modo desenvolvedor do ChatGPT, associe o identificador `plugin_asdk_app_...` ao pacote por
   meio de `.app.json`, instale o plugin em ambiente limpo e valide as skills distribuídas.
3. Abra o fluxo de correção para conferir o recurso MCP `ui://grading-review/v2/app.html`, a
   renderização do widget e o retorno de uma chamada de tool.
4. Para cada escrita habilitada, execute preparação, revisão e confirmação com a conta de teste;
   registre o `CorrelationId` e confirme que uma segunda confirmação é idempotente.
5. Preencha [o checklist de segurança](../security/release-checklist.md) e registre as exceções
   aprovadas, sem incluir segredos, tokens ou dados de estudantes.

## Artefatos a registrar

- versão do plugin, SHA do commit e imagem/versão implantada;
- URL e resultado dos workflows de CI e deploy;
- identificador da conexão MCP (nunca tokens ou chaves);
- evidência de OAuth, Inspector, instalação limpa e UI MCP;
- flags de escrita habilitadas e contas de teste usadas;
- commit estável para rollback.

## Rollback

Siga o [runbook de deploy](deploy-runbook.md#rollback). Se metadata MCP ou schemas de tool
mudarem, recarregue a configuração do app no ChatGPT após retornar o servidor e o pacote à versão
anterior. Preserve banco e certificados OAuth, salvo decisão explícita e documentada de rollback
de dados.
