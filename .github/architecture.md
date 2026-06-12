# Arquitetura - Moodle GPT Connector

## Objetivo
Construir um servidor MCP remoto em .NET que atue como gateway semantico entre o ChatGPT e o Moodle, com autenticacao de usuario via Moodle e controles de seguranca orientados a Zero-Trust.

## Stack Tecnica
- Runtime: .NET 10
- Web host: ASP.NET Core (Kestrel)
- MCP Server: ModelContextProtocol.AspNetCore (Streamable HTTP)
- Camadas: Clean Architecture (Domain, Application, Infrastructure, Presentation)
- Padrao de aplicacao: CQRS com MediatR
- HTTP externo: HttpClientFactory + resiliencia (retry, timeout, circuit breaker)
- Observabilidade: OpenTelemetry (traces/metrics/logs)
- Persistencia opcional: PostgreSQL (estado tecnico, correlacao, auditoria)

## Topologia
- Presentation: endpoint MCP (ex.: /mcp), middleware de seguranca, validacao de entrada, serializacao JSON-RPC
- Application: casos de uso orientados por intencao (ex.: listar tarefas pendentes, consultar notas, enviar atividade)
- Domain: regras de negocio academicas, entidades e invariantes
- Infrastructure: cliente Moodle REST, autenticacao OAuth/OIDC, cache, telemetria, persistencia

## MCP Design
- Tools: expor apenas acoes atomicas de alto valor para o usuario
- Resources: fornecer dados estruturados e, quando aplicavel, UI resource text/html;profile=mcp-app
- Prompts: templates curtos para orientar workflows frequentes
- Degradacao graciosa: toda resposta com structuredContent deve incluir content textual util

## Autenticacao e Autorizacao
- Protocolo: OAuth 2.1 + OIDC
- Fluxo: Authorization Code + PKCE
- Cliente ChatGPT: registro dinamico (DCR) habilitado quando aplicavel
- Sessao: suporte a refresh token com rotacao
- Escopos: principio do menor privilegio (PoLP)
- Validacao: assinatura JWT, issuer, audience, exp, nbf, nonce e claims obrigatorias

## Modelo ACE (Abstract-Concrete-Execute)
- Abstract: o LLM planeja com base apenas na intencao do usuario e metadados confiaveis
- Concrete: a camada de aplicacao traduz para comandos/queries com parametros tipados e validados
- Execute: a infraestrutura executa chamadas Moodle com politicas de seguranca, auditoria e limites
- Regra: nenhum texto nao confiavel entra diretamente na etapa de execucao sem saneamento e validacao

## Seguranca
- Zero-Trust por padrao
- Confirmacao humana obrigatoria para operacoes destrutivas/escrita
- Rate limiting por usuario e por ferramenta
- Correlation ID por requisicao
- Redacao de PII em logs
- Segredos fora do codigo (Key Vault/secret manager)

## Widgets (quando aplicavel)
- UI em iframe sandbox
- Comunicacao exclusiva via window.openai
- Estado efemero via setWidgetState (ate ~4KB)
- Tema dinamico via window.openai.theme
- CSP explicita em _meta.ui.csp para toda origem externa
- Evitar scroll aninhado e navegacao interna complexa

## Convencoes de Ferramentas
- Nomear tools com verbos orientados ao usuario (ex.: listar_meus_cursos)
- Descricoes curtas e semanticas (foco no que resolve)
- Esquemas de entrada estritos (tipos, limites, enums)
- Erros estruturados e acionaveis para facilitar recuperacao no chat

## Pipeline de Qualidade
- Testes unitarios por camada
- Testes de contrato MCP (JSON-RPC + schemas)
- Golden prompts (diretos, indiretos e negativos)
- Validacao no MCP Inspector antes de publicar
- SAST/SCA e verificacao de dependencias no CI

## Deploy
- Container Linux
- Hospedagem sugerida: Azure Container Apps
- HTTPS obrigatorio
- Ambiente por estagio (dev/hml/prod)
- Feature flags para rollout gradual
