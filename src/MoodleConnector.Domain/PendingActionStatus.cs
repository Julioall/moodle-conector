namespace MoodleConnector.Domain;

public enum PendingActionStatus
{
    Draft = 0,
    PendingConfirmation = 1,
    Confirmed = 2,
    Executing = 3,
    Executed = 4,
    Expired = 5,
    Cancelled = 6,
    Failed = 7,
    /// <summary>
    /// The remote Moodle result cannot be determined. The action must not be
    /// submitted again without an explicit reconciliation workflow.
    /// </summary>
    ExecutionUnknown = 8
}
