using System.Reflection;
using UIEngine.Core.Reflection;

namespace UIEngine.Core.Exposure;

/// <summary>Composes host-scoped programmatic exposure over annotated reflection metadata.</summary>
internal sealed class ProgrammaticExposureDescriptorProvider
{
    private readonly ExposureRegistrySnapshot _Registry;
    private readonly ReflectionObjectDescriptorProvider? _ReflectionFallback;

    public ProgrammaticExposureDescriptorProvider(
        ExposureRegistrySnapshot registry,
        ReflectionObjectDescriptorProvider? reflectionFallback)
    {
        _Registry = registry;
        _ReflectionFallback = reflectionFallback;
    }

    public bool CanDescribe(Type objectType) =>
        _Registry.TryGet(objectType, out var registration) && registration?.HasDescriptorExposure == true;

    public async ValueTask<InteractionResult<IObjectDescriptor>> DescribeAsync(
        UIEngineHost host,
        object instance,
        ObjectHandle handle,
        CancellationToken cancellationToken)
    {
        if (!_Registry.TryGet(instance.GetType(), out var registration) ||
            registration?.HasDescriptorExposure != true)
        {
            return InteractionResult.Failure<IObjectDescriptor>(
                InteractionErrorCode.DESCRIPTOR_UNAVAILABLE,
                $"No programmatic exposure is registered for '{instance.GetType().FullName}'.");
        }

        IObjectDescriptor? fallbackDescriptor = null;
        if (registration.DescriptorFactory is not null)
        {
            var factoryResult = await registration.DescriptorFactory(
                host,
                instance,
                handle,
                cancellationToken).ConfigureAwait(false);
            if (!factoryResult.IsSuccess)
            {
                return factoryResult;
            }

            fallbackDescriptor = factoryResult.Value;
        }
        else if (_ReflectionFallback is not null && _ReflectionFallback.CanDescribe(instance.GetType()))
        {
            var reflectionResult = await _ReflectionFallback
                .DescribeAsync(host, instance, handle, cancellationToken)
                .ConfigureAwait(false);
            if (!reflectionResult.IsSuccess)
            {
                return reflectionResult;
            }

            fallbackDescriptor = reflectionResult.Value;
        }

        IObjectDescriptor descriptor = new ProgrammaticObjectDescriptor(
            host,
            instance,
            handle,
            registration,
            fallbackDescriptor);
        return InteractionResult.Success(descriptor);
    }
}

internal sealed class ProgrammaticObjectDescriptor : IObjectDescriptor
{
    public ProgrammaticObjectDescriptor(
        UIEngineHost host,
        object instance,
        ObjectHandle handle,
        ExposureTypeRegistration registration,
        IObjectDescriptor? reflectionDescriptor)
    {
        Identity = handle.Identity;
        TypeName = reflectionDescriptor?.TypeName ?? instance.GetType().FullName ?? instance.GetType().Name;
        DisplayName = reflectionDescriptor?.DisplayName ?? instance.GetType().Name;
        Summary = registration.GetSummary is null
            ? reflectionDescriptor?.Summary
            : registration.GetSummary(instance);

        var registeredIdentifiers = registration.Values.Select(static definition => definition.Id)
            .Concat(registration.References.Select(static definition => definition.Id))
            .Concat(registration.Collections.Select(static definition => definition.Id))
            .Concat(registration.Actions.Select(static definition => definition.Id))
            .ToHashSet(StringComparer.Ordinal);
        Values = registration.Values
            .Select(definition => (IValueDescriptor)new ProgrammaticValueDescriptor(
                host,
                instance,
                definition))
            .Concat(reflectionDescriptor?.Values.Where(value => !registeredIdentifiers.Contains(value.Id)) ?? [])
            .ToArray();
        References = registration.References
            .Select(definition => (IReferenceDescriptor)new ProgrammaticReferenceDescriptor(
                host,
                instance,
                definition))
            .Concat(_WithoutOverrides(reflectionDescriptor?.References, registeredIdentifiers))
            .ToArray();
        Collections = registration.Collections
            .Select(definition => (ICollectionDescriptor)new ProgrammaticCollectionDescriptor(
                host,
                instance,
                definition))
            .Concat(_WithoutOverrides(reflectionDescriptor?.Collections, registeredIdentifiers))
            .ToArray();
        Actions = registration.Actions
            .Select(definition => (IActionDescriptor)new ProgrammaticActionDescriptor(
                host,
                instance,
                definition))
            .Concat(_WithoutOverrides(reflectionDescriptor?.Actions, registeredIdentifiers))
            .ToArray();
    }

