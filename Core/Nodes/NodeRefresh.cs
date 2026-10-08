namespace UIEngine.Core;

/// <summary>Identifies why a node occurrence requested a refresh.</summary>
public enum NodeRefreshKind
{
    PROPERTY_CHANGED,
    COLLECTION_CHANGED,
}

/// <summary>
/// Carries the identity of a node requesting refresh without copying its live value.
/// </summary>
public sealed class NodeRefreshRequestedEventArgs(
    BaseNode node,
    NodeRefreshKind kind) : EventArgs
{
    public BaseNode Node { get; } = node ?? throw new ArgumentNullException(nameof(node));

    public NodeRefreshKind Kind { get; } = kind;
}
