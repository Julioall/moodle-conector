using System.Net.Mime;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleSubmissionFileGateway(
    HttpClient httpClient,
    IOptions<MoodleApiOptions> options,
    IMoodleAccessTokenProvider tokenProvider,
    IMoodleConnectorCredentialsProvider credentialsProvider) : IMoodleSubmissionFileGateway
{
    private readonly MoodleApiOptions _options = options.Value;

    public async Task<SubmissionFileDownloadResult> DownloadFileAsync(
        string userExternalId,
        string fileUrl,
        string filename,
        long maxBytes,
        CancellationToken cancellationToken)
    {
        if (_options.UseStubData)
        {
            return CreateStub(filename);
        }

        if (string.IsNullOrWhiteSpace(fileUrl))
        {
            throw new ArgumentException("A URL do arquivo e obrigatoria.", nameof(fileUrl));
        }

        var credentials = await credentialsProvider.GetCurrentCredentialsAsync(cancellationToken);
        var fileUri = ValidateFileUri(fileUrl, credentials.BaseUrl);
        var token = await ResolveReadTokenAsync(credentials, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, fileUri);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        response.EnsureSuccessStatusCode();

        var mimeType = DetectMimeType(response, filename);
        var declared = response.Content.Headers.ContentLength;

        if (declared.HasValue && declared.Value > maxBytes)
        {
            throw new InvalidOperationException(
                $"O arquivo '{filename}' excede o limite de {maxBytes / (1024 * 1024)} MB permitido para download.");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var readBuffer = new byte[81920];
        long totalRead = 0;
        var truncated = false;
        int bytesRead;

        while ((bytesRead = await stream.ReadAsync(readBuffer, cancellationToken)) > 0)
        {
            var remaining = maxBytes - totalRead;
            if (remaining <= 0)
            {
                truncated = true;
                break;
            }

            var toWrite = (int)Math.Min(bytesRead, remaining);
            buffer.Write(readBuffer, 0, toWrite);
            totalRead += toWrite;
        }

        var content = buffer.ToArray();
        var sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

        return new SubmissionFileDownloadResult(
            string.IsNullOrWhiteSpace(filename) ? "arquivo" : filename,
            mimeType,
            content.Length,
            sha256,
            content,
            truncated);
    }

    private async Task<string> ResolveReadTokenAsync(
        MoodleConnectorCredentials connection,
        CancellationToken cancellationToken)
    {
        if (_options.AllowServiceTokenForReadOnlyQueries &&
            !string.IsNullOrWhiteSpace(_options.ServiceToken) &&
            HasSameOrigin(_options.BaseUrl, connection.BaseUrl))
        {
            return _options.ServiceToken;
        }

        return await tokenProvider.GetAccessTokenAsync(connection, cancellationToken);
    }

    private static bool HasSameOrigin(string? configuredBaseUrl, string connectionBaseUrl)
    {
        return Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var configured) &&
               Uri.TryCreate(connectionBaseUrl, UriKind.Absolute, out var connection) &&
               string.Equals(configured.Scheme, connection.Scheme, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(configured.Host, connection.Host, StringComparison.OrdinalIgnoreCase) &&
               configured.Port == connection.Port;
    }

    private static Uri ValidateFileUri(string fileUrl, string moodleBaseUrl)
    {
        if (!Uri.TryCreate(fileUrl, UriKind.Absolute, out var fileUri) ||
            !Uri.TryCreate(moodleBaseUrl, UriKind.Absolute, out var moodleUri) ||
            fileUri.Scheme is not ("http" or "https") ||
            !string.Equals(fileUri.Host, moodleUri.Host, StringComparison.OrdinalIgnoreCase) ||
            fileUri.Port != moodleUri.Port)
        {
            throw new InvalidOperationException("A URL do arquivo deve pertencer ao Moodle selecionado e usar HTTP(S).");
        }

        var safeQuery = string.Join("&", fileUri.Query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(part =>
            {
                var separator = part.IndexOf('=');
                var name = separator < 0 ? part : part[..separator];
                return !string.Equals(name, "token", StringComparison.OrdinalIgnoreCase) &&
                       !string.Equals(name, "wstoken", StringComparison.OrdinalIgnoreCase);
            }));
        var builder = new UriBuilder(fileUri) { Query = safeQuery };
        return builder.Uri;
    }

    private static string DetectMimeType(HttpResponseMessage response, string filename)
    {
        var contentType = response.Content.Headers.ContentType?.MediaType;
        if (!string.IsNullOrWhiteSpace(contentType) &&
            !contentType.Equals("application/octet-stream", StringComparison.OrdinalIgnoreCase))
        {
            return contentType;
        }

        return filename?.ToLowerInvariant() switch
        {
            var f when f?.EndsWith(".pdf") == true => "application/pdf",
            var f when f?.EndsWith(".docx") == true => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            var f when f?.EndsWith(".doc") == true => "application/msword",
            var f when f?.EndsWith(".pptx") == true => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            var f when f?.EndsWith(".xlsx") == true => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            var f when f?.EndsWith(".txt") == true => "text/plain",
            var f when f?.EndsWith(".html") == true || f?.EndsWith(".htm") == true => "text/html",
            var f when f?.EndsWith(".odt") == true => "application/vnd.oasis.opendocument.text",
            var f when f?.EndsWith(".png") == true => "image/png",
            var f when f?.EndsWith(".jpg") == true || f?.EndsWith(".jpeg") == true => "image/jpeg",
            _ => "application/octet-stream"
        };
    }

    private static SubmissionFileDownloadResult CreateStub(string filename)
    {
        const string stubText = "Arquivo de submissao simulado para testes. Conteudo nao disponivel em modo stub.";
        var bytes = System.Text.Encoding.UTF8.GetBytes(stubText);
        var sha256 = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new SubmissionFileDownloadResult(
            filename ?? "stub.txt",
            "text/plain",
            bytes.Length,
            sha256,
            bytes,
            Truncated: false);
    }
}
