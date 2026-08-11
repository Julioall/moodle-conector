# Memória do usuário e orientações pedagógicas

## Objetivo

Adicionar ao Moodle Connector uma memória persistente por usuário e uma forma de consultar os documentos versionados em `public/pedagogic`. O conector deve ajudar a IA a reaproveitar preferências, caminhos, correções e decisões, reduzindo repetição de erros e usando as orientações pedagógicas do projeto nas tarefas em que forem relevantes.

O conector opera como servidor MCP e não recebe automaticamente o histórico completo da conversa. Portanto, a captura automática depende de a IA chamar a tool quando identificar um aprendizado durável. As instruções do servidor e as descrições das tools devem tornar esse comportamento explícito, sem afirmar uma garantia que o protocolo não oferece.

## Abordagem escolhida

Será adotada uma arquitetura híbrida:

- uma tool MCP gerencia a memória do usuário;
- uma tool MCP pesquisa as orientações pedagógicas;
- instruções do servidor MCP orientam a IA a consultar contexto antes de tarefas relevantes e registrar aprendizados duráveis depois de preferências, correções ou decisões;
- as tools Moodle existentes continuam com respostas enxutas, sem injetar indiscriminadamente memórias ou documentos em toda chamada.

Essa abordagem oferece persistência e rastreabilidade sem aumentar todas as respostas nem acoplar cada tool existente ao mecanismo de memória.

## Memória do usuário

### Tool MCP

A tool `gerenciar_memoria_usuario` terá três ações:

- `salvar`: cria uma memória ou atualiza uma equivalente;
- `listar`: recupera memórias relevantes usando texto, categoria e escopo opcionais;
- `remover`: exclui uma memória pelo identificador retornado nas consultas.

As categorias aceitas serão:

- `preferencia`: formato, estilo ou comportamento preferido pelo usuário;
- `caminho`: rota, alias, sequência ou localização reutilizável;
- `correcao`: instrução que evita repetir um erro;
- `decisao`: escolha operacional estável que deve orientar tarefas futuras.

Cada memória terá uma origem:

- `explicit`: declarada diretamente pelo usuário;
- `inferred`: inferida pela IA a partir da conversa ou de uma correção.

A IA deve salvar somente fatos curtos, duráveis, factuais e reutilizáveis. Conteúdo temporário da tarefa atual não deve virar memória.

### Escopos

Uma memória pode ser:

- global para o usuário;
- específica de um alias Moodle;
- específica de um curso dentro de um alias Moodle.

Ao consultar, a tool combina as memórias globais com as memórias do Moodle e do curso informados. Memórias mais específicas têm precedência sem apagar as globais.

### Persistência e isolamento

As memórias serão armazenadas no PostgreSQL e associadas ao `Subject` do `ICurrentUserContext`. Nenhum parâmetro da tool poderá escolher ou sobrescrever o usuário proprietário.

O modelo persistido conterá, no mínimo:

- identificador UUID;
- proprietário (`subject`);
- categoria;
- chave normalizada;
- conteúdo;
- origem;
- alias Moodle opcional;
- ID do curso opcional;
- data de criação;
- data de atualização.

Uma restrição única por proprietário, categoria, escopo e chave normalizada implementará upsert e impedirá duplicatas equivalentes. O conteúdo e a data de atualização serão substituídos ao salvar novamente a mesma chave.

## Orientações pedagógicas

### Tool MCP

A tool `consultar_orientacoes_pedagogicas` receberá uma consulta textual e um limite de resultados. Ela pesquisará somente arquivos Markdown abaixo de `public/pedagogic` e retornará:

- caminho relativo do documento;
- título do documento;
- seção;
- trecho relevante;
- pontuação ou ordem de relevância.

Tarefas de avaliação, feedback, planejamento, fóruns, acompanhamento de estudantes e relatórios pedagógicos devem consultar essa tool antes de formular uma orientação ou executar uma ação relevante.

### Índice

Os documentos permanecerão versionados no repositório e serão indexados em memória no início da aplicação. O índice dividirá cada documento por títulos e blocos de texto, preservando caminho, título e seção. A busca será determinística e sem dependência de serviços externos.

