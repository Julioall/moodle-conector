using System.Globalization;
using System.Text.RegularExpressions;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Application.Grading;

/// <summary>
/// Implementação determinística de geração de critérios avaliativos.
/// Extrai "Resultados esperados" do texto do SAP, filtra metadados contaminantes,
/// transforma em critérios com verbos avaliáveis e distribui pontos proporcionalmente.
/// Pode ser substituída por implementação baseada em LLM sem alterar a orquestração.
/// </summary>
public sealed partial class HeuristicCriteriaGenerationService : ICriteriaGenerationService
{
    /// <summary>
    /// Palavras/expressões que indicam metadados e NÃO devem formar critérios.
    /// </summary>
    private static readonly string[] MetadataBlocklist =
    [
        "à distância",
        "a distancia",
        "individual",
        "momento",
        "carga horária",
        "carga horaria",
        "cabeçalho",
        "cabecalho",
        "modalidade",
        "nível",
        "nivel",
        "curso",
        "disciplina",
        "turma",
        "polo",
        "tutor",
        "professor"
    ];

    /// <summary>
    /// Verbos que indicam critérios avaliáveis.
    /// </summary>
    private static readonly string[] EvaluableVerbs =
    [
        "elaborar",
        "indicar",
        "apresentar",
        "descrever",
        "comparar",
        "justificar",
        "aplicar",
        "adequar",
        "identificar",
        "analisar",
        "relacionar",
        "propor",
        "planejar",
        "demonstrar",
        "avaliar",
        "explicar",
        "classificar",
        "definir",
        "listar",
        "formular",
        "organizar"
    ];

    private const int MinCriterionLength = 15;
    private const int MaxCriteria = 20;

