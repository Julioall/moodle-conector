# ADR-0001: API Moodle orientada por capacidades

## Decisão

O conector descobre as funções Web Service em `core_webservice_get_site_info` para cada conexão Moodle e usa esse perfil, não a versão do Moodle, para selecionar comportamentos.

O transporte comum usa `POST /webservice/rest/server.php` com parâmetros `application/x-www-form-urlencoded`. Tokens, usuário e senha não são colocados em URLs. Funções descobertas sem classificação local permanecem `Unknown` e não podem ser executadas pelo executor universal.

As funções explicitamente classificadas como leitura podem ser chamadas por `moodle_execute_read`. Escritas controladas exigem `moodle_prepare_write` seguido de `moodle_confirm_write`, feature flag, `CanWrite`, escopo `moodle.write`, mesma conexão e confirmação literal. Funções destrutivas são bloqueadas.

## Consequências

- Cada alias pode ter funções e estratégias diferentes.
- Tools acadêmicas continuam sendo a superfície preferencial para fluxos recorrentes.
- O catálogo de risco é uma allowlist deliberada: adicionar uma função exige revisão administrativa.
- Clientes MCP sem atualização dinâmica consultam `moodle_list_available_flows` para receber indisponibilidades explicadas.
