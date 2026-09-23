using System.Reflection;
using System.Runtime.InteropServices;

namespace UIEngine.Core;

// Facets vary per occurrence. Dynamic interface casting keeps one concrete node type while
// preserving normal C# interface pattern matching and avoiding a class for every combination.
internal sealed class LiveValueNode : BaseNode, IDynamicInterfaceCastable
{
    private readonly ValueNodeSource _Source;
    private readonly ValueNodeShape _Shape;

    internal LiveValueNode(ValueNodeBinding binding, ReflectedMember member)
        : this(
            binding,
            member.Member switch
            {
                PropertyInfo => ValueNodeSource.PROPERTY,
                FieldInfo => ValueNodeSource.FIELD,
                _ => throw new InvalidOperationException(
                    "A reflected value node requires a property or field."),
            },
            member.Member.DeclaringType)
    {
    }

    internal LiveValueNode(ValueNodeBinding binding)
        : this(binding, ValueNodeSource.PROGRAMMATIC, declaringType: null)
    {
    }

    private LiveValueNode(
        ValueNodeBinding binding,
        ValueNodeSource source,
        Type? declaringType)
        : base(binding.Host, binding.Id, binding.ValueType)
    {
        Binding = binding;
        DeclaringType = declaringType;
        _Source = source;
        _Shape = _GetShape(binding.ValueType);
    }

    internal ValueNodeBinding Binding { get; }

    internal Type? DeclaringType { get; }

    RuntimeTypeHandle IDynamicInterfaceCastable.GetInterfaceImplementation(
        RuntimeTypeHandle interfaceType)
    {
        var type = Type.GetTypeFromHandle(interfaceType);
        if (type == typeof(IMemberNode))
        {
            return typeof(ILiveMemberNodeImplementation).TypeHandle;
        }

        if (type == typeof(IPropertyNode))
        {
            return typeof(ILivePropertyNodeImplementation).TypeHandle;
        }

        if (type == typeof(IFieldNode))
        {
            return typeof(ILiveFieldNodeImplementation).TypeHandle;
        }

        if (type == typeof(IProgrammaticValueNode))
        {
            return typeof(ILiveProgrammaticValueNodeImplementation).TypeHandle;
        }

        if (type == typeof(IReadableValueNode))
        {
            return typeof(ILiveReadableValueNodeImplementation).TypeHandle;
        }

        if (type == typeof(IWritableValueNode))
        {
            return typeof(ILiveWritableValueNodeImplementation).TypeHandle;
        }

        if (type == typeof(INullableValueNode))
        {
            return typeof(ILiveNullableValueNodeImplementation).TypeHandle;
        }

        if (type == typeof(IStringNode))
        {
            return typeof(ILiveStringNodeImplementation).TypeHandle;
        }

        if (type == typeof(ICharacterNode))
        {
            return typeof(ILiveCharacterNodeImplementation).TypeHandle;
        }

        if (type == typeof(IBooleanNode))
        {
            return typeof(ILiveBooleanNodeImplementation).TypeHandle;
        }

        if (type == typeof(INumberNode))
        {
            return typeof(ILiveNumberNodeImplementation).TypeHandle;
        }

        if (type == typeof(IEnumNode))
        {
            return typeof(ILiveEnumNodeImplementation).TypeHandle;
        }

        throw new InvalidCastException($"The value node does not implement '{type}'.");
    }

