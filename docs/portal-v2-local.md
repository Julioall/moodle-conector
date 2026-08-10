# Portal v2 local

## Subir a versão local

Na raiz do repositório:

```powershell
docker compose -f docker-compose.local.yml up -d --build
```

URLs:

- Portal: `http://127.0.0.1:8787/portal/`
- Health: `http://127.0.0.1:8787/health/live`
- Frontend Vite opcional: `http://127.0.0.1:4173/portal/`

O ambiente local habilita `PortalV2Enabled`, usa Postgres no próprio Compose e ativa os adaptadores Moodle stub. Nenhuma credencial Moodle, token ou chave de produção é usada.

O Vite usa `http://127.0.0.1:8787` como proxy padrão da API. Para outro backend local, defina `PORTAL_API_PROXY` antes de iniciar o Vite.

## Verificar

```powershell
docker compose -f docker-compose.local.yml ps
Invoke-WebRequest http://127.0.0.1:8787/health/live
Invoke-WebRequest http://127.0.0.1:8787/portal/

npm --prefix src/MoodleConnector.Web run smoke:api
```

O smoke de API cria uma conta descartável `example.test` no banco local e valida registro, sessão autenticada e Dashboard vazio. Ele recusa executar contra hosts que não sejam locais.

Para parar:

```powershell
docker compose -f docker-compose.local.yml down
```

Para remover também os dados descartáveis locais:

```powershell
docker compose -f docker-compose.local.yml down -v
```
