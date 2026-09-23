namespace UIEngine.Core;

internal sealed class LiveObjectNode : BaseNode, IObjectNode
{
    public LiveObjectNode(
        UIEngineHost host,
        string name,
        Type valueType,
        Guid handle,
        string? summary,
        IReadOnlyList<BaseNode> members)
        : base(host, name, valueType)
    {
        Handle = handle;
        Summary = summary;
        Members = members;
    }

    public Guid Handle { get; }

    public string? Summary { get; }

    public IReadOnlyList<BaseNode> Members { get; }
}
