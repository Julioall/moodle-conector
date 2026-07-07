using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using MoodleConnector.Application.Abstractions;
using MoodleConnector.Domain;

namespace MoodleConnector.Application.Memory;

public sealed record SaveUserMemoryRequest(
    string Category,
    string Key,
    string Content,
    string Origin,
    string? MoodleAlias = null,
    string? CourseId = null);

public sealed record ListUserMemoriesRequest(string? MoodleAlias = null, string? CourseId = null, int? Limit = null);

public sealed record UserMemoryDto(
    Guid Id,
    string OwnerSubject,
    string Category,
    string NormalizedKey,
    string Content,
    string Origin,
    string? MoodleAlias,
    string? CourseId,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record RemoveUserMemoryResult(bool Removed);

public interface IUserMemoryService
{
    Task<UserMemoryDto> SaveAsync(SaveUserMemoryRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UserMemoryDto>> ListAsync(ListUserMemoriesRequest request, CancellationToken cancellationToken = default);
    Task<RemoveUserMemoryResult> RemoveAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed partial class UserMemoryService(
    IUserMemoryRepository repository,
    ICurrentUserContext currentUser,
    TimeProvider timeProvider) : IUserMemoryService
{
    private static readonly HashSet<string> Categories = ["preferencia", "caminho", "correcao", "decisao"];
    private static readonly HashSet<string> Origins = ["explicit", "inferred"];

    public async Task<UserMemoryDto> SaveAsync(SaveUserMemoryRequest request, CancellationToken cancellationToken = default)
    {
        var owner = GetOwner();
        ArgumentNullException.ThrowIfNull(request);
        var category = Required(request.Category, nameof(request.Category)).ToLowerInvariant();
        var origin = Required(request.Origin, nameof(request.Origin)).ToLowerInvariant();
        var key = Required(request.Key, nameof(request.Key));
        var content = Required(request.Content, nameof(request.Content));
        var alias = Optional(request.MoodleAlias);
        var courseId = Optional(request.CourseId);

        if (!Categories.Contains(category)) throw new ArgumentException("Categoria de memória inválida.", nameof(request));
        if (!Origins.Contains(origin)) throw new ArgumentException("Origem de memória inválida.", nameof(request));
        EnsureLength(key, 120, nameof(request.Key));
        EnsureLength(content, 1000, nameof(request.Content));
        EnsureLength(alias, 64, nameof(request.MoodleAlias));
        EnsureLength(courseId, 64, nameof(request.CourseId));
        if (courseId is not null && alias is null) throw new ArgumentException("CourseId exige MoodleAlias.", nameof(request));
        if (ContainsSecret(content) || ContainsSecret(key)) throw new ArgumentException("Memórias não podem conter segredos.", nameof(request));

        var normalizedKey = NormalizeKey(key);
        if (normalizedKey.Length == 0) throw new ArgumentException("A chave deve conter caracteres alfanuméricos.", nameof(request));

        var now = timeProvider.GetUtcNow();
        var memory = await repository.FindEquivalentAsync(owner, category, alias, courseId, normalizedKey, cancellationToken);
        if (memory is null)
        {
            memory = new UserMemory(owner, category, normalizedKey, content, origin, alias, courseId, now);
            await repository.AddAsync(memory, cancellationToken);
        }
        else
        {
            memory.Update(content, origin, now);
        }

        await repository.SaveChangesAsync(cancellationToken);
        return Map(memory);
    }

    public async Task<IReadOnlyList<UserMemoryDto>> ListAsync(ListUserMemoriesRequest request, CancellationToken cancellationToken = default)
    {
        var owner = GetOwner();
        ArgumentNullException.ThrowIfNull(request);
        var alias = Optional(request.MoodleAlias);
        var courseId = Optional(request.CourseId);
        EnsureLength(alias, 64, nameof(request.MoodleAlias));
        EnsureLength(courseId, 64, nameof(request.CourseId));
        if (courseId is not null && alias is null) throw new ArgumentException("CourseId exige MoodleAlias.", nameof(request));
        var limit = Math.Clamp(request.Limit ?? 20, 1, 50);
        var memories = await repository.ListAsync(owner, alias, courseId, limit, cancellationToken);
        return memories.Select(Map).ToList();
    }

    public async Task<RemoveUserMemoryResult> RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var removed = await repository.RemoveAsync(id, GetOwner(), cancellationToken);
        if (removed) await repository.SaveChangesAsync(cancellationToken);
        return new RemoveUserMemoryResult(removed);
    }

    private string GetOwner() => string.IsNullOrWhiteSpace(currentUser.Subject)
        ? throw new InvalidOperationException("Usuário autenticado não identificado.")
        : currentUser.Subject.Trim();

    private static string Required(string? value, string parameterName) =>
        string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Valor obrigatório.", parameterName) : value.Trim();

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void EnsureLength(string? value, int maximum, string parameterName)
    {
        if (value?.Length > maximum) throw new ArgumentException($"O valor excede {maximum} caracteres.", parameterName);
    }

    private static string NormalizeKey(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var result = new StringBuilder(decomposed.Length);
        var separatorPending = false;
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(character))
            {
                if (separatorPending && result.Length > 0) result.Append('-');
                result.Append(char.ToLowerInvariant(character));
                separatorPending = false;
            }
            else
            {
                separatorPending = true;
            }
        }
        return result.ToString();
    }

    private static UserMemoryDto Map(UserMemory memory) => new(
        memory.Id, memory.OwnerSubject, memory.Category, memory.NormalizedKey, memory.Content, memory.Origin,
        memory.MoodleAlias, memory.CourseId, memory.CreatedAtUtc, memory.UpdatedAtUtc);

    private static bool ContainsSecret(string value) => SecretPattern().IsMatch(value) || ContainsJwt(value);

    private static bool ContainsJwt(string value)
    {
        foreach (Match match in JwtCandidatePattern().Matches(value))
        {
            try
            {
                var segment = match.Groups[1].Value.Replace('-', '+').Replace('_', '/');
                if (segment.Length % 4 == 1) continue;
                segment = segment.PadRight(segment.Length + ((4 - segment.Length % 4) % 4), '=');
                using var document = JsonDocument.Parse(Convert.FromBase64String(segment));
                if (document.RootElement.ValueKind == JsonValueKind.Object &&
                    (document.RootElement.TryGetProperty("alg", out _) || document.RootElement.TryGetProperty("typ", out _)))
                {
                    return true;
                }
            }
            catch (FormatException)
            {
                // Not Base64URL.
            }
            catch (JsonException)
            {
                // Not a JSON JWT header.
            }
        }

        return false;
    }

    [GeneratedRegex(@"(?ix)(password|senha|token|api\s*[-_]?\s*key|secret|cookie|bearer\s+|sk-)")]
    private static partial Regex SecretPattern();

    [GeneratedRegex(@"(?<![A-Za-z0-9_-])([A-Za-z0-9_-]+)\.([A-Za-z0-9_-]+)\.([A-Za-z0-9_-]*)(?![A-Za-z0-9_-])")]
    private static partial Regex JwtCandidatePattern();
}