A imagem de publicação deve conter `public/pedagogic`, e o caminho de conteúdo deve ser resolvido de forma segura a partir da raiz configurada da aplicação. A implementação não aceitará caminhos enviados pelo cliente e não permitirá travessia de diretório.

## Fluxo esperado da IA

As instruções MCP devem orientar o cliente a seguir este fluxo:

1. Antes de uma tarefa Moodle, consultar memórias relevantes com o alias e curso quando conhecidos.
2. Antes de uma tarefa pedagógica, pesquisar também as orientações pedagógicas usando os conceitos centrais do pedido.
3. Aplicar as memórias mais específicas e os trechos pertinentes, sem tratar resultados irrelevantes como regras.
4. Quando o usuário revelar uma preferência ou caminho durável, corrigir um comportamento ou consolidar uma decisão reutilizável, salvar uma memória automaticamente.
5. Marcar como `explicit` o que foi declarado diretamente e como `inferred` o que foi deduzido.
6. Permitir que o usuário liste ou remova suas memórias quando solicitar.

## Segurança e privacidade

- A memória nunca armazenará senhas, tokens, chaves, cookies, credenciais Moodle ou outros segredos.
- O serviço rejeitará padrões evidentes de segredos e conteúdo acima dos limites definidos.
- A IA será instruída a não armazenar dados pessoais de alunos, notas, mensagens privadas, conteúdo de submissões ou informações sensíveis.
- Consultas e remoções sempre serão filtradas pelo usuário autenticado.
- A remoção exigirá um UUID pertencente ao usuário atual; não haverá exclusão por texto ambíguo.
- A listagem terá limite máximo de resultados e não exporá o identificador interno do proprietário.
- As novas tools serão classificadas e documentadas no catálogo MCP. Salvar e remover memória são escritas locais de baixo risco e não alteram o Moodle, portanto não usarão o fluxo de confirmação de ações Moodle.

## Tratamento de erros

- Usuário sem identidade autenticada receberá erro seguro, sem persistência anônima.
- Categoria, origem, ação ou combinação de escopo inválida será rejeitada com mensagem acionável.
- `courseId` sem `moodleAlias` será inválido.
- Remover um UUID inexistente ou de outro usuário produzirá a mesma resposta de não encontrado, evitando enumeração.
- Falha no repositório de memória ou no índice pedagógico não deverá bloquear uma consulta normal ao Moodle quando ela puder continuar com segurança; a indisponibilidade de contexto será informada.
- Arquivos pedagógicos ausentes resultarão em resposta vazia com diagnóstico seguro e log operacional, sem revelar caminhos absolutos.

## Componentes

- Entidade de domínio para a memória e seus valores permitidos.
- Contrato de repositório na camada Application.
- Serviço de aplicação responsável por validação, normalização, upsert, consulta e remoção.
- Repositório EF Core e tabela PostgreSQL criada por novo script de schema versionado.
- Serviço de indexação e busca dos documentos pedagógicos.
- Classe de tools MCP de memória e classe de tool MCP pedagógica.
- Instruções do servidor MCP e descrições das tools com o fluxo de uso automático.
- Atualizações do catálogo de tools e da documentação de segurança/privacidade pertinente.

## Testes e critérios de aceite

A implementação será aceita quando testes automatizados demonstrarem:

- criação e atualização idempotente de memória equivalente;
- isolamento completo entre dois usuários;
- combinação e precedência dos escopos global, Moodle e curso;
- filtros por categoria e texto;
- remoção apenas pelo proprietário;
- rejeição de ação, categoria, origem e escopo inválidos;
- rejeição de segredos evidentes e dados acima dos limites;
- divisão e ranking determinístico de trechos pedagógicos;
- restrição da busca à pasta autorizada e comportamento com arquivos ausentes;
- registro das duas tools no servidor MCP com descrições e metadados adequados;
- inclusão dos documentos pedagógicos no artefato de publicação;
- manutenção da suíte existente sem regressões.
