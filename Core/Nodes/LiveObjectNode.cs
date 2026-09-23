namespace UIEngine.Core;

internal sealed class LiveObjectNode : BaseNode, IObjectNode
{
    public LiveObjectNode(
        UIEngineHost host,
        string id,
        Type valueType,
        Guid handle,
        string? domainIdentity,
        string? summary,
        IReadOnlyList<BaseNode> members)
        : base(host, id, valueType)
    {
        Handle = handle;
        DomainIdentity = domainIdentity;
        Summary = summary;
        Members = members;
    }

    public Guid Handle { get; }

    public string? DomainIdentity { get; }

    public string? Summary { get; }

    public IReadOnlyList<BaseNode> Members { get; }
}
