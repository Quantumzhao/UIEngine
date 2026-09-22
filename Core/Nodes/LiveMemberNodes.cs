using System.Reflection;
using System.Runtime.InteropServices;

namespace UIEngine.Core;

internal abstract class LiveReflectedMemberNode : BaseNode, IDynamicInterfaceCastable
{
    private readonly MemberNodeSource _Source;

    private protected LiveReflectedMemberNode(
        MemberDescriptor descriptor,
        ReflectedMember member,
        Type valueType)
        : base(descriptor.Host, descriptor.Id, valueType)
    {
        Descriptor = descriptor;
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

    internal MemberDescriptor Descriptor { get; }

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
    ReferenceDescriptor descriptor,
    ReflectedMember member)
    : LiveReflectedMemberNode(descriptor, member, descriptor.ReferenceType), IReferenceNode
{
    public Type ReferenceType => descriptor.ReferenceType;

    public Task<InteractionResult<ObjectHandle?>> ReadReferenceAsync() => descriptor.ReadAsync();
}

internal sealed class LiveCollectionNode(
    CollectionDescriptor descriptor,
    ReflectedMember member)
    : LiveReflectedMemberNode(descriptor, member, descriptor.CollectionType), ICollectionNode
{
    public Type ElementType => descriptor.ElementType;

    public Type? KeyType => descriptor.KeyType;

    public Task<InteractionResult<CollectionSlice>> ReadEntriesAsync(long offset, int limit) =>
        descriptor.ReadAsync(offset, limit);
}

internal sealed class LiveMethodNode : LiveReflectedMemberNode, IMethodNode
{
    private readonly ActionDescriptor _Descriptor;

    public LiveMethodNode(ActionDescriptor descriptor, ReflectedMember member)
        : base(descriptor, member, descriptor.ReturnType)
    {
        _Descriptor = descriptor;
        Parameters = descriptor.Parameters.Select(static parameter => new MethodParameter(
            parameter.Id,
            parameter.ParameterType,
            parameter.IsRequired,
            parameter.IsNullable,
            parameter.HasDefaultValue,
            parameter.DefaultValue,
            parameter.Options,
            parameter.Range)).ToArray();
    }

    public IReadOnlyList<MethodParameter> Parameters { get; }

    public Type? ResultType => _Descriptor.ResultType;

    public bool IsAsynchronous => _Descriptor.IsAsynchronous;

    public Type? ProgressType => _Descriptor.ProgressType;

    public Task<InteractionResult<ActionInvocation>> InvokeAsync(
        IReadOnlyDictionary<string, object?> arguments) => _Descriptor.InvokeAsync(arguments);
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
