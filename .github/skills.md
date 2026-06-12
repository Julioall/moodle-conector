# Skills do Projeto - Moodle GPT Connector

## 1) Mastery MCP (Tools, Resources, Prompts)
- Projetar tools como acoes de negocio, nao wrappers crus da API Moodle
- Definir resources para dados estruturados e UI quando necessario
- Criar prompts de workflow reutilizaveis e curtos
- Usar descricoes LLM-first: verbos claros, termos do usuario, sem ruidao
- Priorizar contexto minimo: carregar apenas o necessario por interacao

## 2) Backend .NET + MCP
- Implementar MCP remoto com ModelContextProtocol.AspNetCore
- Organizar com Clean Architecture + CQRS (MediatR)
- Aplicar validacao de entrada e limites por tool
- Mapear erros tecnicos para erros funcionais explicaveis no chat
- Garantir degradacao graciosa (content textual + structuredContent)

## 3) Integracao Moodle (Proxy Semantico)
- Traduzir endpoints Moodle em capacidades academicas de alto nivel
- Modelar conceitos: curso, atividade, prazo, tentativa, nota, feedback
- Encapsular detalhes de API, pagina e versao em infraestrutura
- Implementar retries com idempotencia para operacoes seguras
- Tratar timezone e datas academicas de forma consistente

## 4) Frontend Widget (OpenAI Apps SDK)
- Construir UI em React para iframe sandboxado
- Integrar com window.openai: ontoolresult, callServerTool, setWidgetState, sendFollowUpMessage
- Usar design tokens nativos (@openai/apps-sdk-ui + Tailwind 4 quando aplicavel)
- Suportar tema claro/escuro dinamicamente
- Garantir acessibilidade: teclado, foco, labels e ARIA

## 5) Seguranca Zero-Trust
- PoLP em escopos OAuth, dados e rede
- OAuth 2.1 + OIDC + PKCE + DCR (quando aplicavel)
- Validacao rigorosa de JWT e claims
- CSP explicita para widgets em _meta.ui.csp
- Confirmacao humana para escrita/delecao/pagamentos
- Nao armazenar dado sensivel em widgetState

## 6) ACE (Abstract-Concrete-Execute)
- Abstract: interpretar intencao sem executar
- Concrete: construir plano tipado e validado
- Execute: executar com guardrails e auditoria
- Bloquear passagem direta de texto nao confiavel para chamadas externas

## 7) QA e Operacao
- Golden prompts para cobertura funcional e de seguranca
- MCP Inspector para validar JSON-RPC e schemas
- Testes em desktop e mobile
- Observabilidade com traces, metricas e logs redigidos
- Politica de atualizacao de dependencias e resposta a vulnerabilidades
