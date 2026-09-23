using System.Reflection;
using System.Runtime.InteropServices;

namespace UIEngine.Core;

internal abstract class LiveReflectedMemberNode : BaseNode, IDynamicInterfaceCastable
{
    private readonly MemberNodeSource _Source;

    private protected LiveReflectedMemberNode(
        UIEngineHost host,
        string id,
        ReflectedMember member,
        Type valueType)
        : base(host, id, valueType)
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

internal sealed class LiveReferenceNode(
    ReferenceNodeBinding binding,
    ReflectedMember member)
    : LiveReflectedMemberNode(binding.Host, binding.Id, member, binding.ReferenceType), IReferenceNode
{
    public Type ReferenceType => binding.ReferenceType;

    public InteractionResult<Guid?> ReadReference() => binding.Read();
}

internal sealed class LiveCollectionNode(
    CollectionNodeBinding binding,
    ReflectedMember member)
    : LiveReflectedMemberNode(binding.Host, binding.Id, member, binding.CollectionType), ICollectionNode
{
    public Type ElementType => binding.ElementType;

    public Type? KeyType => binding.KeyType;

    public InteractionResult<CollectionSlice> ReadEntries(long offset, int limit) =>
        binding.Read(offset, limit);

    internal InteractionResult<IReadOnlyList<Guid>> Select(
        CollectionSelector selector) => binding.Select(selector);
}

internal sealed class LiveMethodNode : LiveReflectedMemberNode, IMethodNode
{
    private readonly MethodNodeBinding _Binding;

    public LiveMethodNode(MethodNodeBinding binding, ReflectedMember member)
        : base(binding.Host, binding.Id, member, binding.ReturnType)
    {
        _Binding = binding;
    }

    public IReadOnlyList<MethodParameter> Parameters => _Binding.Parameters;

    public Type? ResultType => _Binding.ResultType;

    public bool IsAsynchronous => _Binding.IsAsynchronous;

    public Type? ProgressType => _Binding.ProgressType;

    public InteractionResult<ActionInvocation> Invoke(
        IReadOnlyDictionary<string, object?> arguments) => _Binding.Invoke(arguments);
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
