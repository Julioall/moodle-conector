# Exportação de contratos Moodle

`moodle-export-contract-source.php` é um utilitário administrativo standalone.
Ele deve ser executado com PHP CLI no checkout da instalação Moodle, por um
operador autorizado. Não é um plugin Moodle e não deve ser copiado para
`local/` ou instalado no site.

O exportador lê o registro `external_functions` e chama somente
`core_external\external_api::external_function_info()` para obter descrições de
parâmetros e retorno. Ele não chama nenhuma External Function remota, não
altera serviços/permissões e não grava credenciais.

Fluxo recomendado:

```bash
php tools/moodle-export-contract-source.php \
  --moodle-root=/var/www/moodle \
  --output=/secure/export/moodle-contract-source.json \
  --source=controlled-export://alias/release
```

Depois, no repositório do connector:

```powershell
./scripts/prepare-moodle-contract-manifest.ps1 `
  -InputPath /secure/export/moodle-contract-source.json `
  -OutputPath ./artifacts/moodle-contracts/alias.json
```

Conversões desconhecidas são emitidas como `invalid` e exigem revisão. O
exportador não marca contratos como verificados por padrão.
