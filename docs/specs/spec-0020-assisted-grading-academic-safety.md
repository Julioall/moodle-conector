# SPEC-0020: Segurança acadêmica da escala e da proposta de correção por IA

## Status

In Progress.

## Objetivo

Impedir notas numéricas inventadas ou fora da escala, preservar a fundamentação da análise
assistida e tratar entregas e materiais Moodle como dados não confiáveis, sem alterar o
fluxo humano de revisão e o lançamento seguro da SPEC-0011.

## Contexto e evidência atual

- Os fluxos de contexto, revisão, lançamento individual e pacote IA devem preservar escala
  desconhecida sem convertê-la em `100`.
- `AssistedGradingItem.SetDraft`, `ApplyTeacherReview`, `SaveAssignmentGradeCommand` e os
  handlers de lançamento precisam validar o limite máximo quando a escala é conhecida.
- O contrato atual do gateway representa apenas `AssignmentId`, `MaxGrade` e `Name`, sem
  distinguir escala conhecida, desconhecida ou escala nominal.
- A resposta da IA contém basicamente nota e feedback; o pipeline aceita confiança fixa
  próxima de `0.85` e não preserva critérios, evidências, lacunas ou cobertura.
- Estados de extração são comparados por strings incompletas e não cobrem de forma uniforme
  `scanned_pdf`, `file_too_large`, `empty` e `ocr_extracted`.
- O gerador heurístico pode adicionar critérios de linguagem/organização e redistribuir
  pontos mesmo quando não foram definidos pelo professor.
- O prompt precisa declarar que submissão, anexos e materiais são evidência não confiável,
  nunca instrução para o agente.
- A instrução de cumprimentar o estudante pelo nome não é suportada pelo pacote que contém
  apenas `studentId`.

## Decisão e arquitetura-alvo

1. Criar o value object `GradingScale` com `Type` (`Points`, `Scale`, `Unknown`),
   `MaxPoints`, `ScaleId`, `ScaleValues`, `Source` e evidência de verificação.
2. `Unknown` não será convertido em 100: a IA poderá produzir análise textual e feedback
   condicional, mas `SuggestedGrade` ficará nulo e qualquer lançamento será bloqueado. Nos
   DTOs MCP legados, `maxGrade=0` representa escala não confirmada para preservar o tipo
   numérico do contrato.
3. Toda nota será validada contra a escala resolvida no domínio e novamente no preview e
   no comando de lançamento. Escalas nominais deverão ser mapeadas explicitamente; não se
   presume que sejam pontos.
4. O contrato versionado `AiGradingProposal` conterá nota opcional, feedback,
   `CriterionResults`, referências de evidência, lacunas, `Confidence`, motivos de
   incerteza, `EvidenceCoverage` e `ReviewRequired`.
5. A confiança será recalculada/limitada pelo backend usando cobertura, truncamento,
   extração, escala e origem dos critérios. O valor enviado pelo modelo não será aceito
   sem validação.
6. Critérios terão proveniência explícita: `FormalRubric`, `TeacherDefined`,
   `StatementDerived`, `GeneratedSupport` ou `Unknown`. Critérios gerados para apoio não
   podem alterar a distribuição da nota sem aprovação humana.
7. O prompt e o contrato separarão instruções confiáveis de dados Moodle não confiáveis.
   Texto de submissão, OCR, anexos, materiais e feedback anterior serão delimitados como
   evidência e nunca poderão substituir instruções do sistema/professor.
8. O pacote enviará `studentName` somente quando obtido por fonte autorizada e minimizada;
   caso contrário, a exigência de saudação nominal será removida. IDs Moodle nunca serão
   usados como nome.
9. O conjunto canônico de estados de extração será compartilhado pelo domínio e mapeado
   para comportamento explícito: sucesso, revisão obrigatória, bloqueio ou falha.

## Escopo

- Escala conhecida/desconhecida e validação de nota em todos os caminhos.
- Proposta IA versionada com critérios, evidências, cobertura e incerteza.
- Confiança derivada pelo backend.
- Proveniência de critérios e bloqueio de critérios heurísticos não aprovados.
- Proteção contra prompt injection em submissões e materiais.
- Estados canônicos de extração e propagação de `studentName` ou remoção da saudação.
- Integração produtiva de rubrica Moodle, quando a capability estiver disponível, com
  `RubricSource` explícito (`MoodleAdvancedGrading`, `TeacherProvided`, `StatementDerived`
  ou `None`).

## Fora de escopo

- Decisão pedagógica autônoma ou lançamento sem revisão humana.
- Reescrita do commit Moodle, reconciliação ou confirmação da SPEC-0011.
- Fila, leases e retenção, tratados na SPEC-0022.
- Histórico analítico de snapshots da SPEC-0015.

## Dependências e decisões em aberto

- Depende de SPEC-0011, SPEC-0012 e SPEC-0021.
- Mapear as versões/capabilities de rubrica Moodle efetivamente suportadas.
- Definir a fórmula e os limites de confiança com o responsável pedagógico; a fórmula deve
  ser determinística e auditável.
