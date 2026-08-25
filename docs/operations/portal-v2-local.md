# Portal v2 local

## Subir a versão local

Na raiz do repositório:

```powershell
docker compose -f docker-compose.local.yml up -d --build
```

URLs:

- Portal: `http://127.0.0.1:8787/`
- Health: `http://127.0.0.1:8787/health/live`
- Frontend Vite opcional: `http://127.0.0.1:4173/`

O ambiente local habilita `PortalV2Enabled`, usa Postgres no próprio Compose e ativa os adaptadores Moodle stub. Nenhuma credencial Moodle, token ou chave de produção é usada.

O Vite usa `http://127.0.0.1:8787` como proxy padrão da API. Para outro backend local, defina `PORTAL_API_PROXY` antes de iniciar o Vite.

Se a porta padrão estiver ocupada, defina `PORTAL_HTTP_PORT` antes do Compose e use a mesma porta nas URLs e no smoke:

```powershell
$env:PORTAL_HTTP_PORT = 8899
docker compose -f docker-compose.local.yml up -d --build
$env:PORTAL_SMOKE_URL = "http://127.0.0.1:8899"
npm --prefix src/MoodleConnector.Web run smoke
```

## Verificar

```powershell
docker compose -f docker-compose.local.yml ps
Invoke-WebRequest http://127.0.0.1:8787/health/live
Invoke-WebRequest http://127.0.0.1:8787/

npm --prefix src/MoodleConnector.Web run smoke:api
npm --prefix src/MoodleConnector.Web run smoke:e2e
```

Os smokes criam contas descartáveis `example.test` no banco local. `smoke:api` valida registro, sessão autenticada e estado vazio; `smoke:e2e` usa `MoodleApi__UseStubData=true` para validar conexão, cursos, curso, alunos, perfil, pendências e dashboard. Ambos recusam executar contra hosts que não sejam locais.

Para parar:

```powershell
docker compose -f docker-compose.local.yml down
```

Para remover também os dados descartáveis locais:

```powershell
docker compose -f docker-compose.local.yml down -v
```
