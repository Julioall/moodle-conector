using System.Net.Http.Headers;
using System.Text.Json;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Application.MoodleApi;

namespace MoodleConnector.Infrastructure.MoodleApi;

internal sealed class MoodleDraftUploadGateway(
    HttpClient httpClient,
    IMoodleAccessTokenProvider tokenProvider) : IMoodleDraftUploadGateway
{
    public async Task<MoodleDraftUploadResult> UploadAsync(
        MoodleConnectorCredentials connection,
        MoodleDraftUploadRequest request,
        CancellationToken cancellationToken)
    {
        if (request.Content is null)
        {
            throw new ArgumentNullException(nameof(request.Content));
        }

        if (!Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out var baseUri) || !IsAllowedBaseUri(baseUri))
        {
            throw new MoodleApiException(
                MoodleErrorContract.NetworkError,
                "A conexao Moodle possui uma URL invalida para upload.",
                connectionId: connection.ConnectionId,
                connectionAlias: connection.Alias,
                stage: MoodleIntegrationStage.UrlValidation);
        }

        var token = await tokenProvider.GetAccessTokenAsync(connection, cancellationToken);
        var endpoint = new Uri(baseUri.ToString().TrimEnd('/') + "/webservice/upload.php");
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent(token), "token");
        multipart.Add(new StringContent(NormalizeFilePath(request.FilePath)), "filepath");
        if (request.ItemId is { } itemId)
        {
            multipart.Add(new StringContent(itemId.ToString(System.Globalization.CultureInfo.InvariantCulture)), "itemid");
        }

        var file = new ByteArrayContent(request.Content);
        file.Headers.ContentType = new MediaTypeHeaderValue(
            string.IsNullOrWhiteSpace(request.MimeType) ? "application/octet-stream" : request.MimeType);
        multipart.Add(file, "file", Path.GetFileName(request.Filename));

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = multipart
        };
        httpRequest.Options.Set(MoodleHttpRequestOptions.DisableAutomaticRetry, true);
        using var response = await httpClient.SendAsync(httpRequest, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        {
            tokenProvider.Invalidate(connection);
        }

        if (!response.IsSuccessStatusCode)
        {
            try
            {
                _ = MoodleResponseParser.Parse(body);
            }
            catch (MoodleApiException exception)
            {
                throw new MoodleApiException(
                    exception.ErrorCode,
                    "O Moodle recusou o upload do arquivo.",
                    (int)response.StatusCode,
                    exception,
                    connectionId: connection.ConnectionId,
                    connectionAlias: connection.Alias,
                    endpoint: endpoint.GetLeftPart(UriPartial.Path),
                    stage: MoodleIntegrationStage.MoodleRequest);
            }

            throw new MoodleApiException(
                MoodleErrorContract.ApiError,
                "O Moodle recusou o upload do arquivo.",
                (int)response.StatusCode,
                connectionId: connection.ConnectionId,
                connectionAlias: connection.Alias,
                endpoint: endpoint.GetLeftPart(UriPartial.Path),
                stage: MoodleIntegrationStage.MoodleRequest);
        }

        JsonElement payload;
        try
        {
            payload = MoodleResponseParser.Parse(body);
        }
        catch (MoodleApiException exception)
        {
            if (MoodleErrorContract.NormalizeCode(exception.ErrorCode) == MoodleErrorContract.AuthenticationFailed)
            {
                tokenProvider.Invalidate(connection);
            }

            throw new MoodleApiException(
                exception.ErrorCode,
                "O Moodle recusou o upload do arquivo.",
                (int)response.StatusCode,
                exception,
                connectionId: connection.ConnectionId,
                connectionAlias: connection.Alias,
                endpoint: endpoint.GetLeftPart(UriPartial.Path),
                remoteErrorCode: exception.RemoteErrorCode ?? exception.ErrorCode,
                stage: MoodleIntegrationStage.MoodleRequest);
        }

        var files = ParseFiles(payload, request);
        if (files.Count == 0)
        {
            throw new MoodleApiException(
                MoodleErrorContract.InvalidResponse,
                "O Moodle nao retornou o arquivo criado no upload.",
                (int)response.StatusCode,
                connectionId: connection.ConnectionId,
                connectionAlias: connection.Alias,
                endpoint: endpoint.GetLeftPart(UriPartial.Path),
                stage: MoodleIntegrationStage.MoodleRequest);
        }

        return new MoodleDraftUploadResult(payload, files);
    }

    private static IReadOnlyList<MoodleDraftUploadFile> ParseFiles(
        JsonElement payload,
        MoodleDraftUploadRequest request)
    {
        var elements = payload.ValueKind == JsonValueKind.Array
            ? payload.EnumerateArray().ToArray()
            : payload.ValueKind == JsonValueKind.Object ? [payload] : [];
        return elements
            .Where(element => element.ValueKind == JsonValueKind.Object)
            .Select(element => new MoodleDraftUploadFile(
                GetInt32(element, "itemid"),
                GetString(element, "filename") ?? Path.GetFileName(request.Filename),
                GetString(element, "filepath") ?? NormalizeFilePath(request.FilePath),
                GetString(element, "url"),
                GetInt64(element, "filesize") ?? request.Content.LongLength,
                GetString(element, "mimetype") ?? request.MimeType))
            .ToArray();
    }

    private static string NormalizeFilePath(string? value)
    {
        var path = string.IsNullOrWhiteSpace(value) ? "/" : value.Trim();
        return path.StartsWith('/') ? path : "/" + path;
    }

    private static bool IsAllowedBaseUri(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps ||
        uri.Scheme == Uri.UriSchemeHttp &&
        (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
         System.Net.IPAddress.TryParse(uri.Host, out var address) && System.Net.IPAddress.IsLoopback(address));

    private static string? GetString(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int? GetInt32(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.TryGetInt32(out var number) ? number : null;

    private static long? GetInt64(JsonElement element, string name) =>
        TryGetProperty(element, name, out var value) && value.TryGetInt64(out var number) ? number : null;

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
