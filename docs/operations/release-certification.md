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
php -l tools/moodle-export-contract-source.php
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

Para a cobertura genérica, execute o gate de matriz no ambiente de homologação. Ele consulta
somente `core_webservice_get_site_info` para cada alias; com
`-RequireCompleteManifest`, falha se alguma função anunciada a qualquer token não possuir
contrato verificado compatível com a release:

```powershell
./scripts/verify-moodle-generic-coverage-matrix.ps1 `
  -Alias fieg,senai `
  -ManifestPath ./artifacts/moodle-contracts/moodle-contracts.json `
  -InventoryDirectory ./artifacts/moodle-contracts `
  -RequireCompleteManifest
```

O comando anterior substitui a execução manual por alias, mas o gate individual continua
disponível para investigar uma instalação isoladamente (`verify-moodle-generic-coverage.ps1`).
Contratos verificados de funções `administrativeOnly` satisfazem a completude contratual,
mas aparecem como `policy-blocked` e não são contados na cobertura executável; a cobertura
percentual usa somente as funções executáveis restantes. Isso mantém funções administrativas
visíveis no inventário sem alegar que um token comum pode executá-las.

O workflow de deploy aplica o mesmo controle ao artefato que já está no VPS: em modo estrito,
confirma que o arquivo remoto existe, copia-o apenas para a área temporária do runner e executa
`verify-moodle-generic-coverage.ps1 -RequireCompleteManifest` contra o Moodle configurado antes
de escrever o novo `.env` ou reiniciar os containers. Assim, um manifesto antigo, parcial ou
incompatível com a release atualmente anunciada pelo token interrompe o deploy; o manifesto
continua fora do repositório e nunca é impresso nos logs.

As credenciais devem vir do ambiente ou de um secret store. Para cada alias, use
`LIVE_FIEG_URL` e `LIVE_FIEG_TOKEN` quando já existir um token de serviço; como
alternativa, use `LIVE_FIEG_URL`, `LIVE_FIEG_USERNAME` e `LIVE_FIEG_PASSWORD`.
Substitua `FIEG` por `SENAI` para o outro alias. O script nunca imprime token,
senha ou URL completa.
Os testes `LiveShadow` também exigem o manifesto verificado por alias em
`LIVE_FIEG_CONTRACT_MANIFEST_PATH`/`LIVE_SENAI_CONTRACT_MANIFEST_PATH` (ou o
manifesto comum em `MOODLE_API_CONTRACT_MANIFEST_PATH`); sem ele, não há
contrato fictício de fallback.
O `InventoryPath` é opcional e grava somente alias, release e os pares nome/versão
das funções retornadas pelo `core_webservice_get_site_info`; ele não é um contrato
e não autoriza execução. Quando um contrato declarar `externalFunctionVersion`, o
gate também exige que essa versão seja igual à versão anunciada pelo Moodle. Quando
`ManifestPath` e `InventoryPath` são informados juntos, o gate chama também o
validador oficial do manifesto e recalcula schema, efeito e `contractHash` antes de
publicar a cobertura; assim, um manifesto com hash adulterado não pode passar apenas
por coincidir em nome e versão.

Para registrar a lacuna completa sem expor credenciais, acrescente
`-GapReportPath ./artifacts/moodle-contracts/fieg-gap.json`. O relatório sanitizado
lista cada função descoberta, sua versão e o estado `covered`, `policy_blocked`,
`missing_contract`, `contract_not_verified` ou `contract_incompatible`; ele é evidência
operacional e não substitui o manifesto verificado.

Se a exportação administrativa ainda não possuir `contractHash`, gere o
manifesto com `scripts/prepare-moodle-contract-manifest.ps1`. O comando apenas
calcula hashes e preserva o `status` informado pela revisão; ele não promove um
contrato para `verified` automaticamente. O arquivo em
`contracts/moodle-contract-source.example.json` é didático e não deve ser usado
como manifesto de produção.

Quando houver uma fonte por instalação, consolide-as antes da preparação:

```powershell
./scripts/merge-moodle-contract-sources.ps1 `
  -InputPath ./exports/fieg-source.json,./exports/senai-source.json `
  -OutputPath ./artifacts/moodle-contracts/merged-source.json
./scripts/prepare-moodle-contract-manifest.ps1 `
  -InputPath ./artifacts/moodle-contracts/merged-source.json `
  -OutputPath ./artifacts/moodle-contracts/moodle-contracts.json
```

O consolidador recusa identidades duplicadas potencialmente conflitantes; ele
não escolhe automaticamente entre schemas de duas instalações.

Quando houver acesso administrativo ao checkout da instalação Moodle, a fonte
pode ser criada com o exportador standalone
`tools/moodle-export-contract-source.php`. Ele lê a tabela de External Functions
e chama somente os métodos de descrição; não instala plugin, não executa a
operação remota e marca conversões não reconhecidas para revisão.

Se o banco da instalação não estiver disponível, o exportador também possui o
modo `--source-only`, que percorre o checkout oficial e gera um snapshot
preliminar. Esse modo não substitui a exportação da instalação: não conhece
plugins ativados, capabilities, versões registradas nem configurações locais e
nunca pode produzir contratos `verified`.

## Validação externa obrigatória

1. No ambiente HTTPS de homologação, conecte o MCP em um cliente real. Use o MCP Inspector para
   verificar discovery, OAuth, `tools/list` e as chamadas read-only permitidas para uma conta de
   teste vinculada.
2. No modo desenvolvedor do ChatGPT, associe o identificador `plugin_asdk_app_...` ao pacote por
   meio de `.app.json`, instale o plugin em ambiente limpo e valide as skills distribuídas.
3. Abra o fluxo de correção para conferir o recurso MCP `ui://grading-review/v2/app.html`, a
   renderização do widget e o retorno de uma chamada de tool.
4. Para cada escrita habilitada, execute preparação, revisão e confirmação com a conta de teste;
   registre o `CorrelationId` e confirme que uma segunda confirmação é idempotente. Inclua a
   escrita universal e, quando habilitados, os fluxos de download/upload de arquivos.
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