    public ObjectIdentity Identity { get; }

    public string TypeName { get; }

    public string DisplayName { get; }

    public string? Summary { get; }

    public IReadOnlyList<IValueDescriptor> Values { get; }

    public IReadOnlyList<IReferenceDescriptor> References { get; }

    public IReadOnlyList<ICollectionDescriptor> Collections { get; }

    public IReadOnlyList<IActionDescriptor> Actions { get; }

    private static TDescriptor[] _WithoutOverrides<TDescriptor>(
        IReadOnlyList<TDescriptor>? descriptors,
        HashSet<string> registeredIdentifiers)
        where TDescriptor : IMemberDescriptor => descriptors?
            .Where(descriptor => !registeredIdentifiers.Contains(descriptor.Id))
            .ToArray() ?? [];
}

internal abstract class ProgrammaticMemberDescriptor(
    UIEngineHost host,
    object target,
    string id,
    string displayName) : IMemberDescriptor
{
    private readonly WeakReference<object> _Target = new(target);

    public string Id { get; } = id;

    public string DisplayName { get; } = displayName;

    protected UIEngineHost Host { get; } = host;

    protected InteractionResult<T>? CheckAvailability<T>(string operation, CancellationToken cancellationToken)
    {
        if (Host.IsDisposed)
        {
            return InteractionResult.Failure<T>(
                InteractionErrorCode.HOST_DISPOSED,
                "The UIEngine host has been disposed.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return InteractionResult.Failure<T>(
                InteractionErrorCode.CANCELLED,
                $"The {operation} was cancelled.");
        }

        if (!_Target.TryGetTarget(out _))
        {
            return InteractionResult.Failure<T>(
                InteractionErrorCode.TARGET_UNAVAILABLE,
                "The containing object is no longer available.");
        }

        return null;
    }

    protected bool TryGetTarget(out object? target) => _Target.TryGetTarget(out target);
}

internal sealed class ProgrammaticReferenceDescriptor : ProgrammaticMemberDescriptor, IReferenceDescriptor
{
    private readonly ReferenceExposureDefinition _Definition;

    public ProgrammaticReferenceDescriptor(
        UIEngineHost host,
        object target,
        ReferenceExposureDefinition definition)
        : base(host, target, definition.Id, definition.GetDisplayName(target))
    {
        _Definition = definition;
    }

    public Type ReferenceType => _Definition.ReferenceType;

    public async ValueTask<InteractionResult<ObjectHandle?>> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        var unavailable = CheckAvailability<ObjectHandle?>("reference read", cancellationToken);
        if (unavailable is not null)
        {
            return unavailable;
        }

        TryGetTarget(out var target);
        try
        {
            var referenced = _Definition.Read(target!);
            if (referenced is null)
            {
                return InteractionResult.Success<ObjectHandle?>(null);
            }

            var encountered = await Host.EncounterAsync(referenced, cancellationToken).ConfigureAwait(false);
            return encountered.IsSuccess
                ? InteractionResult.Success<ObjectHandle?>(encountered.Value)
                : InteractionResult.Failure<ObjectHandle?>(
                    encountered.Error!.Code,
                    encountered.Error.Message,
                    encountered.Error.Issues);
        }
        catch (Exception exception)
        {
            return InteractionResult.Failure<ObjectHandle?>(
                InteractionErrorCode.INVOCATION_FAILED,
                exception.Message);
        }
    }
}

