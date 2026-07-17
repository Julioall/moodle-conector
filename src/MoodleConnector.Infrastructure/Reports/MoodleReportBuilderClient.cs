using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Infrastructure.Reports;

public sealed record MoodleReportResult(DateTimeOffset UpdatedAt, IReadOnlyList<Dictionary<string, object?>> Rows);

public interface IMoodleReportBuilderClient
{
    Task<MoodleReportResult> DownloadAsync(int reportId, int pageSize, CancellationToken cancellationToken);
}

internal sealed partial class MoodleReportBuilderClient(
    IMoodleConnectorCredentialsProvider credentialsProvider) : IMoodleReportBuilderClient
{
    private static readonly HashSet<int> AllowedReportIds = [509, 512];

    public async Task<MoodleReportResult> DownloadAsync(
        int reportId,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (!AllowedReportIds.Contains(reportId))
            throw new ArgumentOutOfRangeException(nameof(reportId), "Somente os relatorios 509 e 512 sao permitidos.");

        pageSize = Math.Clamp(pageSize, 1, 50_000);
        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var baseUri = ValidateBaseUri(credentials.BaseUrl);

        using var handler = new HttpClientHandler
        {
            CookieContainer = new CookieContainer(),
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.All
        };
        using var client = new HttpClient(handler) { BaseAddress = baseUri, Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("MoodleConnector/1.0");

        var loginHtml = await GetTextAsync(client, "login/index.php", cancellationToken);
        var loginToken = ExtractRequired(LoginTokenRegex(), loginHtml, "logintoken");

        using var loginResponse = await client.PostAsync(
            "login/index.php",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["username"] = credentials.Username,
                ["password"] = credentials.Password,
                ["logintoken"] = loginToken
            }),
            cancellationToken);
        loginResponse.EnsureSuccessStatusCode();
        var postLoginHtml = await loginResponse.Content.ReadAsStringAsync(cancellationToken);
        if (LoginTokenRegex().IsMatch(postLoginHtml) || loginResponse.RequestMessage?.RequestUri?.AbsolutePath.Contains("/login/", StringComparison.OrdinalIgnoreCase) == true)
            throw new InvalidOperationException("O Moodle recusou o login usado para obter o relatorio.");

        var viewHtml = await GetTextAsync(client, $"reportbuilder/view.php?id={reportId}", cancellationToken);
        var sessKey = ExtractRequired(SessKeyRegex(), viewHtml, "sesskey");

        using var jsonResponse = await client.GetAsync(
            $"reportbuilder/download.php?sesskey={Uri.EscapeDataString(sessKey)}&download=json&id={reportId}",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        jsonResponse.EnsureSuccessStatusCode();

        var mediaType = jsonResponse.Content.Headers.ContentType?.MediaType;
        if (string.Equals(mediaType, "text/html", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O Moodle devolveu HTML em vez do JSON do relatorio. Verifique a permissao do usuario no Report Builder.");

        await using var stream = await jsonResponse.Content.ReadAsStreamAsync(cancellationToken);
        var rows = await ReadJsonAsync(stream, pageSize, cancellationToken);
        var saoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");
        return new MoodleReportResult(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, saoPaulo), rows);
    }

    internal static async Task<IReadOnlyList<Dictionary<string, object?>>> ReadJsonAsync(
        Stream stream,
        int limit,
        CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var rowsElement = document.RootElement;
        if (rowsElement.ValueKind == JsonValueKind.Object)
        {
            if (!TryGetRowsProperty(rowsElement, out rowsElement))
                throw new InvalidOperationException("O JSON devolvido pelo Moodle nao contem uma lista de dados reconhecida.");
        }

        // O exportador JSON do Report Builder pode devolver [[{...}, {...}]].
        if (rowsElement.ValueKind == JsonValueKind.Array &&
            rowsElement.GetArrayLength() == 1 &&
            rowsElement[0].ValueKind == JsonValueKind.Array)
            rowsElement = rowsElement[0];

        if (rowsElement.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("O JSON devolvido pelo Moodle nao e uma lista de registros.");

        var result = new List<Dictionary<string, object?>>();
        foreach (var item in rowsElement.EnumerateArray())
        {
            if (result.Count >= limit) break;
            if (item.ValueKind != JsonValueKind.Object) continue;

            var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            var usedHeaders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in item.EnumerateObject())
            {
                var header = MakeUniqueHeader(NormalizeHeader(property.Name), usedHeaders);
                row[header] = NormalizeValue(header, property.Value);
            }
            result.Add(row);
        }
        return result;
    }

    private static bool TryGetRowsProperty(JsonElement root, out JsonElement rows)
    {
        foreach (var name in new[] { "dados", "data", "rows", "records" })
        {
            if (root.TryGetProperty(name, out rows) && rows.ValueKind == JsonValueKind.Array)
                return true;
        }

        rows = default;
        return false;
    }

    private static async Task<string> GetTextAsync(HttpClient client, string uri, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private static Uri ValidateBaseUri(string value)
    {
        if (!Uri.TryCreate(value.TrimEnd('/') + "/", UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            throw new InvalidOperationException("A URL base do Moodle e invalida.");
        return uri;
    }

    private static string ExtractRequired(Regex regex, string html, string name)
    {
        var match = regex.Match(html);
        if (!match.Success) throw new InvalidOperationException($"Nao foi possivel localizar {name} na pagina do Moodle.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private static string NormalizeHeader(string value)
    {
        var compact = NonAlphaNumericRegex().Replace(RemoveDiacritics(value), string.Empty).ToLowerInvariant();
        if (KnownHeaders.TryGetValue(compact, out var knownHeader)) return knownHeader;

        var words = NonAlphaNumericRegex().Split(RemoveDiacritics(value)).Where(x => x.Length > 0).ToArray();
        if (words.Length == 0) return "campo";
        return words[0].ToLowerInvariant() + string.Concat(words.Skip(1).Select(x => char.ToUpperInvariant(x[0]) + x[1..].ToLowerInvariant()));
    }

    private static string RemoveDiacritics(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormD);
        return new string(normalized.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray())
            .Normalize(NormalizationForm.FormC);
    }

    private static string MakeUniqueHeader(string header, Dictionary<string, int> counts)
    {
        counts.TryGetValue(header, out var count);
        counts[header] = ++count;
        return count == 1 ? header : $"{header}{count}";
    }

    private static object? NormalizeValue(string header, JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;
        if (value.ValueKind == JsonValueKind.True) return true;
        if (value.ValueKind == JsonValueKind.False) return false;
        if (value.ValueKind == JsonValueKind.Number)
        {
            if (value.TryGetInt64(out var integer)) return integer;
            if (value.TryGetDecimal(out var number)) return number;
            return value.GetDouble();
        }
        if (value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            return JsonSerializer.Deserialize<object>(value.GetRawText());

        var trimmed = value.GetString()?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return IsNullableReportField(header) ? null : string.Empty;
        if (trimmed is "-" or "Não disponível" or "Nunca") return null;
        if ((header.StartsWith("data", StringComparison.OrdinalIgnoreCase) ||
             header.StartsWith("ultimoAcesso", StringComparison.OrdinalIgnoreCase)) && TryParseMoodleDate(trimmed, out var date))
            return date.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

        if (header.StartsWith("nota", StringComparison.OrdinalIgnoreCase) &&
            decimal.TryParse(trimmed, NumberStyles.Number, CultureInfo.GetCultureInfo("pt-BR"), out var number))
            return number;

        return trimmed;
    }

    private static bool IsNullableReportField(string header) =>
        header.StartsWith("data", StringComparison.OrdinalIgnoreCase) ||
        header.StartsWith("ultimoAcesso", StringComparison.OrdinalIgnoreCase) ||
        header.StartsWith("nota", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseMoodleDate(string value, out DateTime date)
    {
        var match = MoodleLongDateRegex().Match(value);
        if (match.Success && MonthNumbers.TryGetValue(match.Groups[2].Value, out var month))
        {
            date = new DateTime(
                int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture),
                month,
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[5].Value, CultureInfo.InvariantCulture),
                0);
            return true;
        }

        return DateTime.TryParse(
            value,
            CultureInfo.GetCultureInfo("pt-BR"),
            DateTimeStyles.AllowWhiteSpaces,
            out date);
    }

    private static readonly IReadOnlyDictionary<string, int> MonthNumbers =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["jan"] = 1, ["fev"] = 2, ["mar"] = 3, ["abr"] = 4,
            ["mai"] = 5, ["jun"] = 6, ["jul"] = 7, ["ago"] = 8,
            ["set"] = 9, ["out"] = 10, ["nov"] = 11, ["dez"] = 12
        };

    private static readonly IReadOnlyDictionary<string, string> KnownHeaders =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["aluno"] = "aluno",
            ["cpf"] = "cpf",
            ["email"] = "email",
            ["telefone1"] = "telefone1",
            ["telefone2"] = "telefone2",
            ["papel"] = "papel",
            ["iduc"] = "idUc",
            ["cursocaminho"] = "cursoCaminho",
            ["unidadecurricular"] = "unidadeCurricular",
            ["notafinal"] = "notaFinal",
            ["datadeinciouc"] = "dataInicioUc",
            ["datadeiniciouc"] = "dataInicioUc",
            ["datatrminouc"] = "dataTerminoUc",
            ["dataterminouc"] = "dataTerminoUc",
            ["ltimoacessouc"] = "ultimoAcessoUc",
            ["ultimoacessouc"] = "ultimoAcessoUc",
            ["ltimoacessomoodle"] = "ultimoAcessoMoodle",
            ["ultimoacessomoodle"] = "ultimoAcessoMoodle",
            ["statusuc"] = "statusUc",
            ["datamatricula"] = "dataMatricula",
            ["categoria"] = "categoria"
        };

    [GeneratedRegex("name=[\\\"']logintoken[\\\"'][^>]*value=[\\\"']([^\\\"']+)", RegexOptions.IgnoreCase)]
    private static partial Regex LoginTokenRegex();

    [GeneratedRegex("(?:sesskey=|[\\\"']sesskey[\\\"']\\s*:\\s*[\\\"'])([A-Za-z0-9]+)", RegexOptions.IgnoreCase)]
    private static partial Regex SessKeyRegex();

    [GeneratedRegex("[^A-Za-z0-9]+")]
    private static partial Regex NonAlphaNumericRegex();

    [GeneratedRegex("(?:^|,\\s*)(\\d{1,2})\\s+([a-zç]{3})\\.\\s+(\\d{4}),\\s+(\\d{2}):(\\d{2})$", RegexOptions.IgnoreCase)]
    private static partial Regex MoodleLongDateRegex();
}
