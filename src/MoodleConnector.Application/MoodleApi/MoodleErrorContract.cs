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
    public const string WriteScopeNotRegistered = "moodle_write_scope_not_registered";
    public const string PermissionDenied = "moodle_permission_denied";
    public const string RequestTimeout = "moodle_request_timeout";
    public const string NetworkError = "moodle_network_error";
    public const string InvalidResponse = "moodle_invalid_response";
    public const string ApiError = "moodle_api_error";
    public const string ScormNotFound = "scorm_not_found";
    public const string ScormSelectionRequired = "scorm_selection_required";
    public const string ScormPackageUnavailable = "scorm_package_unavailable";
    public const string ScormPackageTooLarge = "scorm_package_too_large";
    public const string ScormManifestMissing = "scorm_manifest_missing";
    public const string InvalidScormPackage = "invalid_scorm_package";
    public const string InvalidScormManifest = "invalid_scorm_manifest";
    public const string InvalidScormResponse = "invalid_scorm_response";
    public const string CourseNotFound = "moodle_course_not_found";
    public const string SnapshotUnavailable = "snapshot_unavailable";
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
            WriteScopeNotRegistered => WriteScopeNotRegistered,
            PermissionDenied or "access_exception" or "accessexception" or "webservice_access_exception" or
                "nopermissions" or "not_enrolled" => PermissionDenied,
            RequestTimeout or "timeout" or "moodle_timeout" => RequestTimeout,
            NetworkError or "moodle_unavailable" => NetworkError,
            InvalidResponse or "moodle_empty_response" => InvalidResponse,
            CourseNotFound or "invalidcourseid" or "course_not_found" => CourseNotFound,
            SnapshotUnavailable => SnapshotUnavailable,
            ApiError or "moodle_error" or "invalidparameter" or "invalid_parameter" => ApiError,
            ScormNotFound => ScormNotFound,
            ScormSelectionRequired => ScormSelectionRequired,
            ScormPackageUnavailable => ScormPackageUnavailable,
            ScormPackageTooLarge => ScormPackageTooLarge,
            ScormManifestMissing => ScormManifestMissing,
            InvalidScormPackage => InvalidScormPackage,
            InvalidScormManifest => InvalidScormManifest,
            InvalidScormResponse => InvalidScormResponse,
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
        WriteScopeNotRegistered => "A funcao Moodle nao possui um escopo de escrita explicitamente registrado.",
        PermissionDenied => "O usuario autenticado nao possui permissao para esta leitura no Moodle.",
        RequestTimeout => "O Moodle nao respondeu dentro do tempo limite.",
        NetworkError => "Nao foi possivel estabelecer comunicacao com o Moodle.",
        InvalidResponse => "O Moodle retornou uma resposta invalida.",
        CourseNotFound => "O curso nao foi encontrado ou nao esta acessivel para o usuario autenticado.",
        SnapshotUnavailable => "O snapshot solicitado ainda nao esta disponivel ou esta incompleto.",
        ApiError => "O Moodle recusou ou nao conseguiu concluir a chamada solicitada.",
        ScormNotFound => "Nenhum pacote SCORM correspondente foi encontrado no curso.",
        ScormSelectionRequired => "O curso possui mais de um pacote SCORM; informe o identificador do pacote.",
        ScormPackageUnavailable => "O Moodle nao forneceu um pacote SCORM baixavel para esta atividade.",
        ScormPackageTooLarge => "O pacote SCORM excede o limite configurado para leitura.",
        ScormManifestMissing => "O pacote SCORM nao contem imsmanifest.xml.",
        InvalidScormPackage => "O pacote baixado nao e um ZIP SCORM valido.",
        InvalidScormManifest => "O imsmanifest.xml nao pode ser lido com seguranca.",
        InvalidScormResponse => "O Moodle retornou uma resposta de SCORM invalida.",
        _ => "O conector encontrou um erro inesperado ao consultar o Moodle."
    };
}
