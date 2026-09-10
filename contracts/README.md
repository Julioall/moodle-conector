# Manifestos de contratos Moodle

Este diretório é montado como somente leitura no container. O manifesto é um
artefato administrativo por instalação e release; ele não é gerado pelo
Moodle Connector e nunca contém credenciais ou PHP executável.

Cada contrato deve descrever uma External Function anunciada ao token:

- `inputSchema` e `outputSchema` em JSON Schema;
- `effect` (`read`, `write` ou `unknown`);
- release Moodle e, quando disponível, `externalFunctionVersion` do `site_info`;
- componente/plugin, permissões, paginação e tratamento de arquivos;
- `administrativeOnly=true` para funções restritas à administração, sempre com
  uma `platformPermission` explícita;
- `contractHash` calculado pelo registro do conector sobre a forma JSON
  compacta dos schemas (espaços e quebras de linha não alteram o hash);
- `status=verified` somente depois de revisão da fonte e homologação.

Um contrato só pode ser `verified` quando o efeito for explicitamente `read` ou
`write`, os schemas forem objetos válidos e o hash canônico corresponder ao
conteúdo. Funções cujo efeito não puder ser determinado permanecem `unknown`
ou `invalid` e são bloqueadas pelo modo estrito.

Use [moodle-contract-manifest.schema.json](./moodle-contract-manifest.schema.json)
como formato estrutural. Antes de ativar o modo estrito, execute:

```powershell
./scripts/validate-moodle-contract-manifest.ps1 `
  -ManifestPath ./artifacts/moodle-contracts/fieg.json `
  -InventoryPath ./artifacts/moodle-contracts/fieg-inventory.json `
  -RequireComplete
```

O arquivo [moodle-contract-inventory.example.json](./moodle-contract-inventory.example.json)
é apenas um fixture do CI para testar o encadeamento de preparação e validação;
ele não representa uma instalação Moodle real.

Para um deployment que atende mais de uma instalação, consolide as fontes com
`scripts/merge-moodle-contract-sources.ps1` e valide o manifesto resultante
contra todos os inventários usando
`scripts/validate-moodle-contract-manifest-matrix.ps1`. O consolidador preserva
o release de cada contrato e recusa identidades duplicadas potencialmente
conflitantes.

Para gerar os inventários diretamente dos ambientes de homologação e executar
a verificação por todos os aliases em uma única etapa, use
`scripts/verify-moodle-generic-coverage-matrix.ps1`. Ele lê
`LIVE_<ALIAS>_URL` e `LIVE_<ALIAS>_TOKEN` (ou usuário/senha), nunca grava
credenciais e só persiste os pares nome/versão retornados pelo Moodle.
Na comparação de release, o gate aceita o sufixo de build do `site_info`
(por exemplo, `5.1.2 (Build: ...)`) quando o patch release do contrato é o
mesmo; a versão da External Function continua sendo comparada exatamente.

O manifesto não concede acesso: disponibilidade continua sendo verificada por
conexão e token no Moodle. Funções administrativas podem permanecer no
inventário com `policy_blocked` e não entram na cobertura executável. O gate de
completude ainda exige um contrato verificado para elas; isso é diferente de
afirmar que estão liberadas para execução pelo token usado no inventário.

Quando uma exportação controlada ainda não possuir `contractHash`, prepare o
manifesto com:

```powershell
./scripts/prepare-moodle-contract-manifest.ps1 `
  -InputPath ./contracts/moodle-contract-source.json `
  -OutputPath ./artifacts/moodle-contracts/fieg.json `
  -InventoryPath ./artifacts/moodle-contracts/fieg-inventory.json `
  -RequireComplete
```

O preparador não promove contratos para `verified`, não consulta credenciais e
não executa PHP. Ele normaliza a exportação administrativa, calcula o mesmo
SHA-256 usado pelo runtime e encaminha a validação contra o inventário. O
arquivo [moodle-contract-source.example.json](./moodle-contract-source.example.json)
é somente um exemplo estrutural e não certifica funções de nenhuma instalação.
O arquivo [moodle-contract-source.fixture-complete.json](./moodle-contract-source.fixture-complete.json)
é uma fixture controlada para testar o caminho positivo de `-RequireComplete`;
também não é um manifesto de produção nem autoriza funções Moodle reais.

Para obter a fonte diretamente de uma instalação Moodle, um administrador pode
executar o exportador standalone a partir do checkout do Moodle:

```bash
php /caminho/para/moodle-conector/tools/moodle-export-contract-source.php \
  --moodle-root=/var/www/moodle \
  --output=/secure/export/moodle-contract-source.json \
  --source=controlled-export://fieg/2026-09-10
```

Ele consulta `external_functions` e as descrições oficiais de parâmetros e
retorno, sem chamar a função externa. Classes de descrição não mapeadas ficam
como `invalid` com aviso para revisão; o exportador não instala
plugin próprio no Moodle, não altera serviços e não concede permissões.

Quando ainda não houver acesso ao banco da instalação, o mesmo exportador
aceita um checkout oficial do Moodle em modo somente-fonte:

```bash
php /caminho/para/moodle-conector/tools/moodle-export-contract-source.php \
  --moodle-root=/caminho/para/moodle-5.1.2 \
  --source-only \
  --output=/secure/export/moodle-contract-source-5.1.2.json \
  --source=official-moodle-source://5.1.2
```

Esse snapshot percorre `db/services.php` e chama apenas os métodos de
descrição das classes. Ele é útil para preparar a revisão dos schemas, mas não
prova quais plugins estão instalados/ativados, as capabilities da instalação,
as versões registradas das funções ou o comportamento do ambiente. Por isso
sempre produz contratos `missing` (ou `invalid` quando a descrição depende de
configuração/banco), nunca `verified`; a certificação deve usar depois a
exportação da instalação real e o inventário de cada token.

Em `Production`, `MoodleApi:RequireVerifiedContracts=true` é deliberadamente
mais restritivo que a validação estrutural: o runtime rejeita no startup
qualquer manifesto que contenha contrato `missing`, `stale`, `conflicting` ou
`invalid`. Assim, um arquivo parcial não pode ser confundido com uma cobertura
universal pronta para uso.
