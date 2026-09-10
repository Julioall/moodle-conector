# SPEC-0027: Cobertura genérica dos Web Services do Moodle

## Status

Implementing. Definida em 2026-09-10. A implementação cobre descoberta,
contratos, execução REST genérica, autorização, binários, resultados persistidos
e continuação segura. Ainda não certifica cobertura universal da instalação
Moodle alvo: isso exige manifesto real, conexão homologada e testes PostgreSQL.

## Objetivo

Suportar genericamente todas as funções External Web Service disponibilizadas
pela conexão Moodle e para as quais exista contrato verificado e transporte
suportado, sempre sujeito à autorização efetiva para a operação solicitada.
A superfície MCP deve permitir descoberta, descrição, leitura, escrita confirmada,
transferência de arquivos e encadeamento de operações do core e dos plugins.

Os limites de descoberta e execução são distintos:

| Conjunto | Relação com o objetivo |
|---|---|
| APIs PHP internas do Moodle | Fora do universo de execução externa desta SPEC |
| External Functions instaladas | Podem existir sem integrar o serviço Web Service utilizado |
| External Functions adicionadas ao serviço | Podem continuar inacessíveis ao token selecionado |
| External Functions acessíveis pelo token | Universo candidato à execução; ainda exige contrato, transporte e autorização contextual |

Conhecer uma função instalada não prova que ela esteja disponível à conexão.
Ser anunciada ao token não prova permissão para qualquer curso, usuário ou recurso.

“Todos” significa inventariar todas as funções anunciadas ao token e atribuir
uma situação explícita a cada uma. A meta de execução é cobrir todas as funções
com contrato verificado, transporte suportado e autorização efetiva. Funções
desconhecidas ou restritas continuam no inventário com o motivo do bloqueio.
Nenhuma porcentagem de cobertura pode excluir silenciosamente essas funções.

O alvo inicial é a API REST de funções externas e os endpoints de arquivos.
Isso não significa implementar todas as APIs PHP internas, páginas administrativas
ou todos os protocolos alternativos do Moodle.

## Contexto e evidência atual (baseline antes desta implementação)

Evidências relativas à raiz do repositório, inspecionadas em 2026-09-10:

| Componente | Implementação atual | Lacuna a resolver |
|---|---|---|
| Descoberta | `MoodleFunctionCatalog` consulta `core_webservice_get_site_info`; `MoodleFunctionProfileParser` extrai nomes | `MoodleFunctionDescriptor` contém apenas nome, risco e disponibilidade; faltam contratos |
| Exposição | `MoodleUniversalTools` oferece listar/verificar funções | Diagnósticos ocultos em Production; cinco fluxos de negócio não substituem descoberta geral |
| Leitura | `MoodleUniversalTools.ExecuteReadAsync` usa `SafeReadExecutor` e `OperationRegistry` | Classificação e perfil de normalização inferidos do nome |
| Escrita | `MoodleUniversalWriteService` prepara e confirma ações | `MoodleWriteResult` devolve status e tamanho, sem payload/IDs retornados pelo Moodle |
| Arquivos | `MoodleDownloadFileTools` e `MoodleSubmissionFileGateway` | Tool limita MIME a PDF, DOCX, DOC e TXT; não recebe alias; upload Moodle dedicado não localizado |
| Segurança | Flags, scopes, pending actions, auditoria e reconciliação existentes | Consolidar autorização por função e conexão nos caminhos genéricos |

Arquivos principais: `src/MoodleConnector.Presentation/Tools/MoodleUniversalTools.cs`,
`MoodleUniversalWriteTools.cs`, `MoodleDownloadFileTools.cs`;
`src/MoodleConnector.Application/MoodleApi/MoodleFunctionClassifier.cs`,
`MoodleFunctionModels.cs`; `src/MoodleConnector.Application/Registry/OperationRegistry.cs`;
`src/MoodleConnector.Infrastructure/MoodleApi/MoodleUniversalWriteService.cs`.

