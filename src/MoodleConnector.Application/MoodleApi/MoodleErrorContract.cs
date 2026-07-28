using System.Security.Cryptography;
using System.Text.Json;

namespace MoodleConnector.Application.MoodleApi;

public sealed record MoodleErrorDescriptor(
    string ErrorCode,
    string Message,
    string AuditId,
    int? HttpStatusCode = null);

public static class MoodleErrorContract
{
    public const string ConnectionNotFound = "moodle_connection_not_found";
    public const string DefaultConnectionNotConfigured = "moodle_default_connection_not_configured";
    public const string ConnectionDisabled = "moodle_connection_disabled";
    public const string TokenMissing = "moodle_token_missing";
    public const string TokenDecryptionFailed = "moodle_token_decryption_failed";
    public const string AuthenticationFailed = "moodle_authentication_failed";
    public const string FunctionNotAllowed = "moodle_function_not_allowed";
    public const string PermissionDenied = "moodle_permission_denied";
    public const string RequestTimeout = "moodle_request_timeout";
    public const string NetworkError = "moodle_network_error";
    public const string InvalidResponse = "moodle_invalid_response";
    public const string ApiError = "moodle_api_error";
    public const string CourseNotFound = "moodle_course_not_found";
    public const string Unexpected = "unexpected_connector_error";

    public static MoodleErrorDescriptor Describe(Exception exception)
    {
        if (exception is MoodleApiException moodle)
        {
            var stableCode = NormalizeCode(moodle.ErrorCode);
            return new MoodleErrorDescriptor(stableCode, SafeMessage(stableCode), moodle.AuditId, moodle.HttpStatusCode);
        }

        var code = exception switch
        {
            OperationCanceledException => RequestTimeout,
            HttpRequestException => NetworkError,
            CryptographicException or FormatException => TokenDecryptionFailed,
            JsonException => InvalidResponse,
            _ => Unexpected
        };

        return new MoodleErrorDescriptor(code, SafeMessage(code), Guid.NewGuid().ToString("N"));
    }

    public static string NormalizeCode(string? errorCode)
    {
        var code = errorCode?.Trim().ToLowerInvariant().Replace('-', '_');
        return code switch
        {
            ConnectionNotFound => ConnectionNotFound,
            DefaultConnectionNotConfigured => DefaultConnectionNotConfigured,
            ConnectionDisabled => ConnectionDisabled,
            TokenMissing or "moodle_credentials_missing" => TokenMissing,
            TokenDecryptionFailed or "moodle_credentials_decryption_failed" => TokenDecryptionFailed,
            AuthenticationFailed or "invalid_token" or "invalidtoken" or "invalid_login" or "invalidlogin" => AuthenticationFailed,
            FunctionNotAllowed or "function_not_available" or "function_not_discovered" or
                "function_not_read_safe" or "function_not_allowed" or "webservice_function_not_allowed" or
                "flow_unavailable" => FunctionNotAllowed,
            PermissionDenied or "access_exception" or "accessexception" or "webservice_access_exception" or
                "nopermissions" or "not_enrolled" => PermissionDenied,
            RequestTimeout or "timeout" or "moodle_timeout" => RequestTimeout,
            NetworkError or "moodle_unavailable" => NetworkError,
            InvalidResponse or "moodle_empty_response" => InvalidResponse,
            CourseNotFound or "invalidcourseid" or "course_not_found" => CourseNotFound,
            ApiError or "moodle_error" or "invalidparameter" or "invalid_parameter" => ApiError,
            Unexpected => Unexpected,
            _ => ApiError
        };
    }

    public static string SafeMessage(string errorCode) => NormalizeCode(errorCode) switch
    {
        ConnectionNotFound => "A conexao Moodle solicitada nao foi encontrada para esta conta.",
        DefaultConnectionNotConfigured => "Nenhuma conexao Moodle padrao esta configurada para esta conta.",
        ConnectionDisabled => "A conexao Moodle solicitada esta desativada.",
        TokenMissing => "A conexao Moodle nao possui credenciais suficientes para obter um token.",
        TokenDecryptionFailed => "As credenciais da conexao Moodle nao puderam ser descriptografadas.",
        AuthenticationFailed => "O Moodle recusou as credenciais da conexao selecionada.",
        FunctionNotAllowed => "A funcao necessaria nao esta autorizada nesta conexao Moodle.",
        PermissionDenied => "O usuario autenticado nao possui permissao para esta leitura no Moodle.",
        RequestTimeout => "O Moodle nao respondeu dentro do tempo limite.",
        NetworkError => "Nao foi possivel estabelecer comunicacao com o Moodle.",
        InvalidResponse => "O Moodle retornou uma resposta invalida.",
        CourseNotFound => "O curso nao foi encontrado ou nao esta acessivel para o usuario autenticado.",
        ApiError => "O Moodle recusou ou nao conseguiu concluir a chamada solicitada.",
        _ => "O conector encontrou um erro inesperado ao consultar o Moodle."
    };
}
