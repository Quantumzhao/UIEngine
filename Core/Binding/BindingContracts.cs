namespace UIEngine.Core;

public enum DescriptorKind
{
    INSTANCE,
    VALUE,
    REFERENCE,
    COLLECTION,
    ACTION,
}

public enum BindingFallbackPolicy
{
    DOMAIN_IDENTITY_ONLY,
    PATH_ONLY,
    DOMAIN_IDENTITY_THEN_PATH,
}

public enum BindingResolutionState
{
    RESOLVED,
    TEMPORARILY_UNAVAILABLE,
    TYPE_MISMATCH,
    TARGET_MISSING,
    AMBIGUOUS,
    PERMISSION_DENIED,
    INVALID_PATH,
}

/// <summary>Stores a durable binding without process-local runtime handles.</summary>
public sealed record BindingReference(
    string? DomainIdentity,
    string? Path,
    string MemberId,
    DescriptorKind ExpectedDescriptorKind,
    string? ExpectedTypeName,
    BindingFallbackPolicy FallbackPolicy);

/// <summary>Records an object reached while resolving a canonical logical path.</summary>
public sealed record LogicalPathLocation(ObjectHandle Handle, LogicalPath Path);

/// <summary>Contains a transient object or member reached by logical-path resolution.</summary>
public sealed record LogicalPathTarget(
    DescriptorKind Kind,
    ObjectHandle OwnerHandle,
    IObjectDescriptor OwnerDescriptor,
    IMemberDescriptor? MemberDescriptor);

public sealed class LogicalPathResolution
{
    internal LogicalPathResolution(
        BindingResolutionState state,
        LogicalPath? canonicalPath,
        LogicalPathTarget? target,
        IReadOnlyList<LogicalPathLocation> locations,
        InteractionError? error)
    {
        State = state;
        CanonicalPath = canonicalPath;
        Target = target;
        Locations = locations;
        Error = error;
    }

    public BindingResolutionState State { get; }

    public bool IsResolved => State == BindingResolutionState.RESOLVED;

    public LogicalPath? CanonicalPath { get; }

    public LogicalPathTarget? Target { get; }

    public IReadOnlyList<LogicalPathLocation> Locations { get; }

    public InteractionError? Error { get; }
}

/// <summary>Contains one transient live member selected by a durable binding.</summary>
public sealed record ResolvedBindingTarget(
    ObjectHandle OwnerHandle,
    IObjectDescriptor OwnerDescriptor,
    IMemberDescriptor MemberDescriptor,
    DescriptorKind Kind,
    string? TypeName);

public sealed class BindingResolution
{
    internal BindingResolution(
        BindingResolutionState state,
        ResolvedBindingTarget? target,
        LogicalPath? canonicalPath,
        InteractionError? error)
    {
        State = state;
        Target = target;
        CanonicalPath = canonicalPath;
        Error = error;
    }

    public BindingResolutionState State { get; }

    public bool IsResolved => State == BindingResolutionState.RESOLVED;

    public ResolvedBindingTarget? Target { get; }

    public LogicalPath? CanonicalPath { get; }

    public InteractionError? Error { get; }
}
