namespace UIEngine.Core;

/// <summary>Identifies where a structured interaction issue occurred.</summary>
public enum InteractionIssueTarget
{
    VALUE,
    PARAMETER,
    ACTION,
    PERMISSION,
}

/// <summary>Provides stable categories for expected validation and rejection details.</summary>
public enum InteractionIssueCode
{
    NULL_NOT_ALLOWED,
    REQUIRED,
    OUT_OF_RANGE,
    NOT_IN_SELECTION,
    RULE_FAILED,
    READ_ONLY,
    CONVERSION_FAILED,
    ACTION_REJECTED,
    PERMISSION_DENIED,
}

/// <summary>Describes one contextual issue without requiring a frontend to parse its message.</summary>
public sealed record InteractionIssue(
    InteractionIssueCode Code,
    InteractionIssueTarget Target,
    string TargetId,
    string Message);

/// <summary>Identifies validation metadata that a frontend can present before an operation.</summary>
public enum ValidationRuleKind
{
    REQUIRED,
    RANGE,
    FINITE_SELECTION,
    PROGRAMMATIC,
}

public sealed record ValidationRuleDescriptor(ValidationRuleKind Kind, string? Message = null);

public sealed record SelectionOption(object? Value, string DisplayName);

public sealed record ValueRange(object Minimum, object Maximum);

/// <summary>Provides presentation and confirmation hints without granting authorization.</summary>
public enum ActionRisk
{
    SAFE,
    MUTATING,
    DESTRUCTIVE,
    IRREVERSIBLE,
}