    public Task<CriteriaGenerationResult> GenerateAsync(
        CriteriaGenerationRequest request,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var allText = CombineContextText(request);

        if (string.IsNullOrWhiteSpace(allText))
        {
            return Task.FromResult(new CriteriaGenerationResult(
                Source: "heuristic_cleaned",
                MaxPoints: request.MaxGrade,
                Confidence: 0m,
                Criteria: [],
                Warnings: ["Nenhum texto de contexto disponível para extração de critérios."],
                PrivateNotesToTeacher: "Não foi possível gerar critérios: contexto insuficiente."));
        }

        // 1. Tentar extrair de "Resultados esperados"
        var rawCriteria = ExtractFromResultadosEsperados(allText);

        // 2. Se não encontrou, tentar extrair de "Critérios de avaliação" (inline)
        if (rawCriteria.Count == 0)
        {
            rawCriteria = ExtractFromCriteriosAvaliacao(allText);
        }

        // 3. Se ainda não encontrou, tentar extrair frases com verbos avaliáveis
        if (rawCriteria.Count == 0)
        {
            rawCriteria = ExtractFromEvaluableVerbs(allText);
        }

        if (rawCriteria.Count == 0)
        {
            warnings.Add("Não foram identificados critérios avaliáveis no contexto fornecido.");
            return Task.FromResult(new CriteriaGenerationResult(
                Source: "heuristic_cleaned",
                MaxPoints: request.MaxGrade,
                Confidence: 0.1m,
                Criteria: [],
                Warnings: warnings,
                PrivateNotesToTeacher: "Critérios não puderam ser extraídos automaticamente do contexto da atividade."));
        }

        // 4. Limpar e filtrar
        var cleaned = rawCriteria
            .Select(CleanCriterion)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Where(c => c.Length >= MinCriterionLength)
            .Where(c => !IsMetadataOnly(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxCriteria)
            .ToList();

        if (cleaned.Count == 0)
        {
            warnings.Add("Todos os critérios identificados foram descartados por serem metadados ou estarem truncados.");
            return Task.FromResult(new CriteriaGenerationResult(
                Source: "heuristic_cleaned",
                MaxPoints: request.MaxGrade,
                Confidence: 0.1m,
                Criteria: [],
                Warnings: warnings,
                PrivateNotesToTeacher: "Critérios extraídos do contexto foram descartados por não serem avaliáveis."));
        }

        // 5. Distribuir pontos
        var effectiveMaxGrade = request.MaxGrade > 0 ? request.MaxGrade : 0m;
        var pointsPerCriterion = effectiveMaxGrade > 0
            ? Math.Round(effectiveMaxGrade / cleaned.Count, 2)
            : 0m;

        // Normalizar para garantir que a soma bata com maxGrade
        var criteria = new List<GeneratedCriterion>();
        var accumulatedPoints = 0m;
        for (var i = 0; i < cleaned.Count; i++)
        {
            decimal points;
            if (i == cleaned.Count - 1 && effectiveMaxGrade > 0)
            {
                // Último critério recebe o residual para somar exatamente maxGrade
                points = effectiveMaxGrade - accumulatedPoints;
            }
            else
            {
                points = pointsPerCriterion;
            }

            accumulatedPoints += points;
            criteria.Add(new GeneratedCriterion(
                Id: $"C{i + 1}",
                Description: cleaned[i],
                MaxPoints: points,
                EvidenceBasis: "Resultado esperado identificado no contexto da atividade (SAP/enunciado)."));
        }

        // 6. Calcular confiança
        var hasVerbs = criteria.Count(c =>
            EvaluableVerbs.Any(v => c.Description.Contains(v, StringComparison.OrdinalIgnoreCase)));
        var verbRatio = (decimal)hasVerbs / criteria.Count;
        var confidence = verbRatio >= 0.5m ? 0.6m : 0.4m;

        if (effectiveMaxGrade == 0)
        {
            confidence = Math.Min(confidence, 0.3m);
            warnings.Add("Nota máxima não informada; pontuação por critério indisponível.");
        }

        return Task.FromResult(new CriteriaGenerationResult(
            Source: "model_generated_from_activity_context",
            MaxPoints: effectiveMaxGrade,
            Confidence: confidence,
            Criteria: criteria,
            Warnings: warnings,
            PrivateNotesToTeacher: "Critérios gerados a partir do contexto da atividade porque não havia rubrica formal estruturada."));
    }

    /// <summary>
    /// Extrai critérios a partir do bloco "Resultados esperados:" no texto.
    /// </summary>
    private static List<string> ExtractFromResultadosEsperados(string text)
    {
        var match = ResultadosEsperadosRegex().Match(text);
        if (!match.Success)
        {
            return [];
        }

        var afterHeader = match.Groups["content"].Value;
        return SplitCriteriaText(afterHeader);
    }

    /// <summary>
    /// Extrai critérios inline após "Critérios de avaliação:" separados por ; ou |
    /// </summary>
    private static List<string> ExtractFromCriteriosAvaliacao(string text)
    {
        var match = CriteriosAvaliacaoRegex().Match(text);
        if (!match.Success)
        {
            return [];
        }

        var afterHeader = match.Groups["content"].Value;
        return SplitCriteriaText(afterHeader);
    }

    /// <summary>
    /// Extrai frases que contêm verbos avaliáveis.
    /// </summary>
    private static List<string> ExtractFromEvaluableVerbs(string text)
    {
        var sentences = text
            .Split(['.', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length >= MinCriterionLength)
            .ToArray();

        var criteria = new List<string>();
        foreach (var sentence in sentences)
        {
            if (IsMetadataOnly(sentence))
            {
                continue;
            }

            var hasVerb = EvaluableVerbs.Any(v =>
                sentence.Contains(v, StringComparison.OrdinalIgnoreCase));
            if (hasVerb)
            {
                criteria.Add(sentence);
            }
        }

        return criteria;
    }

    private static List<string> SplitCriteriaText(string text)
    {
        // Primeiro, tentar separar por ponto-e-vírgula ou barra vertical
        var parts = text
            .Split([';', '|'], StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length >= 5)
            .ToList();

        if (parts.Count >= 2)
        {
            return parts;
        }

        // Senão, separar por quebra de linha ou marcadores de lista
        var lines = text
            .Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => CriteriaPrefixRegex().Replace(line.Trim(), string.Empty).Trim())
            .Where(line => line.Length >= 5)
            .ToList();

        if (lines.Count >= 2)
        {
            return lines;
        }

        // Tentar separar por período caso tenha múltiplas frases
        var sentences = text
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length >= MinCriterionLength)
            .ToList();

        return sentences.Count >= 2 ? sentences : (parts.Count > 0 ? parts : sentences);
    }

