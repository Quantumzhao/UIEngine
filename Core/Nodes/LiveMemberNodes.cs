using System.Reflection;
using System.Runtime.InteropServices;

namespace UIEngine.Core;

internal abstract class LiveReflectedMemberNode : BaseNode, IDynamicInterfaceCastable
{
    private readonly MemberNodeSource _Source;

    private protected LiveReflectedMemberNode(
        UIEngineHost host,
        string name,
        ReflectedMember member,
        Type valueType)
        : base(host, name, valueType)
    {
        DeclaringType = member.Member.DeclaringType!;
        _Source = member.Member switch
        {
            PropertyInfo => MemberNodeSource.PROPERTY,
            FieldInfo => MemberNodeSource.FIELD,
            MethodInfo => MemberNodeSource.METHOD,
            _ => throw new InvalidOperationException("Unknown reflected member type."),
        };
    }

    public Type DeclaringType { get; }

    RuntimeTypeHandle IDynamicInterfaceCastable.GetInterfaceImplementation(
        RuntimeTypeHandle interfaceType)
    {
        var type = Type.GetTypeFromHandle(interfaceType);
        if (type == typeof(IMemberNode))
        {
            return typeof(ILiveReflectedMemberNodeImplementation).TypeHandle;
        }

        if (type == typeof(IPropertyNode))
        {
            return typeof(ILiveReflectedPropertyNodeImplementation).TypeHandle;
        }

        if (type == typeof(IFieldNode))
        {
            return typeof(ILiveReflectedFieldNodeImplementation).TypeHandle;
        }

        throw new InvalidCastException($"The member node does not implement '{type}'.");
    }

    bool IDynamicInterfaceCastable.IsInterfaceImplemented(
        RuntimeTypeHandle interfaceType,
        bool throwIfNotImplemented)
    {
        var type = Type.GetTypeFromHandle(interfaceType);
        var implemented = type == typeof(IMemberNode) ||
            type == typeof(IPropertyNode) && _Source is MemberNodeSource.PROPERTY ||
            type == typeof(IFieldNode) && _Source is MemberNodeSource.FIELD;
        if (!implemented && throwIfNotImplemented)
        {
            throw new InvalidCastException($"The member node does not implement '{type}'.");
        }

        return implemented;
    }

    private enum MemberNodeSource
    {
        PROPERTY,
        FIELD,
        METHOD,
    }
}

internal sealed class LiveReferenceNode : LiveReflectedMemberNode, IReferenceNode
{
    private readonly ReferenceNodeBinding _Binding;
    private readonly ReflectedMember _Member;

    public LiveReferenceNode(ReferenceNodeBinding binding, ReflectedMember member)
        : base(binding.Host, binding.Name, member, binding.ReferenceType)
    {
        _Binding = binding;
        _Member = member;
    }

    public Type ReferenceType => _Binding.ReferenceType;

    public InteractionResult<Guid?> ReadReference() => _Binding.Read();

    internal InteractionResult<LiveResolvedReferenceNode> ResolveTarget()
    {
        var read = ReadReference();
        if (!read.IsSuccess)
        {
            return InteractionResult.Failure<LiveResolvedReferenceNode>(read.Error!);
        }

        if (read.Value is null)
        {
            return InteractionResult.Failure<LiveResolvedReferenceNode>(
                InteractionErrorCode.UNAVAILABLE,
                $"Reference '{Name}' is empty.");
        }

        var target = Host.CreateObjectNode(read.Value.Value, Name);
        return target.IsSuccess
            ? InteractionResult.Success(new LiveResolvedReferenceNode(
                _Binding,
                _Member,
                target.Value))
            : InteractionResult.Failure<LiveResolvedReferenceNode>(target.Error!);
    }
}

internal sealed class LiveResolvedReferenceNode(
    ReferenceNodeBinding binding,
    ReflectedMember member,
    LiveObjectNode target)
    : LiveReflectedMemberNode(binding.Host, binding.Name, member, target.ValueType),
        IReferenceNode,
        IObjectNode
{
    public Type ReferenceType => binding.ReferenceType;

    public InteractionResult<Guid?> ReadReference() => binding.Read();

    public Guid Handle => target.Handle;

    public string? Summary => target.Summary;

    public IReadOnlyList<BaseNode> Members => target.Members;
}

internal sealed class LiveCollectionNode(
    CollectionNodeBinding binding,
    ReflectedMember member)
    : LiveReflectedMemberNode(binding.Host, binding.Name, member, binding.CollectionType), ICollectionNode
{
    public Type ElementType => binding.ElementType;

    public Type? KeyType => binding.KeyType;

    public InteractionResult<CollectionSlice> ReadEntries(long offset, int limit) =>
        binding.Read(offset, limit);

    internal InteractionResult<IReadOnlyList<Guid>> Select(
        ListLogicalPathSegment segment) => binding.Select(segment);

    internal InteractionResult<IReadOnlyList<Guid>> Select(
        DictLogicalPathSegment segment) => binding.Select(segment);
}

[DynamicInterfaceCastableImplementation]
internal interface ILiveReflectedMemberNodeImplementation : IMemberNode
{
    Type IMemberNode.DeclaringType =>
        ((LiveReflectedMemberNode)(object)this).DeclaringType;
}

[DynamicInterfaceCastableImplementation]
internal interface ILiveReflectedPropertyNodeImplementation
    : IPropertyNode, ILiveReflectedMemberNodeImplementation;

[DynamicInterfaceCastableImplementation]
internal interface ILiveReflectedFieldNodeImplementation
    : IFieldNode, ILiveReflectedMemberNodeImplementation;