Na baseline, o classificador incluía `view` nos verbos de leitura. Isso não
prova ausência de efeitos: `mod_book_view_book` é declarada como escrita no
código Moodle. Nesta implementação, `view` deixou de ser leitura implícita e
passou a exigir contrato verificado ou bloqueio pelo caminho de escrita.
Fonte: [serviços do módulo book](https://github.com/moodle/moodle/blob/main/public/mod/book/db/services.php).

## Execução inicial da SPEC

Implementado nesta iteração, sem instalar plugin no Moodle:

- `MoodleFunctionContractRegistry` independente do catálogo de capabilities,
  com manifesto administrativo opcional, compatibilidade de release, hash,
  conflito e validação estrutural de schemas JSON.
- O fluxo administrativo de contratos agora inclui o preparador de manifesto
  com hash canônico e o exportador standalone `tools/moodle-export-contract-source.php`,
  que lê as descrições oficiais da instalação sem instalar plugin no Moodle;
  conversões desconhecidas permanecem `invalid` para revisão.
- Para deployments com mais de uma instalação/release, `merge-moodle-contract-sources.ps1`
  consolida fontes sem apagar a versão de cada contrato, e
  `validate-moodle-contract-manifest-matrix.ps1` valida o manifesto contra todos
  os inventários antes do rollout.
- `moodle_list_functions` passou a ser uma descoberta utilizável em Production
  e `moodle_describe_function` foi adicionado para expor situação do contrato,
  efeito, origem, versão, hash e schemas sem transformar contrato em permissão.
- `OperationRegistry`, leitura segura e escrita confirmada usam o efeito do
  contrato verificado antes da heurística do nome; `view` não é mais leitura
  implícita. O modo estrito (`MoodleApi:RequireVerifiedContracts`) bloqueia
  `schema_unavailable`, conflito e incompatibilidade antes da chamada remota.
  O startup também rejeita manifesto estrito vazio, qualquer entrada não
  `verified` e contratos verificados com hash, schema ou efeito inválidos,
  evitando descobrir o problema somente na primeira operação Moodle.
- Resultados de escrita e upload preservam envelope sanitizado, IDs retornados,
  hash do contrato e avisos em `moodle_pending_actions.ResultJson`; podem ser
  consultados por `moodle_get_operation_result` sem repetir a chamada.
- Leituras retornam `operationId`, alias, hash e `completeness`. Contratos de
  paginação offset/page/cursor geram tokens opacos, criptografados e vinculados
  a usuário, alias, função e hash; tokens adulterados, expirados ou de outro
  contexto são recusados.
- O executor REST genérico usa somente o token do usuário selecionado; service
  token ficou restrito a fluxos internos de descoberta.
- Download aceita política configurável de MIME e alias explícito. Upload
  confirmado para rascunho usa resource binário autorizado, `POST
  /webservice/upload.php`, `itemid`, hash/tamanho, auditoria e estado
  `execution_unknown`; sua flag é independente e permanece desligada por
  padrão.
- O endpoint de upload e os serviços novos têm testes de transporte, pending
  action, itemid, isolamento contrato/capability, hash e exposição MCP.
- Em `Production`, `MoodleProductionConfigurationGuard` impede o startup quando
  escrita ou upload universal está habilitado sem `RequireVerifiedContracts=true`,
  além do gate equivalente no workflow de deploy.

O modo transicional permanece disponível somente em desenvolvimento e
homologação, porque ainda não há um manifesto de contratos gerado a partir da
instalação Moodle alvo. Em `Production`, o guard e o Compose exigem
`RequireVerifiedContracts=true` e um manifesto não vazio; sem ele o processo
falha no startup, em vez de executar funções sem contrato. A escrita universal
está habilitada no perfil versionado, mas continua condicionada a contratos,
capability, `CanWrite`, escopo e confirmação humana; nenhum deploy foi
executado.

### Evidência live adicional (2026-09-10)

Uma consulta somente leitura a `core_webservice_get_site_info`, usando as contas
de homologação disponíveis localmente, confirmou o inventário atual:

| Alias | Release Moodle | Funções anunciadas | `core_webservice_get_site_info` |
|---|---|---:|---|
| `fieg` | 5.1.2 (Build: 20260209) | 448 | presente |
| `senai` | 4.5.5 (Build: 20250609) | 443 | presente |

O gate `scripts/verify-moodle-generic-coverage.ps1` passou em modo inventário
para os dois aliases. O `MoodleGenericContractLiveShadowTests` agora rejeita
contratos sintéticos: quando há credenciais live, ele exige o manifesto
verificado da instalação e executa somente uma função descrita nesse manifesto.
Como os manifestos reais ainda não foram produzidos, a homologação LiveShadow
completa continua pendente. As 891 funções anunciadas ainda precisam de
contratos verificados, classificação de efeito e homologação por matriz de
permissões antes do rollout universal.

A mesma leitura de `core_webservice_get_site_info` retornou `downloadfiles=1` e
`uploadfiles=1` para `fieg` e `senai`. Isso confirma que o transporte de arquivos
está habilitado nas duas conexões e justifica as ferramentas universais de
download/upload; não significa que toda External Function aceite arquivos. A
aceitação, destino, MIME e itemid continuam dependentes do `fileHandling` de cada
contrato verificado e da homologação correspondente.

O parser agora preserva também o campo `version` devolvido por cada função no
`site_info` como `ExternalFunctionVersion`. Esse valor é evidência da versão da
External Function instalada e aparece na descrição e no relatório de cobertura;
ele não é confundido com a release global do Moodle nem concede autorização.

O exportador também foi validado contra checkouts oficiais sem banco: no
Moodle 5.1.2 ele descobriu 760 declarações, converteu 712 descrições e marcou
48 como dependentes de configuração/banco; no Moodle 4.5.5 descobriu 759,
converteu 710 e marcou 49. Esses snapshots são deliberadamente `missing` ou
`invalid` e não substituem os contratos dos plugins instalados. No inventário
FIEG, 18 funções não existem no core público 5.1.2 e 25 funções coincidem com
descrições que exigem a instalação configurada; no SENAI, são 6 e 26,
respectivamente.

## Decisão e arquitetura-alvo

### 1. Catálogo de disponibilidade separado do catálogo de contratos

Introduzir explicitamente `MoodleFunctionContractRegistry`, acessível por uma
interface `IMoodleFunctionContractRegistry`, como fonte dos contratos versionados.
Sua responsabilidade é resolver o contrato compatível com a versão do Moodle e
do componente, verificar procedência/hash e informar ausência ou conflito.
Ele não recebe credenciais e não decide se o token pode executar uma função.

O Availability Registry mantém a responsabilidade de consultar a disponibilidade
por conexão/token, aproveitando `MoodleFunctionCatalog` e `CapabilityRegistry`.
A implementação deve consolidar a fonte de disponibilidade desses componentes
para evitar snapshots divergentes, mantendo-a independente do registro de contratos.
O executor combina os dois registros e aplica a autorização da operação.

Contrato conceitual mínimo, sem escolher biblioteca de JSON Schema nesta SPEC:

```csharp
public sealed record MoodleFunctionContract
{
    public required string FunctionName { get; init; }
    public required MoodleEffect Effect { get; init; }
    public required JsonElement InputSchema { get; init; }
    public required JsonElement OutputSchema { get; init; }
    public string? MoodleVersion { get; init; }
    public string? ExternalFunctionVersion { get; init; }
    public string? Component { get; init; }
    public string? PluginVersion { get; init; }
    public required ContractStatus Status { get; init; }
    public required string ContractHash { get; init; }
}
```

`JsonElement` representa documentos JSON Schema validados, não parâmetros
arbitrários. `MoodleEffect` e `ContractStatus` são tipos propostos. Ausência de
contrato é um resultado de resolução `missing`, sem fabricar schemas vazios.
O modelo completo inclui procedência, regras de compatibilidade e metadados
operacionais descritos abaixo; versão desconhecida exige evidência explícita
de compatibilidade antes de marcar o contrato como verificado.

Por exemplo, o registro pode conhecer `core_course_update_courses`, enquanto
o token da conexão A a oferece e o da conexão B não. Importar seu contrato não
altera o resultado de disponibilidade de B nem permite encaminhar a chamada.

- Disponibilidade vem da conexão e do token atual. Contrato descreve parâmetros,
  retorno, semântica e versão; não concede permissão.
- Cache de disponibilidade isolado por conexão e identidade/fingerprint de
  credencial. Rotação, alteração de serviço e expiração invalidam o snapshot.
- Contratos têm `functionName`, `component`, `moodleRelease`, `pluginVersion`
  e `externalFunctionVersion` quando conhecidos, `inputSchema`, `outputSchema`, `effect`, `requiredScopes`,
  `platformPermission`, `administrativeOnly`, `pagination`, `fileHandling`, `source`, `contractHash`
  e estado `verified`, `missing`, `stale` ou `conflicting`.
- Preservar tipos, defaults, campos opcionais, null explícito, arrays e estruturas
  aninhadas. Omissão e null não são intercambiáveis. Não inventar enumerações.
- Gerar contratos a partir de uma exportação controlada das definições externas
  da instalação alvo ou de código/documentação oficial de versão compatível.
  Plugins próprios exigem contrato correspondente à versão instalada.
- Implementar importação administrativa de manifestos versionados como primeira
  fonte, sem instalar plugin próprio no Moodle. Não pressupor que exista endpoint
  padrão de schemas; utilizar manifestos gerados de fontes verificadas compatíveis
  com a instalação e documentação disponibilizada pelo administrador.
- Importações validam formato e origem e nunca executam PHP fornecido pelo manifesto.
  Metadados e descrições são dados, não instruções para o agente.
- Sem contrato confiável: permitir descoberta, mas bloquear execução com
  `schema_unavailable` ou `schema_version_mismatch` e orientação de configuração.

### 2. Superfície MCP proposta

A tabela abaixo registra a superfície-alvo e o estado implementado. O relatório
de cobertura fornece a listagem completa paginada; `moodle_list_functions`
continua preservado como retorno de compatibilidade.

| Tool | Contrato proposto |
|---|---|
| `moodle_list_functions` (existente) | Expor em Production; filtro, alias e situação por função |
| `moodle_describe_function` (nova) | Alias + nome; contrato, fonte, versão, efeitos, limites e motivo de indisponibilidade |
| `moodle_execute_read` (existente) | Validar contrato e autorização; executar somente leitura verificada |
| `moodle_prepare_write` / `moodle_confirm_write` (existentes) | Validar, revisar e executar mutação; preservar resultado sanitizado |
| `moodle_download_file` (existente) | Acrescentar alias/referência de arquivo e política configurável de transferência |
| `moodle_prepare_upload` / `moodle_confirm_upload` (novas) | Revisar e enviar recurso binário para área de rascunho Moodle |
| `moodle_get_operation_result` (nova) | Consultar resultado autorizado persistido sem repetir escrita |
| `moodle_reconcile_write` (existente) | Resolver execução desconhecida mediante evidência, sem reenvio automático |

`moodle_check_function` permanece compatível e pode compartilhar a mesma fonte
de `describe`. `moodle_list_available_flows` continua orientando fluxos acadêmicos.
Não criar uma tool por função Moodle nem um executor irrestrito paralelo.

### 3. Semântica e autorização

- Efeitos `read`, `write` e `unknown` vêm de contratos verificados. Inspeção da
  implementação pode tornar mais restritiva uma declaração remota incorreta.
- Verbos no nome servem apenas para diagnóstico. `view`, `get` e `check` não
  autorizam leitura automaticamente; efeitos de logs/completion também contam.
- `unknown` exige resolução do contrato antes de execução. Confirmação humana
  sozinha não resolve schema ou semântica desconhecidos.
- Executar com identidade e token do usuário selecionado; não usar fallback de
  service token para ampliar a disponibilidade ou o acesso da rota genérica.
- Conferir no servidor: permissão da aplicação por função, scope, conexão,
  capability disponível, política de dados e flags; o Moodle decide a permissão
  contextual final. A permissão genérica da tool não contorna restrições da família.
- Reutilizar as fronteiras da SPEC-0026, sem criar um atalho administrativo.
- Parâmetros de credencial continuam bloqueados no JSON genérico. Se uma função
  os exigir, classificá-la como `policy_blocked`; eventual gestão de credenciais
  exige fluxo dedicado, fora desta entrega.

### 4. Execução, retorno e continuação

- Validar parâmetros antes da chamada e serializar no formato REST Moodle,
  preservando arrays, booleanos, números, texto Unicode e defaults do contrato.
- Para funções sem projeção específica validada, preservar a estrutura retornada
  após sanitização. Não escolher projeção destrutiva por substring como `course`.
- Envelope aditivo: `operationId`, `function`, `connectionAlias`, `contractHash`,
  `status`, `payload` ou `resultResourceUri`, `warnings`, `auditId`, `completeness`.
- `completeness` distingue resposta desta chamada de todo o conjunto remoto:
  `returnedCount` quando aplicável, `truncated`, `hasMore` nullable,
  `continuationToken`, `reason`. `total` só existe quando comprovado.
- Paginação depende do contrato: offset/limit, página/tamanho ou cursor.
  Não inventar parâmetros para funções que não os possuem.
- Tokens opacos de continuação vinculam usuário, conexão, função, parâmetros,
  contrato e expiração. Paginar resultado armazenado não repete a requisição remota.
- Limites de tempo/bytes/registros geram resultado parcial explícito; nunca
  marcar um conjunto como completo quando o transporte ou a projeção o cortaram.
- Confirmação persiste payload sanitizado, IDs criados e warnings. Uma operação
  seguinte pode usar esses IDs sem consultar novamente toda a coleção.
- Resposta HTTP bem-sucedida não basta: interpretar erros e falhas por item
  conforme contrato; status `executed`, `partially_executed`, `failed` e
  `execution_unknown` devem refletir a evidência disponível.
- Preservar claim atômico e execução única por pending action. Timeout após envio
  não autoriza retry. Não prometer transação atômica entre múltiplos Web Services.
- Prévia vincula parâmetros, identidade, conexão, escopo e hash do contrato;
  mudanças relevantes invalidam a confirmação e exigem nova prévia.

### 5. Download e upload

- Separar transferência binária da extração de texto. Planilhas, imagens,
  arquivos compactados e outros formatos autorizados podem ser entregues sem
  possuir extrator, sujeitos a limites de tamanho e política de tipos.
- Preferir referência opaca vinculada à conexão; manter URL compatível com
  validação de host, porta, protocolo e endpoint, incluindo Moodle em subdiretório.
- Validar resolução de rede e redirects; não encaminhar token a outro destino.
  Preservar apenas exceções de rede privada explicitamente configuradas.
- Conteúdo binário sai como recurso MCP autorizado. Nunca colocar tokens em
  respostas, logs ou links públicos. Autenticação de arquivos deve respeitar o
  protocolo Moodle e sanitizar a URL interna quando houver token em query.
- Upload usa recurso binário autorizado e `POST /webservice/upload.php` para
  área de rascunho, quando habilitado no serviço. Não pedir base64 ao modelo.
- Prévia de upload registra nome, hash, tamanho, MIME, conexão e destino; a
  confirmação retorna os identificadores da área de rascunho, incluindo `itemid`.
- Anexar o rascunho a uma atividade é uma segunda escrita com contrato próprio;
  upload não significa submissão/publicação concluída.
- Upload também pode ficar `execution_unknown`; não reenviar cegamente.
  Expiração, retenção e limpeza de rascunhos devem ser explícitas e auditadas.

Referência de protocolo: [File handling](https://moodledev.io/docs/5.0/apis/subsystems/external/files).

### 6. Limite para operações sem External Function

O gateway genérico executa funções externas existentes; não cria capacidade
remota. Se a versão instalada não expuser uma função adequada para editar um
recurso, página ou livro, a operação permanece indisponível pelo core Web Service.
Essa avaliação depende da versão e das funções descobertas, não de uma lista
fixa de módulos supostamente suportados.

Não haverá plugin próprio do conector no Moodle, seja para criar operações de
conteúdo ou exportar contratos. A arquitetura funciona a partir das funções
externas oferecidas pelo core e pelos plugins já presentes na instalação.

Quando uma função existente passar a integrar o serviço e ficar acessível ao
token por configuração administrativa, a descoberta deve identificá-la e o
Contract Registry deve resolver seu manifesto verificado. Com transporte e
autorização válidos, os executores existentes a utilizam sem nova tool MCP nem
wrapper C# específico. Conhecer o contrato ou ter o plugin instalado não garante acesso.

Se não existir função externa adequada, retornar indisponibilidade e registrar
a limitação de cobertura. Não propor instalação de plugin próprio, acesso direto
ao banco ou automação de páginas como fallback do executor genérico.

## Escopo e fora de escopo

Inclui core e plugins com funções externas REST, descoberta, contratos, autorização,
leitura, mutação, binários, continuação e relatório de cobertura por conexão.

Exclui desenvolver ou instalar plugin próprio no Moodle, habilitar serviços ou
conceder permissões automaticamente no Moodle,
executar SQL/PHP remoto, automatizar interface administrativa, substituir fluxos
pedagógicos especializados, protocolos SOAP/XML-RPC e fornecer atomicidade distribuída.
Funções restritas por política permanecem reportadas como lacunas explícitas.

## Dependências e decisões em aberto

- SPEC-0011/0012: confirmação, impacto, reconciliação e scopes.
- SPEC-0013/0014: capabilities e exposição MCP.
- SPEC-0017: concorrência e evidências PostgreSQL.
- SPEC-0022/0024: recursos binários, retenção e jobs reutilizáveis.
- SPEC-0025: completude e leituras coletivas.
- SPEC-0026: autorização efetiva por função e perfil de acesso.
- A implementação deve inventariar versões reais de Moodle/plugins para definir
  a matriz de homologação. Esta SPEC não afirma compatibilidade com versões não testadas.
- Definir limites operacionais de binários e retenção a partir dos recursos
  existentes; expor valores efetivos em `describe`, sem limite ilimitado implícito.

## Contratos, compatibilidade e migração

Preservar nomes e campos existentes. Adicionar metadados de contrato, alias,
paginação e resultados de forma compatível; atualizar schemas, catálogo de
submissão, documentação e skills na mesma entrega. Persistência de resultados
usa migration versionada e controle de propriedade/conexão.

Migrar semântica por nome para contratos em duas etapas: primeiro inventário e
comparação sem executar, depois enforcement. Registrar incompatibilidades por
função. Funções antes aceitas heuristicamente podem passar a ser bloqueadas;
essa mudança deve aparecer na release e em `describe`.

## Segurança, privacidade e observabilidade

Resultados persistidos e recursos precisam de autorização em cada leitura,
retenção limitada e proteção equivalente aos dados acadêmicos existentes.
Sanitizar retornos de leitura e escrita sem remover IDs de negócio necessários.
Logs guardam IDs de auditoria, função, hash, duração, tamanho e códigos de erro;
não guardam credenciais nem conteúdo acadêmico desnecessário.

O inventário registra, por snapshot, `discovered`, `contract_verified`,
`read_ready`, `write_ready`, `schema_unavailable`, `policy_blocked`,
`transport_unsupported` e `runtime_failed`. Razões podem ser múltiplas; publicar
também um estado primário por função para contagens sem dupla contabilização.
Funções removidas ficam como histórico `unavailable`, fora do denominador atual.

Métricas separadas: cobertura de contratos / descobertas; prontidão técnica /
descobertas; funções homologadas / descobertas. Prontidão não prova autorização
contextual nem sucesso real. Não afirmar 100% de execução por contar apenas tools.

A meta de 100% aplica-se às External Functions descobertas com contrato
homologado, transporte suportado e autorização efetiva no contexto testado.
Qualquer relatório desse subconjunto deve publicar também o total descoberto,
as exclusões e seus motivos. Não apresentar essa métrica como “100% do Moodle”.

## Plano de execução

1. Inventariar funções e versões; gerar manifestos de contrato e relatório de lacunas.
2. Implementar `MoodleFunctionContractRegistry`, separado da disponibilidade por
   conexão/token, com importação, listagem paginada e descrição.
3. Substituir classificação heurística; unificar autorização dos executores.
4. Preservar retornos, implementar armazenamento autorizado e continuação.
5. Evoluir confirmação e consulta de resultados, incluindo falhas parciais.
6. Ampliar download e implementar upload confirmado para rascunhos.
7. Atualizar exposição, skills e documentação; homologar em Moodle descartável.
8. Publicar cobertura por versão/conexão e executar rollout gradual.

## Critérios de aceite

- [x] AC-01: Toda função descoberta tem situação e razão; plugin desconhecido aparece no inventário.
- [x] AC-02: Listagem completa paginada e descrição disponíveis em Production para usuário autorizado.
- [ ] AC-03: Schemas preservam null/default/arrays; contrato ausente ou incompatível impede execução.
- [x] AC-04: `mod_book_view_book` e funções com efeitos não passam pela leitura; nome `get` sozinho não autoriza.
- [x] AC-05: Troca de usuário/alias/token não reutiliza disponibilidade, resultado ou recurso de outro contexto.
- [x] AC-06: Falta de scope/permissão/CanWrite/flag impede a mutação tanto no preparo quanto na confirmação.
- [x] AC-07: Função de plugin com contrato importado executa sem nova tool MCP ou wrapper C# específico.
- [x] AC-08: IDs e warnings retornados por criação são recuperáveis sem repetir escrita.
- [x] AC-09: Concorrência/repetição não duplicam execução; timeout após envio permanece desconhecido até reconciliação.
- [x] AC-10: Paginação e limites declaram completude; continuação adulterada/expirada é recusada.
- [x] AC-11: Download preserva binários autorizados fora dos quatro MIME atuais e respeita limites/isolamento.
- [x] AC-12: Upload confirmado retorna rascunho utilizável por escrita posterior, com resultado e auditoria separados.
- [x] AC-13: Segredos não aparecem em JSON, recursos públicos, logs ou mensagens de erro.
- [x] AC-14: Funções administrativas restritas constam como bloqueadas, sem serem contadas como cobertas.
- [x] AC-15: Relatório reproduzível distingue descoberta, contrato, prontidão e homologação real.
- [x] AC-16: Um contrato conhecido e verificado permite execução na conexão A autorizada, mas sua importação não libera a conexão B cujo token não oferece a função.
- [x] AC-17: Operação sem External Function adequada retorna indisponibilidade explícita, sem depender de plugin próprio; função de plugin já instalado, disponibilizada ao token e com contrato verificado, executa sem alteração no catálogo de tools MCP.

AC-03 permanece condicionado ao manifesto real e ao modo
`MoodleApi:RequireVerifiedContracts=true`. O modo transicional continua
disponível em desenvolvimento/homologação, mas o guard de Production agora
recusa iniciar quando ele está desligado. O comportamento de AC-14 está
coberto por teste e pelos gates de contrato; sua certificação de produção ainda
aguarda a matriz real de funções administrativas e permissões de cada Moodle
alvo.

## Validação e evidências

Testes novos devem cobrir contratos e autorização em ambas as rotas; snapshots
de versões/plugins, serialização, retorno de IDs, paginação, acesso a recursos,
SSRF/redirects, MIME/tamanho e concorrência em PostgreSQL. Os testes existentes
são regressões de base e não certificam os critérios novos.

```powershell
dotnet test tests/MoodleConnector.Application.Tests/MoodleConnector.Application.Tests.csproj --filter "FullyQualifiedName~MoodleUniversal|FullyQualifiedName~MoodleFunction|FullyQualifiedName~MoodleSubmissionFileGateway|FullyQualifiedName~ToolExposure"
```

Homologação exige Moodle descartável com versão/plugins registrados, usuários
de permissões distintas e fixtures de curso/atividade/arquivos. Executar leitura,
criação/atualização/remoção de fixtures, upload/anexo/download e falha parcial;
guardar hashes de contrato, auditorias e resultados sanitizados. Não executar
todas as funções de produção apenas para medir cobertura. Registrar operações
não exercitadas como não homologadas, com motivo.

## Rollout e rollback

Publicar descoberta antes de habilitar execução por contrato. Reutilizar flags
existentes de escrita/download e manter controle separado de upload confirmado.
Verificar a configuração efetiva: ambiente pode sobrescrever `appsettings.json`.
O perfil de produção habilita escrita/download, mas só sobe com o manifesto
estrito; upload permanece desligado até homologação específica. Esta
implementação ainda não efetua deploy.

Expandir por conexão após homologação. Rollback suspende novas preparações e
uploads, preservando consulta de resultados, auditoria e reconciliação. Manter
registros `execution_unknown`; não apagá-los nem reexecutá-los durante rollback.

## Referências externas

- [External Services](https://moodledev.io/docs/5.0/apis/subsystems/external): documentação da API da instalação e endpoints externos.
- [File handling](https://moodledev.io/docs/5.0/apis/subsystems/external/files): protocolo de upload/download.
- [Common files](https://moodledev.io/docs/4.5/apis/commonfiles): declaração de funções em `db/services.php`.
- [Serviços de book](https://github.com/moodle/moodle/blob/main/public/mod/book/db/services.php): exemplo concreto de `view` com semântica de escrita.

Fontes consultadas em 2026-09-10. Na geração de manifestos, fixar release/commit;
links de documentação e branch principal não substituem o contrato da instalação.
