using MoodleConnector.Application.Abstractions;

namespace MoodleConnector.Infrastructure;

internal sealed class MoodleConnectionSelection : IMoodleConnectionSelection
{
    public string? Alias { get; set; }
}