internal sealed class ProgrammaticCollectionDescriptor :
    ProgrammaticMemberDescriptor,
    ICollectionDescriptor,
    ICollectionPathSelector
{
    private readonly CollectionExposureDefinition _Definition;

    public ProgrammaticCollectionDescriptor(
        UIEngineHost host,
        object target,
        CollectionExposureDefinition definition)
        : base(host, target, definition.Id, definition.GetDisplayName(target))
    {
        _Definition = definition;
    }

    public Type ElementType => _Definition.ElementType;

    public Type? KeyType => _Definition.KeyType;

    public CollectionCapabilities Capabilities => _Definition.Capabilities;

    public async ValueTask<InteractionResult<CollectionReadResult>> ReadAsync(
        CollectionReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var unavailable = CheckAvailability<CollectionReadResult>("collection read", cancellationToken);
        if (unavailable is not null)
        {
            return unavailable;
        }

        var supported = request.Mode is CollectionAccessMode.SNAPSHOT or CollectionAccessMode.VIRTUALIZED_RANGE;
        if (!supported)
        {
            return InteractionResult.Failure<CollectionReadResult>(
                InteractionErrorCode.COLLECTION_ACCESS_UNSUPPORTED,
                $"Collection '{Id}' does not support {request.Mode}.");
        }

        TryGetTarget(out var target);
        try
        {
            var collection = _Definition.Read(target!);
            if (collection is null)
            {
                return InteractionResult.Failure<CollectionReadResult>(
                    InteractionErrorCode.TARGET_UNAVAILABLE,
                    $"Collection '{Id}' is null or unavailable.");
            }

            var count = _Definition.GetCount(collection);

            var offset = request.Mode == CollectionAccessMode.SNAPSHOT ? 0 : request.Offset;
            var take = (int)Math.Min(request.Limit, Math.Max(0L, count - offset));
            var entries = new List<CollectionEntry>(take);
            for (var index = 0; index < take; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var position = offset + index;
                var value = _Definition.GetElement(collection, checked((int)position));
                var keyText = value is null ? null : _Definition.GetKey?.Invoke(value);
                var key = keyText is null ? null : new CollectionEntryKey(keyText);
                entries.Add(await _CreateEntryAsync(position, value, key, cancellationToken)
                    .ConfigureAwait(false));
            }

            return InteractionResult.Success(new CollectionReadResult(
                request.Mode,
                entries,
                offset,
                count,
                offset + entries.Count < count));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return InteractionResult.Failure<CollectionReadResult>(
                InteractionErrorCode.CANCELLED,
                "Collection enumeration was cancelled.");
        }
        catch (Exception exception)
        {
            return InteractionResult.Failure<CollectionReadResult>(
                InteractionErrorCode.INVOCATION_FAILED,
                exception.Message);
        }
    }

    public async ValueTask<InteractionResult<IReadOnlyList<ObjectHandle>>> SelectByKeyAsync(
        string key,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_Definition.GetKey is null)
        {
            return InteractionResult.Failure<IReadOnlyList<ObjectHandle>>(
                InteractionErrorCode.COLLECTION_ACCESS_UNSUPPORTED,
                $"Collection '{Id}' has no key selector.");
        }

        var unavailable = CheckAvailability<IReadOnlyList<ObjectHandle>>(
            "collection key selection",
            cancellationToken);
        if (unavailable is not null)
        {
            return unavailable;
        }

        TryGetTarget(out var target);
        var collection = _Definition.Read(target!);
        if (collection is null)
        {
            return InteractionResult.Failure<IReadOnlyList<ObjectHandle>>(
                InteractionErrorCode.TARGET_UNAVAILABLE,
                $"Collection '{Id}' is null or unavailable.");
        }

        var count = _Definition.GetCount(collection);

        var matches = new List<ObjectHandle>();
        for (var index = 0; index < count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = _Definition.GetElement(collection, index);
            if (value is null || value.GetType().IsValueType || value is string ||
                !StringComparer.Ordinal.Equals(_Definition.GetKey(value), key))
            {
                continue;
            }

            var encountered = await Host.EncounterAsync(value, cancellationToken).ConfigureAwait(false);
            if (!encountered.IsSuccess)
            {
                return InteractionResult.Failure<IReadOnlyList<ObjectHandle>>(
                    encountered.Error!.Code,
                    encountered.Error.Message,
                    encountered.Error.Issues);
            }

            matches.Add(encountered.Value);
        }

        return InteractionResult.Success<IReadOnlyList<ObjectHandle>>(matches);
    }

    private async ValueTask<CollectionEntry> _CreateEntryAsync(
        long position,
        object? value,
        CollectionEntryKey? key,
        CancellationToken cancellationToken)
    {
        if (value is null)
        {
            return CollectionEntry.Null(position, key);
        }

        if (value.GetType().IsValueType || value is string)
        {
            return CollectionEntry.Scalar(position, value, key);
        }

        var encountered = await Host.EncounterAsync(value, cancellationToken).ConfigureAwait(false);
        if (!encountered.IsSuccess)
        {
            throw new InvalidOperationException(encountered.Error!.Message);
        }

        var identity = await Host.GetDomainIdentityAsync(encountered.Value, cancellationToken)
            .ConfigureAwait(false);
        if (!identity.IsSuccess)
        {
            throw new InvalidOperationException(identity.Error!.Message);
        }

        return CollectionEntry.ReferenceValue(position, encountered.Value, identity.Value, key);
    }
}

