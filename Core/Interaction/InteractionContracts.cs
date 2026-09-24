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
