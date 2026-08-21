namespace MoodleConnector.Application.Abstractions;

public interface IMoodleCallTelemetry
{
    void RecordMoodleWebServiceCall(string? connectionAlias = null);

    void RecordMoodleWebServiceCall(string? connectionAlias, string? functionName) =>
        RecordMoodleWebServiceCall(connectionAlias);

    void RecordMoodleWebServiceCompleted(string? connectionAlias, string? functionName, double durationMs)
    {
    }

    void RecordMoodleWebServiceFailure(string? connectionAlias, string? functionName, string? errorCode, double durationMs)
    {
    }
}
