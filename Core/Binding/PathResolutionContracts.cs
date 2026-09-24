namespace UIEngine.Core;

/// <summary>
/// One fully resolved semantic path. The resolution chain contains fresh node occurrences from
/// the registered root through the current node, including a collection before its selected
/// element.
/// </summary>
public sealed class ResolvedPath
{
    internal ResolvedPath(IReadOnlyList<ResolvedNode> resolutionChain)
    {
        ResolutionChain = resolutionChain;
        var current = resolutionChain[^1];
        Node = current.Node;
        CanonicalPath = current.CanonicalPath;
    }

    public BaseNode Node { get; }

    public LogicalPath CanonicalPath { get; }

    public IReadOnlyList<ResolvedNode> ResolutionChain { get; }

    public bool IsObject => Node is IObjectNode;
}
