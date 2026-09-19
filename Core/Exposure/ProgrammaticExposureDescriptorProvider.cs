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

        IObjectDescriptor? reflectionDescriptor = null;
        if (_ReflectionFallback is not null && _ReflectionFallback.CanDescribe(instance.GetType()))
        {
            var reflectionResult = await _ReflectionFallback
                .DescribeAsync(host, instance, handle, cancellationToken)
                .ConfigureAwait(false);
            if (!reflectionResult.IsSuccess)
            {
                return reflectionResult;
            }

            reflectionDescriptor = reflectionResult.Value;
        }

        IObjectDescriptor descriptor = new ProgrammaticObjectDescriptor(
            host,
            instance,
            handle,
            registration,
            reflectionDescriptor);
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

        var registeredIdentifiers = registration.Values
            .Select(static definition => definition.Id)
            .ToHashSet(StringComparer.Ordinal);
        Values = registration.Values
            .Select(definition => (IValueDescriptor)new ProgrammaticValueDescriptor(
                host,
                instance,
                definition))
            .Concat(reflectionDescriptor?.Values.Where(value => !registeredIdentifiers.Contains(value.Id)) ?? [])
            .ToArray();
        References = _WithoutOverrides(reflectionDescriptor?.References, registeredIdentifiers);
        Collections = _WithoutOverrides(reflectionDescriptor?.Collections, registeredIdentifiers);
        Actions = _WithoutOverrides(reflectionDescriptor?.Actions, registeredIdentifiers);
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