    bool IDynamicInterfaceCastable.IsInterfaceImplemented(
        RuntimeTypeHandle interfaceType,
        bool throwIfNotImplemented)
    {
        var type = Type.GetTypeFromHandle(interfaceType);
        var implemented =
            type == typeof(IMemberNode) && _Source is not ValueNodeSource.PROGRAMMATIC ||
            type == typeof(IPropertyNode) && _Source is ValueNodeSource.PROPERTY ||
            type == typeof(IFieldNode) && _Source is ValueNodeSource.FIELD ||
            type == typeof(IProgrammaticValueNode) && _Source is ValueNodeSource.PROGRAMMATIC ||
            type == typeof(IReadableValueNode) && Binding.CanRead ||
            type == typeof(IWritableValueNode) && Binding.CanWrite ||
            type == typeof(INullableValueNode) && Binding.IsNullable ||
            type == typeof(IStringNode) && _Shape is ValueNodeShape.STRING ||
            type == typeof(ICharacterNode) && _Shape is ValueNodeShape.CHARACTER ||
            type == typeof(IBooleanNode) && _Shape is ValueNodeShape.BOOLEAN ||
            type == typeof(INumberNode) && _Shape is ValueNodeShape.NUMBER ||
            type == typeof(IEnumNode) && _Shape is ValueNodeShape.ENUM;
        if (!implemented && throwIfNotImplemented)
        {
            throw new InvalidCastException($"The value node does not implement '{type}'.");
        }

        return implemented;
    }

    private static ValueNodeShape _GetShape(Type valueType)
    {
        var type = Nullable.GetUnderlyingType(valueType) ?? valueType;
        if (type == typeof(string))
        {
            return ValueNodeShape.STRING;
        }

        if (type == typeof(char))
        {
            return ValueNodeShape.CHARACTER;
        }

        if (type == typeof(bool))
        {
            return ValueNodeShape.BOOLEAN;
        }

        if (type.IsEnum)
        {
            return ValueNodeShape.ENUM;
        }

        return ValueConversion.IsNumeric(type) ? ValueNodeShape.NUMBER : ValueNodeShape.OTHER;
    }

    private enum ValueNodeSource
    {
        PROPERTY,
        FIELD,
        PROGRAMMATIC,
    }

    private enum ValueNodeShape
    {
        OTHER,
        STRING,
        CHARACTER,
        BOOLEAN,
        NUMBER,
        ENUM,
    }
}

[DynamicInterfaceCastableImplementation]
internal interface ILiveMemberNodeImplementation : IMemberNode
{
    Type IMemberNode.DeclaringType => ((LiveValueNode)(object)this).DeclaringType!;
}

[DynamicInterfaceCastableImplementation]
internal interface ILivePropertyNodeImplementation : IPropertyNode, ILiveMemberNodeImplementation;

[DynamicInterfaceCastableImplementation]
internal interface ILiveFieldNodeImplementation : IFieldNode, ILiveMemberNodeImplementation;

[DynamicInterfaceCastableImplementation]
internal interface ILiveProgrammaticValueNodeImplementation : IProgrammaticValueNode;

[DynamicInterfaceCastableImplementation]
internal interface ILiveReadableValueNodeImplementation : IReadableValueNode
{
    InteractionResult<object?> IReadableValueNode.ReadValue() =>
        ((LiveValueNode)(object)this).Binding.Read();
}

[DynamicInterfaceCastableImplementation]
internal interface ILiveWritableValueNodeImplementation : IWritableValueNode
{
    IReadOnlyList<SelectionOption> IWritableValueNode.Options =>
        ((LiveValueNode)(object)this).Binding.Options;

    IValueRange? IWritableValueNode.Range => ((LiveValueNode)(object)this).Binding.Range;

    InteractionResult<object?> IWritableValueNode.WriteValue(object? value) =>
        ((LiveValueNode)(object)this).Binding.Write(value);
}

[DynamicInterfaceCastableImplementation]
internal interface ILiveNullableValueNodeImplementation : INullableValueNode;

[DynamicInterfaceCastableImplementation]
internal interface ILiveStringNodeImplementation : IStringNode;

[DynamicInterfaceCastableImplementation]
internal interface ILiveCharacterNodeImplementation : ICharacterNode;

[DynamicInterfaceCastableImplementation]
internal interface ILiveBooleanNodeImplementation : IBooleanNode;

[DynamicInterfaceCastableImplementation]
internal interface ILiveNumberNodeImplementation : INumberNode;

[DynamicInterfaceCastableImplementation]
internal interface ILiveEnumNodeImplementation : IEnumNode
{
    IReadOnlyList<SelectionOption> IEnumNode.Options =>
        ((LiveValueNode)(object)this).Binding.Options;
}
