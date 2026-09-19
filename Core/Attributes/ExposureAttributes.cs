namespace UIEngine.Core.Attributes;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true)]
public sealed class ExposeAttribute : Attribute
{
    public bool ReadOnly { get; init; }
}

[AttributeUsage(AttributeTargets.Method, Inherited = true)]
public sealed class ActionAttribute : Attribute
{
    /// <summary>Names a parameterless Boolean instance method evaluated before invocation.</summary>
    public string? Precondition { get; init; }

    public ActionRisk Risk { get; init; } = ActionRisk.MUTATING;

    public bool RequiresConfirmation { get; init; }
}

[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter,
    Inherited = true)]
public sealed class InteractionMetadataAttribute : Attribute
{
    public string? Unit { get; init; }

    public string[] Tags { get; init; } = [];
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true)]
public sealed class ChildrenAttribute : Attribute;

[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method,
    Inherited = true)]
public sealed class SummaryAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true)]
public sealed class DomainIdentitySourceAttribute : Attribute;
