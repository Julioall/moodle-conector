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
    Failed = 7
}
