namespace MoodleConnector.Application.Abstractions;

public interface IMoodleCallTelemetry
{
    void RecordMoodleWebServiceCall(string? connectionAlias = null);
}
