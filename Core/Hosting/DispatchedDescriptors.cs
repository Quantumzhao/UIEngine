namespace UIEngine.Core;

/// <summary>Snapshots descriptor metadata and routes every live operation through one host.</summary>
internal sealed class DispatchedObjectDescriptor : IObjectDescriptor
{
    private DispatchedObjectDescriptor(
        UIEngineHost host,
        IObjectDescriptor descriptor,
        DomainIdentity? domainIdentity,
        IInteractionDispatchPolicy? policy)
    {
        Identity = descriptor.Identity;
        DomainIdentity = domainIdentity;
        TypeName = descriptor.TypeName;
        DisplayName = descriptor.DisplayName;
        Summary = descriptor.Summary;
        Values = descriptor.Values
            .Select(value => (IValueDescriptor)new DispatchedValueDescriptor(host, value, policy))
            .ToArray();
        References = descriptor.References
            .Select(reference => (IReferenceDescriptor)new DispatchedReferenceDescriptor(
                host,
                reference,
                policy))
            .ToArray();
        Collections = descriptor.Collections
            .Select(collection => collection is ICollectionPathSelector
                ? (ICollectionDescriptor)new DispatchedKeyedCollectionDescriptor(
                    host,
                    collection,
                    policy)
                : new DispatchedCollectionDescriptor(host, collection, policy))
            .ToArray();
        Actions = descriptor.Actions
            .Select(action => (IActionDescriptor)new DispatchedActionDescriptor(host, action, policy))
            .ToArray();
    }

    public ObjectIdentity Identity { get; }

    public DomainIdentity? DomainIdentity { get; }

    public string TypeName { get; }

    public string DisplayName { get; }

    public string? Summary { get; }

    public IReadOnlyList<IValueDescriptor> Values { get; }

    public IReadOnlyList<IReferenceDescriptor> References { get; }

    public IReadOnlyList<ICollectionDescriptor> Collections { get; }

    public IReadOnlyList<IActionDescriptor> Actions { get; }

    internal static ValueTask<InteractionResult<IObjectDescriptor>> CreateAsync(
        UIEngineHost host,
        IObjectDescriptor descriptor,
        DomainIdentity? domainIdentity,
        IInteractionDispatchPolicy? policy,
        CancellationToken cancellationToken) => host.ExecuteInteractionAsync(
            InteractionDispatchOperation.SUMMARY_READ,
            _CanExecuteDirectly(policy, InteractionDispatchOperation.SUMMARY_READ),
            () => InteractionResult.Success<IObjectDescriptor>(new DispatchedObjectDescriptor(
                host,
                descriptor,
                domainIdentity,
                policy)),
            cancellationToken);

    private static bool _CanExecuteDirectly(
        IInteractionDispatchPolicy? policy,
        InteractionDispatchOperation operation) => policy?.CanExecuteDirectly(operation) == true;

    private abstract class _DispatchedMemberDescriptor
    {
        protected _DispatchedMemberDescriptor(
            UIEngineHost host,
            IMemberDescriptor descriptor,
            IInteractionDispatchPolicy? policy)
        {
            Host = host;
            Policy = policy;
            Id = descriptor.Id;
            DisplayName = descriptor.DisplayName;
        }

        public string Id { get; }

        public string DisplayName { get; }

        protected UIEngineHost Host { get; }

        protected IInteractionDispatchPolicy? Policy { get; }

        protected bool CanExecuteDirectly(InteractionDispatchOperation operation) =>
            _CanExecuteDirectly(Policy, operation);
    }

    private sealed class DispatchedValueDescriptor : _DispatchedMemberDescriptor, IValueDescriptor
    {
        private readonly IValueDescriptor _Descriptor;

        public DispatchedValueDescriptor(
            UIEngineHost host,
            IValueDescriptor descriptor,
            IInteractionDispatchPolicy? policy)
            : base(host, descriptor, policy)
        {
            _Descriptor = descriptor;
            ValueType = descriptor.ValueType;
            CanRead = descriptor.CanRead;
            CanWrite = descriptor.CanWrite;
            IsNullable = descriptor.IsNullable;
            Options = descriptor.Options.ToArray();
            Range = descriptor.Range;
            ValidationRules = descriptor.ValidationRules.ToArray();
            Unit = descriptor.Unit;
            Tags = descriptor.Tags.ToArray();
        }

        public Type ValueType { get; }

        public bool CanRead { get; }

        public bool CanWrite { get; }

        public bool IsNullable { get; }

        public IReadOnlyList<SelectionOption> Options { get; }

        public ValueRange? Range { get; }

        public IReadOnlyList<ValidationRuleDescriptor> ValidationRules { get; }

        public string? Unit { get; }

        public IReadOnlyList<string> Tags { get; }

        public ValueTask<InteractionResult<object?>> ReadAsync(
            CancellationToken cancellationToken = default) => Host.ExecuteInteractionAsync(
                InteractionDispatchOperation.VALUE_READ,
                CanExecuteDirectly(InteractionDispatchOperation.VALUE_READ),
                () => _Descriptor.ReadAsync(cancellationToken),
                cancellationToken);

        public ValueTask<InteractionResult<object?>> WriteAsync(
            object? value,
            CancellationToken cancellationToken = default) => Host.ExecuteInteractionAsync(
                InteractionDispatchOperation.VALUE_WRITE,
                CanExecuteDirectly(InteractionDispatchOperation.VALUE_WRITE),
                () => _Descriptor.WriteAsync(value, cancellationToken),
                cancellationToken);
    }