- Definir política de exibição de nome em cada tenant, respeitando minimização e LGPD.

## Contratos, compatibilidade e migração

- A versão antiga de retorno IA será aceita somente como entrada legada, convertida para
  `AiGradingProposal` com `ReviewRequired=true` e sem confiança presumida.
- `SuggestedGrade` passa a ser nullable; clientes devem tratar `grade_unavailable` como
  estado distinto de zero.
- `MaxGrade=100` não será emitido como fallback. Registros antigos com esse valor devem
  ser marcados como `legacy_unverified` quando não houver fonte verificável.
- Lançamento rejeitará notas acima da escala e qualquer nota quando a escala for
  `Unknown`.
- Os estados de extração serão persistidos com nomes canônicos, preservando o texto antigo
  somente para migração/auditoria.

## Segurança, privacidade e observabilidade

- Nunca registrar prompts completos, submissões, OCR, tokens, nomes ou conteúdo de rubrica
  em logs de aplicação.
- Persistir apenas referências de evidência, hashes e trechos mínimos necessários à
  revisão, com retenção definida na SPEC-0022.
- Medir propostas bloqueadas por escala, cobertura insuficiente, extração, incerteza e
  validação de faixa; métricas não devem conter conteúdo acadêmico.
- Auditar origem da escala, origem de cada critério, versão do prompt/contrato e hash do
  contexto usado na proposta.

## Plano de execução

1. Introduzir `GradingScale` e substituir todos os fallbacks numéricos silenciosos.
2. Aplicar validação de faixa no aggregate, revisão, preview e lançamento.
3. Criar enum/constantes canônicas de extração e migrar comparações soltas.
4. Persistir proveniência de critérios e implementar a integração de rubrica disponível.
5. Versionar `AiGradingProposal`, converter legado para revisão obrigatória e recalcular
   confiança no backend.
6. Atualizar prompts, schemas, UI e testes para a fronteira de dados não confiáveis.
7. Adicionar `studentName` autorizado ou retirar a exigência de saudação nominal.

### Incremento aplicado: fronteira de evidência do prompt

Os fluxos de preparação de contexto e pacote IA agora reutilizam uma política comum que
declara enunciado, rubrica, OCR, anexos, materiais, submissão e feedback anterior como
evidência Moodle não confiável. Instruções encontradas nesses campos não podem substituir
regras do sistema/professor, alterar escala, chamar ferramentas ou pular revisão humana;
tentativas de instrução são sinalizadas como `possivel_prompt_injection`. O pacote em lote
também usa saudação neutra: `studentId` não é tratado como nome e nenhum nome é inventado.

Esta é uma barreira de contrato/prompt. A validação de uma proposta retornada pelo modelo,
com critérios, evidências, cobertura e confiança recalculada no backend, continua pendente
da Fase 3 completa.

## Critérios de aceite

- [ ] Escala não resolvida nunca produz `SuggestedGrade=100` nem permite lançamento
      numérico.
- [ ] Notas abaixo de zero, acima do máximo e incompatíveis com escala nominal são
      rejeitadas em domínio, preview e confirmação.
- [ ] Uma proposta IA persiste critérios, evidências, lacunas, cobertura, incerteza e
      `ReviewRequired`.
- [ ] Confiança varia de forma determinística com cobertura/truncamento/extração e não é
      aceita cegamente do modelo.
- [ ] Critérios `GeneratedSupport` não alteram pontos sem aprovação humana explícita.
- [ ] Prompt injection presente na submissão é tratado como texto de evidência e não muda
      as regras do sistema.
- [ ] Estados `scanned_pdf`, `file_too_large`, `empty`, `failed` e `unsupported_format`
      resultam em bloqueio ou revisão conforme matriz documentada.
- [ ] A proposta usa nome autorizado ou não exige saudação nominal; nunca transforma ID em
      nome.
- [ ] Rubrica ausente é declarada como ausente; `includeRubric` não inventa critérios.

## Validação e evidências

```powershell
dotnet test tests/MoodleConnector.Application.Tests --filter "FullyQualifiedName~GradingContext|FullyQualifiedName~GradingAnalysis|FullyQualifiedName~SaveAssignmentGrade|FullyQualifiedName~GradingLaunch|FullyQualifiedName~MoodleGradingTools"
dotnet test MoodleConnector.slnx --configuration Release --no-build --no-restore
```

Evidências esperadas: testes de escala desconhecida, faixa, conversão de legado, estados de
extração, prompt injection, proveniência de critérios, confiança e rubrica.

## Rollout e rollback

Executar primeiro em modo bloqueador/observável para propostas sem escala e sem evidência.
Habilitar o novo contrato IA por versão, mantendo adaptador de leitura legado. Rollback pode
retornar à versão anterior do contrato de proposta, mas não pode reativar o fallback 100 nem
reabrir uma nota já confirmada.
