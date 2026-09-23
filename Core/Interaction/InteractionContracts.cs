namespace UIEngine.Core;

/// <summary>Stable categories that frontends can handle without parsing error messages.</summary>
public enum InteractionErrorCode
{
    NONE = 0,
    INVALID_INPUT,
    NOT_FOUND,
    UNAVAILABLE,
    AMBIGUOUS,
    TYPE_MISMATCH,
    UNSUPPORTED,
    CONVERSION_FAILED,
    VALIDATION_FAILED,
    PERMISSION_DENIED,
    DISPOSED,
    FAULT,
}

public enum ValidationIssueCode
{
    NULL_NOT_ALLOWED,
    REQUIRED,
    OUT_OF_RANGE,
    NOT_IN_SELECTION,
    READ_ONLY,
    RULE_FAILED,
}

/// <summary>Names one invalid member or action parameter.</summary>
public sealed record ValidationIssue(
    ValidationIssueCode Code,
    string TargetName,
    string Message);

public sealed record InteractionError(
    InteractionErrorCode Code,
    string Message,
    IReadOnlyList<ValidationIssue> Issues)
{
    public InteractionError(InteractionErrorCode code, string message)
        : this(code, message, [])
    {
    }
}

/// <summary>Represents either a successful value or one structured interaction error.</summary>
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

    public static InteractionResult<T> Failure<T>(InteractionErrorCode code, string message) =>
        new(new InteractionError(code, message));

    public static InteractionResult<T> Failure<T>(
        InteractionErrorCode code,
        string message,
        IReadOnlyList<ValidationIssue> issues) =>
        new(new InteractionError(code, message, issues));

    internal static InteractionResult<T> Failure<T>(InteractionError error) => new(error);
}

public sealed record SelectionOption(object? Value, string DisplayName);

/// <summary>Untyped range metadata for runtime-discovered values.</summary>
public interface IValueRange
{
    object? Minimum { get; }

    object? Maximum { get; }
}

/// <summary>A closed range whose bounds have the same compile-time type.</summary>
public sealed record ValueRange<T>(T Minimum, T Maximum) : IValueRange
{
    object? IValueRange.Minimum => Minimum;

    object? IValueRange.Maximum => Maximum;
}