    private sealed class DispatchedReferenceDescriptor :
        _DispatchedMemberDescriptor,
        IReferenceDescriptor
    {
        private readonly IReferenceDescriptor _Descriptor;

        public DispatchedReferenceDescriptor(
            UIEngineHost host,
            IReferenceDescriptor descriptor,
            IInteractionDispatchPolicy? policy)
            : base(host, descriptor, policy)
        {
            _Descriptor = descriptor;
            ReferenceType = descriptor.ReferenceType;
        }

        public Type ReferenceType { get; }

        public ValueTask<InteractionResult<ObjectHandle?>> ReadAsync(
            CancellationToken cancellationToken = default) => Host.ExecuteInteractionAsync(
                InteractionDispatchOperation.REFERENCE_READ,
                CanExecuteDirectly(InteractionDispatchOperation.REFERENCE_READ),
                () => _Descriptor.ReadAsync(cancellationToken),
                cancellationToken);
    }

    private class DispatchedCollectionDescriptor : _DispatchedMemberDescriptor, ICollectionDescriptor
    {
        private readonly ICollectionDescriptor _Descriptor;

        public DispatchedCollectionDescriptor(
            UIEngineHost host,
            ICollectionDescriptor descriptor,
            IInteractionDispatchPolicy? policy)
            : base(host, descriptor, policy)
        {
            _Descriptor = descriptor;
            ElementType = descriptor.ElementType;
        }

        public Type ElementType { get; }

        public ValueTask<InteractionResult<IReadOnlyList<ObjectHandle>>> SnapshotAsync(
            CancellationToken cancellationToken = default) => Host.ExecuteInteractionAsync(
                InteractionDispatchOperation.COLLECTION_READ,
                CanExecuteDirectly(InteractionDispatchOperation.COLLECTION_READ),
                () => _Descriptor.SnapshotAsync(cancellationToken),
                cancellationToken);
    }

    private sealed class DispatchedKeyedCollectionDescriptor :
        DispatchedCollectionDescriptor,
        ICollectionPathSelector
    {
        private readonly ICollectionPathSelector _Selector;

        public DispatchedKeyedCollectionDescriptor(
            UIEngineHost host,
            ICollectionDescriptor descriptor,
            IInteractionDispatchPolicy? policy)
            : base(host, descriptor, policy)
        {
            _Selector = (ICollectionPathSelector)descriptor;
        }

        public ValueTask<InteractionResult<IReadOnlyList<ObjectHandle>>> SelectByKeyAsync(
            string key,
            CancellationToken cancellationToken = default) => Host.ExecuteInteractionAsync(
                InteractionDispatchOperation.COLLECTION_READ,
                CanExecuteDirectly(InteractionDispatchOperation.COLLECTION_READ),
                () => _Selector.SelectByKeyAsync(key, cancellationToken),
                cancellationToken);
    }

    private sealed class DispatchedActionDescriptor : _DispatchedMemberDescriptor, IActionDescriptor
    {
        private readonly IActionDescriptor _Descriptor;

        public DispatchedActionDescriptor(
            UIEngineHost host,
            IActionDescriptor descriptor,
            IInteractionDispatchPolicy? policy)
            : base(host, descriptor, policy)
        {
            _Descriptor = descriptor;
            Parameters = descriptor.Parameters
                .Select(static parameter => (IParameterDescriptor)new _ParameterDescriptor(parameter))
                .ToArray();
            Risk = descriptor.Risk;
            RequiresConfirmation = descriptor.RequiresConfirmation;
        }

        public IReadOnlyList<IParameterDescriptor> Parameters { get; }

        public ActionRisk Risk { get; }

        public bool RequiresConfirmation { get; }

        public ValueTask<InteractionResult<object?>> InvokeAsync(
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken cancellationToken = default) => Host.ExecuteInteractionAsync(
                InteractionDispatchOperation.ACTION_INVOKE,
                CanExecuteDirectly(InteractionDispatchOperation.ACTION_INVOKE),
                () => _Descriptor.InvokeAsync(arguments, cancellationToken),
                cancellationToken);
    }

    private sealed class _ParameterDescriptor : IParameterDescriptor
    {
        public _ParameterDescriptor(IParameterDescriptor descriptor)
        {
            Id = descriptor.Id;
            DisplayName = descriptor.DisplayName;
            ParameterType = descriptor.ParameterType;
            IsRequired = descriptor.IsRequired;
            IsNullable = descriptor.IsNullable;
            HasDefaultValue = descriptor.HasDefaultValue;
            DefaultValue = descriptor.DefaultValue;
            Options = descriptor.Options.ToArray();
            Range = descriptor.Range;
            ValidationRules = descriptor.ValidationRules.ToArray();
            Unit = descriptor.Unit;
            Tags = descriptor.Tags.ToArray();
        }

        public string Id { get; }

        public string DisplayName { get; }

        public Type ParameterType { get; }

        public bool IsRequired { get; }

        public bool IsNullable { get; }

        public bool HasDefaultValue { get; }

        public object? DefaultValue { get; }

        public IReadOnlyList<SelectionOption> Options { get; }

        public ValueRange? Range { get; }

        public IReadOnlyList<ValidationRuleDescriptor> ValidationRules { get; }

        public string? Unit { get; }

        public IReadOnlyList<string> Tags { get; }
    }
}