    /// <summary>
    /// Limpa um critério bruto: remove prefixos de lista, transforma
    /// "elaboração, em documento..." em "Elaborar, em documento..."
    /// </summary>
    private static string CleanCriterion(string raw)
    {
        var cleaned = CriteriaPrefixRegex().Replace(raw.Trim(), string.Empty).Trim();

        // Remover palavras bloqueadas do início
        foreach (var blocked in MetadataBlocklist)
        {
            if (cleaned.StartsWith(blocked, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[blocked.Length..].TrimStart(' ', '-', ',', ':');
            }
        }

        // Transformar substantivação em verbo: "elaboração" → "Elaborar"
        cleaned = NominalizationToVerbRegex().Replace(cleaned, match =>
        {
            var nominalization = match.Value.ToLowerInvariant();
            var verb = NominalizationToVerb(nominalization);
            return verb ?? match.Value;
        });

        // Capitalizar primeira letra
        if (cleaned.Length > 0 && char.IsLower(cleaned[0]))
        {
            cleaned = char.ToUpper(cleaned[0], CultureInfo.GetCultureInfo("pt-BR")) + cleaned[1..];
        }

        // Remover ponto final redundante
        cleaned = cleaned.TrimEnd('.');

        return cleaned;
    }

    /// <summary>
    /// Verifica se o texto é formado apenas por metadados e não constitui um critério válido.
    /// </summary>
    private static bool IsMetadataOnly(string text)
    {
        var lower = text.ToLowerInvariant();

        // Se a maioria das palavras são metadados bloqueados, é metadado
        var words = lower.Split([' ', ',', ';', '-', ':'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0)
        {
            return true;
        }

        var metadataWordCount = 0;
        foreach (var word in words)
        {
            foreach (var blocked in MetadataBlocklist)
            {
                if (blocked.Contains(' '))
                {
                    if (lower.Contains(blocked))
                    {
                        metadataWordCount += blocked.Split(' ').Length;
                    }
                }
                else if (word == blocked)
                {
                    metadataWordCount++;
                }
            }
        }

        // Se mais de 60% das palavras são metadados, é metadado
        var metadataRatio = (double)metadataWordCount / words.Length;
        if (metadataRatio > 0.6)
        {
            return true;
        }

        // Sem nenhum verbo avaliável e curto: provavelmente metadado
        var hasVerb = EvaluableVerbs.Any(v => lower.Contains(v));
        if (!hasVerb && text.Length < 40)
        {
            // Verificar se tem algum conteúdo significativo
            var significantWords = words.Count(w => w.Length > 4 &&
                !MetadataBlocklist.Any(b => b.Equals(w, StringComparison.OrdinalIgnoreCase)));
            if (significantWords < 2)
            {
                return true;
            }
        }

        return false;
    }

    private static string CombineContextText(CriteriaGenerationRequest request)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.AssignmentDescription))
        {
            parts.Add(request.AssignmentDescription);
        }

        if (!string.IsNullOrWhiteSpace(request.ContextText))
        {
            parts.Add(request.ContextText);
        }

        if (!string.IsNullOrWhiteSpace(request.SupportingMaterials))
        {
            parts.Add(request.SupportingMaterials);
        }

        return string.Join("\n", parts);
    }

    private static string? NominalizationToVerb(string nominalization)
    {
        return nominalization switch
        {
            "elaboração" or "elaboracao" => "Elaborar",
            "indicação" or "indicacao" => "Indicar",
            "apresentação" or "apresentacao" => "Apresentar",
            "descrição" or "descricao" => "Descrever",
            "comparação" or "comparacao" => "Comparar",
            "justificação" or "justificacao" or "justificativa" => "Justificar",
            "aplicação" or "aplicacao" => "Aplicar",
            "adequação" or "adequacao" => "Adequar",
            "identificação" or "identificacao" => "Identificar",
            "análise" or "analise" => "Analisar",
            "relação" or "relacao" => "Relacionar",
            "proposição" or "proposicao" or "proposta" => "Propor",
            "planejamento" => "Planejar",
            "demonstração" or "demonstracao" => "Demonstrar",
            "avaliação" or "avaliacao" => "Avaliar",
            "explicação" or "explicacao" => "Explicar",
            "classificação" or "classificacao" => "Classificar",
            "definição" or "definicao" => "Definir",
            "organização" or "organizacao" => "Organizar",
            "formulação" or "formulacao" => "Formular",
            _ => null
        };
    }

    [GeneratedRegex(@"(?i)\b(?:resultado(?:s)?\s+esperado(?:s)?)\s*:\s*(?<content>.+)", RegexOptions.Singleline)]
    private static partial Regex ResultadosEsperadosRegex();

    [GeneratedRegex(@"(?i)\b(?:crit[eé]rio(?:s)?\s+(?:de\s+)?avalia[cç][aã]o)\s*:\s*(?<content>.+?)(?:\n\s*\n|Produto\s+esperado|Entrega|Prazo|$)", RegexOptions.Singleline)]
    private static partial Regex CriteriosAvaliacaoRegex();

    [GeneratedRegex(@"^\s*(?:[-*•]|\d+[\.)]|[a-zA-Z][\.)])\s*")]
    private static partial Regex CriteriaPrefixRegex();

    [GeneratedRegex(@"(?i)\b(?:elabora[cç][aã]o|indica[cç][aã]o|apresenta[cç][aã]o|descri[cç][aã]o|compara[cç][aã]o|justifica[cç][aã]o|justificativa|aplica[cç][aã]o|adequa[cç][aã]o|identifica[cç][aã]o|an[aá]lise|rela[cç][aã]o|proposi[cç][aã]o|proposta|planejamento|demonstra[cç][aã]o|avalia[cç][aã]o|explica[cç][aã]o|classifica[cç][aã]o|defini[cç][aã]o|organiza[cç][aã]o|formula[cç][aã]o)\b")]
    private static partial Regex NominalizationToVerbRegex();
}
