# SPEC-0008: Topologia canônica do portal

## Status

Implementing.

## Objetivo

Escolher uma URL, domínio e proprietário canônicos para o portal, eliminando a ambiguidade entre
SPA integrada, rota `/portal/` e proxy para frontend Claris.

## Escopo

- Decidir topologia de produção e desenvolvimento.
- Alinhar Dockerfile, Caddy, Vite, SPA, documentação e smoke tests.
- Criar redirects e janela de depreciação quando houver URL antiga.

## Fora de escopo

- Extrair o portal para outro deploy ou reintroduzir proxy para frontend externo.

## Dependências

- `APP_DOMAIN` público para validar HTTPS, OAuth e o comportamento do Caddy em homologação.

## Critérios de aceite

- [x] Há uma URL canônica por ambiente: `https://APP_DOMAIN/` em produção e `http://127.0.0.1:8787/` localmente.
- [x] Caddy e Docker servem a SPA integrada no domínio `APP_DOMAIN`, na raiz `/`.
- [x] O portal usa somente `/api` e mantém cookie/CSRF.
- [x] `/app.html` e `/auth.html` redirecionam para a SPA em `/`; `/portal/` é tratado como deep link da SPA integrada até ser removido em uma versão futura.

## Validação e evidências

- Rodar `npm run smoke`, `npm run smoke:api` e smoke E2E do ambiente escolhido.
- Verificar login, refresh, deep link e logout por URL canônica.

## Rollout e rollback

Usar redirects observáveis antes de remover a rota antiga. Restaurar o proxy ou build anterior
mantém reversão rápida.
