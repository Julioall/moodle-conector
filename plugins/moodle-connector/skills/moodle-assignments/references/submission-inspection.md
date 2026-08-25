# Submission inspection reference

Use esta referencia quando uma tarefa depender de anexos, formato, integridade, conteudo extraido ou cobertura de arquivos.

## Pipeline atual do conector

1. A submissao e resolvida pelo Moodle Connector; o agente nao baixa `pluginfile.php` diretamente.
2. `create_assisted_grading_batch` pode baixar arquivos dentro dos limites configurados e persistir `GradingArtifact`.
3. `IDocumentExtractionService` extrai texto e metadados normalizados.
4. `get_grading_item_context`/`prepare_submission_grading` expõem o contexto para revisao.

Formatos atualmente cobertos pelo extrator: texto simples, HTML, JSON, XML, CSV, PDF com texto, DOCX, PPTX, XLSX, OpenDocument e ZIP com entradas suportadas. PDF sem camada de texto retorna `scanned_pdf` ou usa OCR quando o servico estiver habilitado. Textos grandes retornam chunks representativos e `truncated`.

Limites versionados relevantes: 25 MB por arquivo, 10 arquivos por submissao, 120.000 caracteres extraidos, 25 entradas consideradas por ZIP e 20 MB por entrada interna. Confirme `appsettings.json`/ambiente antes de prometer esses valores em outro deployment.

## Politica de evidencia

- `succeeded`: conteudo textual extraido; ainda pode faltar informacao visual.
- `ocr_extracted`: texto recuperado por OCR; acentos, tabelas e numeros exigem revisao.
- `scanned_pdf`: nao ha texto verificavel; nao interpretar como PDF vazio.
- `unsupported_format`: formato conhecido, mas sem extrator disponivel.
- `file_too_large`: o limite impediu a leitura completa.
- `empty`: arquivo sem bytes ou sem conteudo minimo.
- `failed`: arquivo corrompido, protegido ou erro de leitura.

`scanned_pdf`, `unsupported_format`, `file_too_large`, `empty` e `failed` exigem leitura parcial ou falha. Nao atribua nota a criterio que nao foi verificavel e nao converta uma limitacao tecnica em reprovação.

## Pre-processamento de bundles locais

Para um ZIP exportado localmente, execute:

```powershell
python plugins/moodle-connector/skills/moodle-assignments/scripts/inspect_submission_bundle.py "C:\caminho\entregas.zip" --pretty
```

O script apenas inspeciona o arquivo: nao extrai para o disco, detecta assinaturas, lista entradas, identifica containers DOCX/XLSX/PPTX, percorre ZIPs internos ate a profundidade configurada e sinaliza path traversal, arquivos criptografados, excesso de membros, tamanho descomprimido e razao de compressao suspeita. Ele nao substitui o download/extração controlados do conector.

## Lacunas deliberadas

Formulas e resultados salvos de XLSX, dimensoes/transparencia de imagens, camadas XCF/PSD, renderizacao de DOCX/PPTX/XLSX, frames de video e transcricao de audio ainda nao fazem parte do contrato MCP atual. Para esses criterios, marque verificacao pendente ou proponha uma ferramenta dedicada; nao invente evidencia a partir do nome/extensao.
