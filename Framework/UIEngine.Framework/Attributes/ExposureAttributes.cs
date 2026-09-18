namespace UIEngine.Attributes;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true)]
public sealed class ExposeAttribute : Attribute
{
    public bool ReadOnly { get; init; }
}

[AttributeUsage(AttributeTargets.Method, Inherited = true)]
public sealed class ActionAttribute : Attribute;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = true)]
public sealed class ChildrenAttribute : Attribute;

[AttributeUsage(
    AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Method,
    Inherited = true)]
public sealed class SummaryAttribute : Attribute;
