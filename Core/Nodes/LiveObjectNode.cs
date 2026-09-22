namespace UIEngine.Core;

internal sealed class LiveObjectNode : BaseNode, IObjectNode
{
    public LiveObjectNode(
        UIEngineHost host,
        string id,
        Type valueType,
        ObjectDescriptor descriptor,
        IReadOnlySet<string> programmaticValueIds)
        : base(host, id, valueType)
    {
        Descriptor = descriptor;
        var reflected = ReflectionMetadata.Get(valueType).Members.ToDictionary(
            static member => member.Id,
            StringComparer.Ordinal);
        Members = descriptor.Members.Select(member => member switch
        {
            ValueDescriptor value when programmaticValueIds.Contains(value.Id) =>
                (BaseNode)new LiveValueNode(value),
            ValueDescriptor value => new LiveValueNode(value, reflected[value.Id]),
            ReferenceDescriptor reference =>
                new LiveReferenceNode(reference, reflected[reference.Id]),
            CollectionDescriptor collection =>
                new LiveCollectionNode(collection, reflected[collection.Id]),
            ActionDescriptor action => new LiveMethodNode(action, reflected[action.Id]),
            _ => throw new InvalidOperationException("Unknown member descriptor type."),
        }).ToArray();
    }

    public ObjectHandle Handle => Descriptor.Handle;

    public string? DomainIdentity => Descriptor.DomainIdentity;

    public string? Summary => Descriptor.Summary;

    public IReadOnlyList<BaseNode> Members { get; }

    internal ObjectDescriptor Descriptor { get; }
}
