---
name: moodle-publicar-questionario
description: Preparar a publicação de um questionário Moodle e executar somente um fluxo de escrita autorizado após prévia e confirmação humana.
---

# moodle-publicar-questionario

Use quando o usuário pedir para importar, criar ou publicar um questionário no Moodle. A geração do conteúdo pertence a 'senai-gerar-questionario-moodle-gift'; esta skill cuida da fronteira operacional.

## Fluxo obrigatório

1. Resolva conexão, curso, seção, categoria e identificadores com 'moodle-localizar-contexto-curso'.
2. Consulte 'moodle_list_available_flows' para saber se a conexão oferece fluxo autorizado de criação/importação de questionário.
3. Inspecione questionários e atividades existentes com 'list_course_quizzes', 'list_course_activities' e as leituras necessárias para detectar duplicidade.
4. Valide o GIFT e apresente uma prévia com nome, curso, seção, quantidade de itens, pontuação, tentativas, datas, feedback, embaralhamento e alterações.
5. Pare se não houver capability/flow explícito para criação de questionário, se o alvo estiver ambíguo, se houver duplicidade não resolvida ou se o GIFT estiver inválido. Entregue o arquivo GIFT como alternativa.
6. Quando existir um fluxo de escrita aprovado, use exclusivamente a tool especializada ou o caminho 'prepare -> review -> confirm' exposto pelo conector. Nunca execute uma escrita por 'moodle_execute_read'.
7. Após a execução, reconcilie o estado remoto e informe sucesso, falha, resultado desconhecido, identificador criado e auditoria. Não repita cegamente após falha ambígua.

## Segurança

- A skill não assume que 'create_task' cria atividade Moodle; confirme o domínio da tool antes de usá-la.
- Não use operação universal para contornar uma capability específica ausente.
- Não publique no primeiro passo da conversa.
- A confirmação deve identificar curso, seção, nome, contagem, hash/versão do GIFT e opções relevantes.
- Nenhuma exclusão ou substituição automática de questionário existente.
