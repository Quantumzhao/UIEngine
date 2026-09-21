namespace UIEngine.Core;

public sealed record BindingReference(
    string Path,
    string MemberId,
    MemberKind ExpectedKind,
    string? DomainIdentity = null);

public sealed record PathLocation(ObjectHandle Handle, LogicalPath Path);

public sealed record ResolvedPath(
    ObjectHandle OwnerHandle,
    ObjectDescriptor OwnerDescriptor,
    MemberDescriptor? Member,
    LogicalPath CanonicalPath,
    IReadOnlyList<PathLocation> Locations)
{
    public bool IsObject => Member is null;
}

public sealed record ResolvedBinding(
    ObjectHandle OwnerHandle,
    ObjectDescriptor OwnerDescriptor,
    MemberDescriptor Member,
    LogicalPath CanonicalPath);
