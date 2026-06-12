# Regras do Agente de Codigo - Moodle GPT Connector

## Objetivo
Garantir implementacoes seguras, testaveis e alinhadas a arquitetura AI-Native do projeto.

## Regras Obrigatorias
1. Validar JSON-RPC e schemas MCP no MCP Inspector antes de recomendar conclusao tecnica.
2. Exigir confirmacao humana explicita para qualquer tool com efeito de escrita/alteracao/delecao.
3. Retornar sempre duas camadas de resposta:
   - structuredContent para consumo da UI/modelo
   - content textual para degradacao graciosa
4. Seguir Clean Architecture e manter separacao entre Domain/Application/Infrastructure/Presentation.
5. Aplicar ACE (Abstract-Concrete-Execute) em fluxos que executam acoes externas.
6. Nao expor segredos em codigo, logs ou mensagens de erro.
7. Validar entradas com esquemas estritos e rejeitar parametros ambiguos.
8. Preservar rastreabilidade com Correlation ID por requisicao.
9. Redigir PII em logs e eventos de monitoramento.
10. Nao replicar dashboards no chat: priorizar acoes atomicas orientadas a intencao.

## Convencoes para Tools
- Nome com verbo de acao e escopo claro.
- Descricao curta, orientada ao usuario, sem jargao interno.
- Esquema de entrada com tipos, limites, formatos e enums.
- Erros estruturados com mensagem acionavel.

## Convencoes para Widgets
- Sem navegacao interna complexa.
- Evitar scroll aninhado em cards inline.
- Limite de duas acoes em inline card.
- Persistir apenas estado efemero de UI via widgetState.
- Implementar acessibilidade por teclado e ARIA.

## Checklist antes de merge
- Testes unitarios e de contrato MCP passando.
- Validacao de seguranca (token, escopo, confirmacao humana).
- Revisao de CSP para recursos externos do widget.
- Verificacao de degradacao graciosa.
- Evidencia de teste em desktop e mobile.
