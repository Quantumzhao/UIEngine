namespace UIEngine.Core;

public sealed record InteractionError
{
    public InteractionError(InteractionErrorCode code, string message)
        : this(code, message, [])
    {
    }

    public InteractionError(
        InteractionErrorCode code,
        string message,
        IReadOnlyList<InteractionIssue> issues)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(issues);
        Code = code;
        Message = message;
        Issues = issues;
    }

    public InteractionErrorCode Code { get; }

    public string Message { get; }

    public IReadOnlyList<InteractionIssue> Issues { get; }
}
