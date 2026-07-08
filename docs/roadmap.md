# Roadmap de Apoio ao Tutor, Monitor e Corpo Pedagógico

## Propósito e autoridade

O Moodle Connector apoia atividades previstas no Guia do Tutor: coleta e organiza evidências disponíveis, produz rascunhos e executa ações explicitamente autorizadas. Funções Moodle e tools MCP delimitam o apoio possível; não definem o propósito pedagógico e não substituem tutor, monitor, corpo pedagógico, CTM, coordenação ou regras do Departamento Regional.

Este documento é a fonte canônica para prioridades do produto. O status técnico é baseado no código e nos testes do repositório em 7 de julho de 2026. Disponibilidade em execução continua condicionada ao catálogo do serviço, ao contexto, às permissões e à configuração do Moodle autorizado.

## Públicos e responsabilidades

- **Tutor:** acompanha participação e rendimento, orienta, corrige, oferece feedback, identifica sinais e propõe contatos ou recuperação.
- **Monitor:** apoia ambientação, acesso e navegação; verifica a organização inicial e encaminha ocorrências.
- **Corpo pedagógico e CTM:** supervisiona o processo, combina evidências EaD e presenciais, valida critérios e delibera sobre intervenções e resultados acadêmicos.

## Níveis de suporte

- **Nível A — suportado:** função contratada, implementação e teste localizável. Permissão e presença do dado ainda são verificadas em execução.
- **Nível B — assistido com limitações:** agregação local, chamadas por estudante ou sinal observável; exige declaração de cobertura, truncamento e revisão humana.
- **Nível C — dependente de configuração/admin:** exige capability, permissão, plugin, completion ou mapeamento institucional ainda não garantido.
- **Nível D — não suportado:** fonte ou infraestrutura ausente no contrato atual.
- **Nível H — exclusivamente humano:** julgamento ou decisão acadêmica/pedagógica que não pode ser automatizada.

## Semântica obrigatória de estados vazios

Toda resposta nova ou revisada deve distinguir explicitamente:

| Estado | Significado obrigatório |
| --- | --- |
| `zero_observado` | A fonte foi consultada com sucesso, dentro da cobertura declarada, e nenhum registro correspondente foi encontrado. |
| `dado_indisponivel` | A resposta não trouxe o campo ou a fonte necessária; não equivale a zero. |
| `funcao_indisponivel` | A função não consta ou não pôde ser usada no serviço autorizado. |
| `sem_permissao` | A função existe, mas o token não pode acessar o recurso/contexto. |
| `nao_configurado` | O recurso Moodle necessário, como completion, não está configurado. |
| `truncado` | Há resultados além do limite de estudantes, páginas, discussões, posts ou chamadas. |
| `falha_parcial` | Parte das fontes/chamadas falhou; resultados remanescentes não representam cobertura integral. |

Lista vazia sem estado é ambígua e não autoriza concluir ausência de atividade, aprendizagem, participação ou risco. A linguagem externa deve preferir “não encontramos registro nos dados visíveis” e informar fonte, período e cobertura.

## Regras transversais

- Acesso, entrega, completion e posts são registros técnicos, não medidas diretas de estudo, esforço, compreensão, motivação ou engajamento.
- Nota ou completion isolados não comprovam competência. Avaliação combina funções diagnóstica, formativa e somativa e requer critérios observáveis vinculados a capacidades.
- “Abaixo do mínimo” é um sinal numérico configurado; não prova que critério crítico ou capacidade não foi atingido.
- Dados ausentes reduzem a confiança e aparecem em `limitations`; nunca elevam risco automaticamente.
- Recuperação exige análise, orientação, período, nova oportunidade e acompanhamento humano.
- Toda escrita real exige conexão `CanWrite`, escopo aplicável, flag ativa, `PendingAction`, prévia, confirmação literal, idempotência e auditoria sanitizada.
- Nenhuma ação coletiva expõe nota, risco individual ou conteúdo sensível a outros estudantes.
- Paginação pública deve ser **1-based**. Cobertura deve informar elegíveis, analisados, excluídos, limites e falhas.

## Como ler as fichas

Cada atividade declara: público; referência pedagógica em `public/pedagogic`; resultado humano; evidências necessárias e disponíveis; funções Moodle; tool MCP; nível; cobertura e limites; limitações; gate humano; status; e evidência de conclusão. “Implementado” significa código e teste localizáveis, não disponibilidade universal no Moodle.