internal sealed class ProgrammaticActionDescriptor : ProgrammaticMemberDescriptor, IActionDescriptor
{
    private readonly ActionExposureDefinition _Definition;

    public ProgrammaticActionDescriptor(
        UIEngineHost host,
        object target,
        ActionExposureDefinition definition)
        : base(host, target, definition.Id, definition.GetDisplayName(target))
    {
        _Definition = definition;
        Parameters = definition.Parameters
            .Select(static parameter => (IParameterDescriptor)new ProgrammaticParameterDescriptor(parameter))
            .ToArray();
    }

    public IReadOnlyList<IParameterDescriptor> Parameters { get; }

    public Type? ResultType => _Definition.ResultType;

    public bool IsAsynchronous => _Definition.IsAsynchronous;

    public bool SupportsCancellation => _Definition.SupportsCancellation;

    public Type? ProgressType => _Definition.ProgressType;

    public ActionRisk Risk => _Definition.Risk;

    public bool RequiresConfirmation => _Definition.RequiresConfirmation;

    public async ValueTask<InteractionResult<IActionInvocation>> InvokeAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        var unavailable = CheckAvailability<IActionInvocation>("action invocation", cancellationToken);
        if (unavailable is not null)
        {
            return unavailable;
        }

        var unknown = arguments.Keys.FirstOrDefault(argument =>
            !_Definition.Parameters.Any(parameter => StringComparer.Ordinal.Equals(parameter.Id, argument)));
        if (unknown is not null)
        {
            return _ArgumentFailure(
                unknown,
                InteractionIssueCode.ACTION_REJECTED,
                $"Action '{Id}' has no parameter named '{unknown}'.");
        }

        var bound = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var parameter in _Definition.Parameters)
        {
            if (!arguments.TryGetValue(parameter.Id, out var supplied))
            {
                if (parameter.IsRequired)
                {
                    return _ArgumentFailure(
                        parameter.Id,
                        InteractionIssueCode.REQUIRED,
                        $"Required parameter '{parameter.Id}' was not supplied.");
                }

                var defaultValue = parameter.HasDefaultValue
                    ? ReflectionValueConverter.Convert(parameter.DefaultValue, parameter.ParameterType).Value
                    : null;
                bound.Add(parameter.Id, defaultValue);
                continue;
            }

            var converted = ReflectionValueConverter.Convert(supplied, parameter.ParameterType);
            if (!converted.IsSuccess)
            {
                return _ArgumentFailure(
                    parameter.Id,
                    converted.Error!.Code == InteractionErrorCode.VALIDATION_FAILED
                        ? InteractionIssueCode.NULL_NOT_ALLOWED
                        : InteractionIssueCode.CONVERSION_FAILED,
                    converted.Error.Message,
                    converted.Error.Code);
            }

            var issues = ValueValidation.Validate(
                converted.Value,
                parameter.IsNullable,
                parameter.Options,
                parameter.Range,
                [],
                null,
                InteractionIssueTarget.PARAMETER,
                parameter.Id);
            if (issues.Count > 0)
            {
                return InteractionResult.Failure<IActionInvocation>(
                    InteractionErrorCode.VALIDATION_FAILED,
                    $"Parameter '{parameter.Id}' failed validation.",
                    issues);
            }

            bound.Add(parameter.Id, converted.Value);
        }

        var created = Host.CreateInvocation(Id, SupportsCancellation);
        if (!created.IsSuccess)
        {
            return InteractionResult.Failure<IActionInvocation>(
                created.Error!.Code,
                created.Error.Message,
                created.Error.Issues);
        }

        TryGetTarget(out var target);
        var invocation = created.Value;
        var progress = ProgressType is null ? null : invocation.CreateProgressReporter(ProgressType);
        invocation.Start();
        if (IsAsynchronous)
        {
            _ = _CompleteAsync(invocation, target!, bound, progress);
        }
        else
        {
            try
            {
                var result = await _Definition.Invoke(
                    target!,
                    bound,
                    CancellationToken.None,
                    progress).ConfigureAwait(false);
                invocation.CompleteSuccess(result);
            }
            catch (Exception exception)
            {
                invocation.CompleteFailure(exception);
            }
        }

        return InteractionResult.Success<IActionInvocation>(invocation);
    }

    private async Task _CompleteAsync(
        ActionInvocation invocation,
        object target,
        IReadOnlyDictionary<string, object?> arguments,
        object? progress)
    {
        try
        {
            var result = await _Definition.Invoke(
                target,
                arguments,
                invocation.CancellationToken,
                progress).ConfigureAwait(false);
            invocation.CompleteSuccess(result);
        }
        catch (Exception exception)
        {
            invocation.CompleteFailure(exception);
        }
    }

    private static InteractionResult<IActionInvocation> _ArgumentFailure(
        string parameterId,
        InteractionIssueCode issueCode,
        string message,
        InteractionErrorCode errorCode = InteractionErrorCode.INVALID_INPUT) =>
        InteractionResult.Failure<IActionInvocation>(
            errorCode,
            message,
            [new InteractionIssue(
                issueCode,
                InteractionIssueTarget.PARAMETER,
                parameterId,
                message)]);
}

