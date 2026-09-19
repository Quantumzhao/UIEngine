namespace UIEngine.Core;

public sealed class InteractionResult<T>
{
    private readonly T? _Value;

    internal InteractionResult(T value)
    {
        IsSuccess = true;
        _Value = value;
    }

    internal InteractionResult(InteractionError error)
    {
        Error = error;
    }

    public bool IsSuccess { get; }

    public T Value => IsSuccess
        ? _Value!
        : throw new InvalidOperationException("A failed interaction result has no value.");

    public InteractionError? Error { get; }
}

public static class InteractionResult
{
    public static InteractionResult<T> Success<T>(T value) => new(value);

    public static InteractionResult<T> Failure<T>(InteractionErrorCode code, string message)
        => Failure<T>(code, message, []);

    public static InteractionResult<T> Failure<T>(
        InteractionErrorCode code,
        string message,
        IReadOnlyList<InteractionIssue> issues)
    {
        if (code == InteractionErrorCode.NONE)
        {
            throw new ArgumentOutOfRangeException(nameof(code), code, "A failure requires an error code.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        ArgumentNullException.ThrowIfNull(issues);
        return new InteractionResult<T>(new InteractionError(code, message, issues));
    }
}
