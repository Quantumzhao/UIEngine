namespace UIEngine.Core;

public interface IMemberDescriptor
{
    string Id { get; }

    string DisplayName { get; }
}

public interface IObjectDescriptor
{
    ObjectIdentity Identity { get; }

    DomainIdentity? DomainIdentity => null;

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

    bool IsNullable { get; }

    IReadOnlyList<SelectionOption> Options { get; }

    ValueRange? Range { get; }

    IReadOnlyList<ValidationRuleDescriptor> ValidationRules { get; }

    string? Unit { get; }

    IReadOnlyList<string> Tags { get; }

    ValueTask<InteractionResult<object?>> ReadAsync(CancellationToken cancellationToken = default);

    ValueTask<InteractionResult<object?>> WriteAsync(
        object? value,
        CancellationToken cancellationToken = default);
}

public interface IReferenceDescriptor : IMemberDescriptor
{
    Type ReferenceType { get; }

    ValueTask<InteractionResult<ObjectHandle?>> ReadAsync(
        CancellationToken cancellationToken = default);
}

public interface ICollectionDescriptor : IMemberDescriptor
{
    Type ElementType { get; }

    Type? KeyType { get; }

    CollectionCapabilities Capabilities { get; }

    ValueTask<InteractionResult<CollectionReadResult>> ReadAsync(
        CollectionReadRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Lets a collection provider implement stable keyed logical-path selection.</summary>
public interface ICollectionPathSelector
{
    ValueTask<InteractionResult<IReadOnlyList<ObjectHandle>>> SelectByKeyAsync(
        string key,
        CancellationToken cancellationToken = default);
}

public interface IActionDescriptor : IMemberDescriptor
{
    IReadOnlyList<IParameterDescriptor> Parameters { get; }

    Type? ResultType { get; }

    bool IsAsynchronous { get; }

    bool SupportsCancellation { get; }

    Type? ProgressType { get; }

    ActionRisk Risk { get; }

    bool RequiresConfirmation { get; }

    ValueTask<InteractionResult<IActionInvocation>> InvokeAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default);
}

public interface IParameterDescriptor : IMemberDescriptor
{
    Type ParameterType { get; }

    bool IsRequired { get; }

    bool IsNullable { get; }

    bool HasDefaultValue { get; }

    object? DefaultValue { get; }

    IReadOnlyList<SelectionOption> Options { get; }

    ValueRange? Range { get; }

    IReadOnlyList<ValidationRuleDescriptor> ValidationRules { get; }

    string? Unit { get; }

    IReadOnlyList<string> Tags { get; }
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
