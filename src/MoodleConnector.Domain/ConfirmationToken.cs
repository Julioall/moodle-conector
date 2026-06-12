namespace MoodleConnector.Domain;

public readonly record struct ConfirmationToken(string Value)
{
    public override string ToString() => Value;
}
