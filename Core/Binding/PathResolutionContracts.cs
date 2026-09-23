namespace UIEngine.Core;

public sealed record PathLocation(Guid Handle, LogicalPath Path);

public sealed record ResolvedPath(
    BaseNode Node,
    LogicalPath CanonicalPath,
    IReadOnlyList<PathLocation> Locations)
{
    public bool IsObject => Node is IObjectNode;
}