internal sealed class ProgrammaticParameterDescriptor : IParameterDescriptor
{
    public ProgrammaticParameterDescriptor(ProgrammaticParameter parameter)
    {
        Id = parameter.Id;
        DisplayName = parameter.DisplayName;
        ParameterType = parameter.ParameterType;
        IsRequired = parameter.IsRequired;
        IsNullable = parameter.IsNullable;
        HasDefaultValue = parameter.HasDefaultValue;
        DefaultValue = parameter.DefaultValue;
        Options = parameter.Options.ToArray();
        Range = parameter.Range;
        ValidationRules = (parameter.IsRequired
                ? new[] { new ValidationRuleDescriptor(ValidationRuleKind.REQUIRED) }
                : [])
            .Concat(parameter.Options.Count > 0
                ? [new ValidationRuleDescriptor(ValidationRuleKind.FINITE_SELECTION)]
                : [])
            .Concat(parameter.Range is not null
                ? [new ValidationRuleDescriptor(ValidationRuleKind.RANGE)]
                : [])
            .ToArray();
        Unit = parameter.Unit;
        Tags = parameter.Tags.ToArray();
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

internal sealed class ProgrammaticValueDescriptor : IValueDescriptor
{
    private readonly UIEngineHost _Host;
    private readonly WeakReference<object> _Target;
    private readonly ValueExposureDefinition _Definition;

    public ProgrammaticValueDescriptor(
        UIEngineHost host,
        object target,
        ValueExposureDefinition definition)
    {
        _Host = host;
        _Target = new WeakReference<object>(target);
        _Definition = definition;
        DisplayName = definition.GetDisplayName(target);
    }

    public string Id => _Definition.Id;

    public string DisplayName { get; }

    public Type ValueType => _Definition.ValueType;

    public bool CanRead => true;

    public bool CanWrite => _Definition.Write is not null;

    public bool IsNullable => _Definition.IsNullable;

    public IReadOnlyList<SelectionOption> Options => _Definition.Options;

    public ValueRange? Range => _Definition.Range;

    public IReadOnlyList<ValidationRuleDescriptor> ValidationRules => _Definition.ValidationRules;

    public string? Unit => _Definition.Unit;

    public IReadOnlyList<string> Tags => _Definition.Tags;

    public ValueTask<InteractionResult<object?>> ReadAsync(CancellationToken cancellationToken = default)
    {
        var unavailable = _CheckAvailability("read", cancellationToken);
        if (unavailable is not null)
        {
            return ValueTask.FromResult(unavailable);
        }

        _Target.TryGetTarget(out var target);
        try
        {
            return ValueTask.FromResult(InteractionResult.Success(_Definition.Read(target!)));
        }
        catch (TargetInvocationException exception)
        {
            return ValueTask.FromResult(_InvocationFailure(exception.InnerException ?? exception));
        }
        catch (Exception exception)
        {
            return ValueTask.FromResult(_InvocationFailure(exception));
        }
    }

    public async ValueTask<InteractionResult<object?>> WriteAsync(
        object? value,
        CancellationToken cancellationToken = default)
    {
        var unavailable = _CheckAvailability("write", cancellationToken);
        if (unavailable is not null)
        {
            return unavailable;
        }

        if (_Definition.Write is null)
        {
            var message = $"Value '{Id}' is read-only.";
            return InteractionResult.Failure<object?>(
                InteractionErrorCode.VALIDATION_FAILED,
                message,
                [new InteractionIssue(
                    InteractionIssueCode.READ_ONLY,
                    InteractionIssueTarget.VALUE,
                    Id,
                    message)]);
        }

        var converted = ReflectionValueConverter.Convert(value, ValueType);
        if (!converted.IsSuccess)
        {
            var error = converted.Error!;
            return InteractionResult.Failure<object?>(
                error.Code,
                error.Message,
                [new InteractionIssue(
                    error.Code == InteractionErrorCode.VALIDATION_FAILED
                        ? InteractionIssueCode.NULL_NOT_ALLOWED
                        : InteractionIssueCode.CONVERSION_FAILED,
                    InteractionIssueTarget.VALUE,
                    Id,
                    error.Message)]);
        }

        _Target.TryGetTarget(out var target);
        var issues = ValueValidation.Validate(
            converted.Value,
            IsNullable,
            Options,
            Range,
            [],
            target,
            InteractionIssueTarget.VALUE,
            Id).ToList();
        if (issues.Count == 0)
        {
            foreach (var validator in _Definition.Validators)
            {
                string? domainIssue;
                try
                {
                    var pendingResult = await _Host.ExecuteInteractionAsync(
                        InteractionDispatchOperation.VALUE_WRITE,
                        canExecuteDirectly: false,
                        () => InteractionResult.Success(
                            validator(target!, converted.Value, cancellationToken).AsTask()),
                        cancellationToken).ConfigureAwait(false);
                    if (!pendingResult.IsSuccess)
                    {
                        return InteractionResult.Failure<object?>(
                            pendingResult.Error!.Code,
                            pendingResult.Error.Message,
                            pendingResult.Error.Issues);
                    }

                    domainIssue = await pendingResult.Value.ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return InteractionResult.Failure<object?>(
                        InteractionErrorCode.CANCELLED,
                        "Value validation was cancelled.");
                }
                catch (Exception exception)
                {
                    return InteractionResult.Failure<object?>(
                        InteractionErrorCode.INVOCATION_FAILED,
                        $"Validation for value '{Id}' failed: {exception.Message}");
                }

                if (domainIssue is not null)
                {
                    issues.Add(new InteractionIssue(
                        InteractionIssueCode.RULE_FAILED,
                        InteractionIssueTarget.VALUE,
                        Id,
                        domainIssue));
                }
            }
        }

        if (issues.Count > 0)
        {
            return InteractionResult.Failure<object?>(
                InteractionErrorCode.VALIDATION_FAILED,
                $"Value '{Id}' failed validation.",
                issues);
        }

        return await _Host.ExecuteInteractionAsync(
            InteractionDispatchOperation.VALUE_WRITE,
            canExecuteDirectly: false,
            () =>
            {
                try
                {
                    _Definition.Write(target!, converted.Value);
                    return InteractionResult.Success(converted.Value);
                }
                catch (TargetInvocationException exception)
                {
                    return _WriteFailure(exception.InnerException ?? exception);
                }
                catch (Exception exception)
                {
                    return _WriteFailure(exception);
                }
            },
            cancellationToken).ConfigureAwait(false);
    }

    private InteractionResult<object?>? _CheckAvailability(
        string operation,
        CancellationToken cancellationToken)
    {
        if (_Host.IsDisposed)
        {
            return InteractionResult.Failure<object?>(
                InteractionErrorCode.HOST_DISPOSED,
                "The UIEngine host has been disposed.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return InteractionResult.Failure<object?>(
                InteractionErrorCode.CANCELLED,
                $"The value {operation} was cancelled.");
        }

        if (!_Target.TryGetTarget(out _))
        {
            return InteractionResult.Failure<object?>(
                InteractionErrorCode.TARGET_UNAVAILABLE,
                "The containing object is no longer available.");
        }

        return null;
    }

    private static InteractionResult<object?> _InvocationFailure(Exception exception) =>
        InteractionResult.Failure<object?>(InteractionErrorCode.INVOCATION_FAILED, exception.Message);

    private InteractionResult<object?> _WriteFailure(Exception exception) =>
        InteractionResult.Failure<object?>(
            exception is UnauthorizedAccessException
                ? InteractionErrorCode.PERMISSION_DENIED
                : exception is ArgumentException or InvalidOperationException
                    ? InteractionErrorCode.VALIDATION_FAILED
                    : InteractionErrorCode.INVOCATION_FAILED,
            exception.Message,
            [new InteractionIssue(
                exception is UnauthorizedAccessException
                    ? InteractionIssueCode.PERMISSION_DENIED
                    : exception is ArgumentException or InvalidOperationException
                        ? InteractionIssueCode.RULE_FAILED
                        : InteractionIssueCode.ACTION_REJECTED,
                exception is UnauthorizedAccessException
                    ? InteractionIssueTarget.PERMISSION
                    : InteractionIssueTarget.VALUE,
                Id,
                exception.Message)]);
}
