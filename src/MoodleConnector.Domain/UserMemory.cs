namespace MoodleConnector.Domain;

public sealed class UserMemory
{
    public UserMemory(
        string ownerSubject,
        string category,
        string normalizedKey,
        string content,
        string origin,
        string? moodleAlias,
        string? courseId,
        DateTimeOffset createdAtUtc)
    {
        OwnerSubject = ownerSubject;
        Category = category;
        NormalizedKey = normalizedKey;
        Content = content;
        Origin = origin;
        MoodleAlias = moodleAlias;
        CourseId = courseId;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; init; } = Guid.NewGuid();
    public string OwnerSubject { get; init; }
    public string Category { get; init; }
    public string NormalizedKey { get; init; }
    public string Content { get; private set; }
    public string Origin { get; private set; }
    public string? MoodleAlias { get; init; }
    public string? CourseId { get; init; }
    public DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public void Update(string content, string origin, DateTimeOffset updatedAtUtc)
    {
        Content = content;
        Origin = origin;
        UpdatedAtUtc = updatedAtUtc;
    }
}
