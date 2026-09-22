namespace UIEngine.Core;

public sealed record BindingReference(
    string Path,
    string MemberId,
    MemberKind ExpectedKind,
    string? DomainIdentity = null);

public sealed record PathLocation(Guid Handle, LogicalPath Path);

public sealed record ResolvedPath(
    Guid OwnerHandle,
    ObjectDescriptor OwnerDescriptor,
    MemberDescriptor? Member,
    LogicalPath CanonicalPath,
    IReadOnlyList<PathLocation> Locations)
{
    public bool IsObject => Member is null;
}

public sealed record ResolvedBinding(
    Guid OwnerHandle,
    ObjectDescriptor OwnerDescriptor,
    MemberDescriptor Member,
    LogicalPath CanonicalPath);
