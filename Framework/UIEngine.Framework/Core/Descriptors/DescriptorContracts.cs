namespace UIEngine.Core;

public interface IMemberDescriptor
{
    string Id { get; }

    string DisplayName { get; }
}

public interface IObjectDescriptor
{
    ObjectIdentity Identity { get; }

    string TypeName { get; }

    string DisplayName { get; }

    string? Summary { get; }

    IReadOnlyList<IValueDescriptor> Values { get; }

    IReadOnlyList<IReferenceDescriptor> References { get; }

    IReadOnlyList<ICollectionDescriptor> Collections { get; }

    IReadOnlyList<IActionDescriptor> Actions { get; }
}

public interface IValueDescriptor : IMemberDescriptor
{
    Type ValueType { get; }

    bool CanRead { get; }

    bool CanWrite { get; }

    ValueTask<InteractionResult<object?>> ReadAsync(CancellationToken cancellationToken = default);

    ValueTask<InteractionResult<object?>> WriteAsync(
        object? value,
        CancellationToken cancellationToken = default);
}

public interface IReferenceDescriptor : IMemberDescriptor
{
    Type ReferenceType { get; }

    ValueTask<InteractionResult<ObjectHandle>> ReadAsync(
        CancellationToken cancellationToken = default);
}

public interface ICollectionDescriptor : IMemberDescriptor
{
    Type ElementType { get; }

    ValueTask<InteractionResult<IReadOnlyList<ObjectHandle>>> SnapshotAsync(
        CancellationToken cancellationToken = default);
}

public interface IActionDescriptor : IMemberDescriptor
{
    IReadOnlyList<IParameterDescriptor> Parameters { get; }

    ValueTask<InteractionResult<object?>> InvokeAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default);
}

public interface IParameterDescriptor : IMemberDescriptor
{
    Type ParameterType { get; }

    bool IsRequired { get; }

    object? DefaultValue { get; }
}

public interface IObjectDescriptorProvider
{
    bool CanDescribe(Type objectType);

    ValueTask<InteractionResult<IObjectDescriptor>> DescribeAsync(
        UIEngineHost host,
        object instance,
        ObjectHandle handle,
        CancellationToken cancellationToken = default);
}
