using System.Collections;
using System.Collections.Specialized;
using System.Reflection;
using System.Runtime.CompilerServices;
using UIEngine.Core.Attributes;

namespace UIEngine.Core.Reflection;

/// <summary>Creates semantic descriptors for explicitly annotated reference types.</summary>
public sealed class ReflectionObjectDescriptorProvider : IObjectDescriptorProvider
{
    public bool CanDescribe(Type objectType)
    {
        ArgumentNullException.ThrowIfNull(objectType);
        return !objectType.IsValueType;
    }

    public ValueTask<InteractionResult<IObjectDescriptor>> DescribeAsync(
        UIEngineHost host,
        object instance,
        ObjectHandle handle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(instance);

        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromResult(InteractionResult.Failure<IObjectDescriptor>(
                InteractionErrorCode.CANCELLED,
                "Descriptor discovery was cancelled."));
        }

        if (!CanDescribe(instance.GetType()))
        {
            return ValueTask.FromResult(InteractionResult.Failure<IObjectDescriptor>(
                InteractionErrorCode.UNSUPPORTED_TARGET_TYPE,
                "Reflection descriptors require a reference-type target."));
        }

        var metadata = ReflectionTypeMetadataCache.GetOrCreate(instance.GetType());
        IObjectDescriptor descriptor = new ReflectionObjectDescriptor(host, instance, handle, metadata);
        return ValueTask.FromResult(InteractionResult.Success(descriptor));
    }
}

/// <summary>Describes one live object without recursively expanding its references.</summary>
internal sealed class ReflectionObjectDescriptor : IObjectDescriptor
{
    public ReflectionObjectDescriptor(
        UIEngineHost host,
        object instance,
        ObjectHandle handle,
        ReflectionTypeMetadata metadata)
    {
        Identity = handle.Identity;
        TypeName = instance.GetType().FullName ?? instance.GetType().Name;
        DisplayName = instance.GetType().Name;
        Summary = _ReadSummary(metadata.SummaryMember, instance);

        Values = metadata.DescriptorMembers
            .Where(static member => member.Kind == ReflectionMemberKind.VALUE)
            .Select(member => (IValueDescriptor)new ReflectionValueDescriptor(host, instance, member))
            .ToArray();
        References = metadata.DescriptorMembers
            .Where(static member => member.Kind == ReflectionMemberKind.REFERENCE)
            .Select(member => (IReferenceDescriptor)new ReflectionReferenceDescriptor(host, instance, member))
            .ToArray();
        Collections = metadata.DescriptorMembers
            .Where(static member => member.Kind == ReflectionMemberKind.COLLECTION)
            .Select(member => (ICollectionDescriptor)new ReflectionCollectionDescriptor(host, instance, member))
            .ToArray();
        Actions = metadata.DescriptorMembers
            .Where(static member => member.Kind == ReflectionMemberKind.ACTION)
            .Select(member => (IActionDescriptor)new ReflectionActionDescriptor(host, instance, member))
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

    private static string? _ReadSummary(MemberInfo? summaryMember, object instance)
    {
        if (summaryMember is null)
        {
            return null;
        }

        try
        {
            return ReflectionMemberAccess.Read(summaryMember, instance)?.ToString();
        }
        catch (TargetInvocationException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}

/// <summary>Provides shared metadata and weak target access for reflected member descriptors.</summary>
internal abstract class ReflectionMemberDescriptor : IMemberDescriptor
{
    private readonly UIEngineHost _Host;
    private readonly WeakReference<object> _Target;

    protected ReflectionMemberDescriptor(
        UIEngineHost host,
        object target,
        ReflectionMemberMetadata metadata)
    {
        _Host = host;
        _Target = new WeakReference<object>(target);
        Metadata = metadata;
    }

    public string Id => Metadata.Id;

    public string DisplayName => Metadata.DisplayName;

    protected ReflectionMemberMetadata Metadata { get; }

    protected bool IsHostDisposed => _Host.IsDisposed;

    protected bool TryGetTarget(out object? target) => _Target.TryGetTarget(out target);

    protected static InteractionResult<T> _DisposedFailure<T>() => InteractionResult.Failure<T>(
        InteractionErrorCode.HOST_DISPOSED,
        "The UIEngine host has been disposed.");
}

/// <summary>Reads and writes one exposed scalar member against current domain state.</summary>
internal sealed class ReflectionValueDescriptor : ReflectionMemberDescriptor, IValueDescriptor
{
    public ReflectionValueDescriptor(
        UIEngineHost host,
        object target,
        ReflectionMemberMetadata metadata)
        : base(host, target, metadata)
    {
        CanRead = ReflectionMemberAccess.CanRead(metadata.Member);
        CanWrite = ReflectionMemberAccess.CanWrite(metadata.Member) &&
            metadata.Member.GetCustomAttribute<ExposeAttribute>(inherit: true)?.ReadOnly != true;
    }

    public Type ValueType => Metadata.MemberType;

    public bool CanRead { get; }

    public bool CanWrite { get; }

    public bool IsNullable => Metadata.IsNullable;

    public IReadOnlyList<SelectionOption> Options => Metadata.Options;

    public ValueRange? Range => Metadata.Range;

    public IReadOnlyList<ValidationRuleDescriptor> ValidationRules => Metadata.ValidationRules;

    public string? Unit => Metadata.Unit;

    public IReadOnlyList<string> Tags => Metadata.Tags;

    public ValueTask<InteractionResult<object?>> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (IsHostDisposed)
        {
            return ValueTask.FromResult(_DisposedFailure<object?>());
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromResult(InteractionResult.Failure<object?>(
                InteractionErrorCode.CANCELLED,
                "The value read was cancelled."));
        }

        if (!CanRead)
        {
            return ValueTask.FromResult(InteractionResult.Failure<object?>(
                InteractionErrorCode.DESCRIPTOR_UNAVAILABLE,
                $"Value '{Id}' is not readable."));
        }

        if (!TryGetTarget(out var target) || target is null)
        {
            return ValueTask.FromResult(InteractionResult.Failure<object?>(
                InteractionErrorCode.TARGET_UNAVAILABLE,
                "The containing object is no longer available."));
        }

        try
        {
            return ValueTask.FromResult(InteractionResult.Success(
                ReflectionMemberAccess.Read(Metadata.Member, target)));
        }
        catch (TargetInvocationException exception)
        {
            return ValueTask.FromResult(InteractionResult.Failure<object?>(
                InteractionErrorCode.INVOCATION_FAILED,
                exception.InnerException?.Message ?? exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            return ValueTask.FromResult(InteractionResult.Failure<object?>(
                InteractionErrorCode.DESCRIPTOR_UNAVAILABLE,
                exception.Message));
        }
    }

    public ValueTask<InteractionResult<object?>> WriteAsync(
        object? value,
        CancellationToken cancellationToken = default)
    {
        if (IsHostDisposed)
        {
            return ValueTask.FromResult(_DisposedFailure<object?>());
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromResult(InteractionResult.Failure<object?>(
                InteractionErrorCode.CANCELLED,
                "The value write was cancelled."));
        }

        if (!CanWrite)
        {
            var message = $"Value '{Id}' is read-only.";
            return ValueTask.FromResult(InteractionResult.Failure<object?>(
                InteractionErrorCode.VALIDATION_FAILED,
                message,
                [new InteractionIssue(
                    InteractionIssueCode.READ_ONLY,
                    InteractionIssueTarget.VALUE,
                    Id,
                    message)]));
        }

        if (!TryGetTarget(out var target) || target is null)
        {
            return ValueTask.FromResult(InteractionResult.Failure<object?>(
                InteractionErrorCode.TARGET_UNAVAILABLE,
                "The containing object is no longer available."));
        }

        var converted = ReflectionValueConverter.Convert(value, ValueType);
        if (!converted.IsSuccess)
        {
            var error = converted.Error!;
            return ValueTask.FromResult(InteractionResult.Failure<object?>(
                error.Code,
                error.Message,
                [new InteractionIssue(
                    error.Code == InteractionErrorCode.VALIDATION_FAILED
                        ? InteractionIssueCode.NULL_NOT_ALLOWED
                        : InteractionIssueCode.CONVERSION_FAILED,
                    InteractionIssueTarget.VALUE,
                    Id,
                    error.Message)]));
        }

        var issues = ValueValidation.Validate(
            converted.Value,
            IsNullable,
            Options,
            range: null,
            Metadata.ValidationAttributes,
            target,
            InteractionIssueTarget.VALUE,
            Id);
        if (issues.Count > 0)
        {
            return ValueTask.FromResult(InteractionResult.Failure<object?>(
                InteractionErrorCode.VALIDATION_FAILED,
                $"Value '{Id}' failed validation.",
                issues));
        }

        try
        {
            ReflectionMemberAccess.Write(Metadata.Member, target, converted.Value);
            return ValueTask.FromResult(InteractionResult.Success(converted.Value));
        }
        catch (TargetInvocationException exception)
        {
            var cause = exception.InnerException ?? exception;
            return ValueTask.FromResult(_SetterFailure(cause));
        }
        catch (ArgumentException exception)
        {
            return ValueTask.FromResult(_SetterFailure(exception));
        }
        catch (InvalidOperationException exception)
        {
            return ValueTask.FromResult(_SetterFailure(exception));
        }
        catch (UnauthorizedAccessException exception)
        {
            return ValueTask.FromResult(_SetterFailure(exception));
        }
    }

    private InteractionResult<object?> _SetterFailure(Exception exception)
    {
        var denied = exception is UnauthorizedAccessException;
        return InteractionResult.Failure<object?>(
            denied ? InteractionErrorCode.PERMISSION_DENIED : InteractionErrorCode.VALIDATION_FAILED,
            exception.Message,
            [new InteractionIssue(
                denied ? InteractionIssueCode.PERMISSION_DENIED : InteractionIssueCode.RULE_FAILED,
                denied ? InteractionIssueTarget.PERMISSION : InteractionIssueTarget.VALUE,
                Id,
                exception.Message)]);
    }
}

/// <summary>Resolves an exposed object reference to a host-scoped handle on demand.</summary>
internal sealed class ReflectionReferenceDescriptor : ReflectionMemberDescriptor, IReferenceDescriptor
{
    private readonly UIEngineHost _Host;

    public ReflectionReferenceDescriptor(
        UIEngineHost host,
        object target,
        ReflectionMemberMetadata metadata)
        : base(host, target, metadata)
    {
        _Host = host;
    }

    public Type ReferenceType => Metadata.MemberType;

    public async ValueTask<InteractionResult<ObjectHandle?>> ReadAsync(
        CancellationToken cancellationToken = default)
    {
        if (IsHostDisposed)
        {
            return _DisposedFailure<ObjectHandle?>();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return InteractionResult.Failure<ObjectHandle?>(
                InteractionErrorCode.CANCELLED,
                "Reference resolution was cancelled.");
        }

        if (!TryGetTarget(out var target) || target is null)
        {
            return InteractionResult.Failure<ObjectHandle?>(
                InteractionErrorCode.TARGET_UNAVAILABLE,
                "The containing object is no longer available.");
        }

        try
        {
            var referencedObject = ReflectionMemberAccess.Read(Metadata.Member, target);
            if (referencedObject is null)
            {
                return InteractionResult.Success<ObjectHandle?>(null);
            }

            if (referencedObject.GetType().IsValueType)
            {
                return InteractionResult.Failure<ObjectHandle?>(
                    InteractionErrorCode.UNSUPPORTED_TARGET_TYPE,
                    $"Reference '{Id}' produced a value type.");
            }

            var encountered = await _Host.EncounterAsync(referencedObject, cancellationToken).ConfigureAwait(false);
            return encountered.IsSuccess
                ? InteractionResult.Success<ObjectHandle?>(encountered.Value)
                : InteractionResult.Failure<ObjectHandle?>(
                    encountered.Error!.Code,
                    encountered.Error.Message,
                    encountered.Error.Issues);
        }
        catch (TargetInvocationException exception)
        {
            return InteractionResult.Failure<ObjectHandle?>(
                InteractionErrorCode.INVOCATION_FAILED,
                exception.InnerException?.Message ?? exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return InteractionResult.Failure<ObjectHandle?>(
                InteractionErrorCode.DESCRIPTOR_UNAVAILABLE,
                exception.Message);
        }
    }
}

/// <summary>Provides bounded access that reflects the collection's declared shape.</summary>
internal sealed class ReflectionCollectionDescriptor :
    ReflectionMemberDescriptor,
    ICollectionDescriptor,
    ICollectionPathSelector
{
    private readonly UIEngineHost _Host;

    public ReflectionCollectionDescriptor(
        UIEngineHost host,
        object target,
        ReflectionMemberMetadata metadata)
        : base(host, target, metadata)
    {
        _Host = host;
        ElementType = ReflectionTypeClassifier.GetCollectionElementType(metadata.MemberType);
        KeyType = ReflectionTypeClassifier.GetDictionaryKeyType(metadata.MemberType);
        Capabilities = ReflectionTypeClassifier.GetCollectionCapabilities(metadata.MemberType);
    }

    public Type ElementType { get; }

    public Type? KeyType { get; }

    public CollectionCapabilities Capabilities { get; }

    public async ValueTask<InteractionResult<CollectionReadResult>> ReadAsync(
        CollectionReadRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (IsHostDisposed)
        {
            return _DisposedFailure<CollectionReadResult>();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return InteractionResult.Failure<CollectionReadResult>(
                InteractionErrorCode.CANCELLED,
                "Collection enumeration was cancelled.");
        }

        var requiredCapability = request.Mode switch
        {
            CollectionAccessMode.SNAPSHOT => CollectionCapabilities.FINITE_SNAPSHOT,
            CollectionAccessMode.PAGE => CollectionCapabilities.PAGING,
            CollectionAccessMode.VIRTUALIZED_RANGE => CollectionCapabilities.VIRTUALIZED_RANGE,
            _ => CollectionCapabilities.NONE,
        };
        if ((Capabilities & requiredCapability) == 0)
        {
            return InteractionResult.Failure<CollectionReadResult>(
                InteractionErrorCode.COLLECTION_ACCESS_UNSUPPORTED,
                $"Collection '{Id}' does not support {request.Mode}.");
        }

        if (!TryGetTarget(out var target) || target is null)
        {
            return InteractionResult.Failure<CollectionReadResult>(
                InteractionErrorCode.TARGET_UNAVAILABLE,
                "The containing object is no longer available.");
        }

        try
        {
            if (ReflectionMemberAccess.Read(Metadata.Member, target) is not IEnumerable collection)
            {
                return InteractionResult.Failure<CollectionReadResult>(
                    InteractionErrorCode.TARGET_UNAVAILABLE,
                    $"Collection '{Id}' is null or unavailable.");
            }

            return request.Mode switch
            {
                CollectionAccessMode.SNAPSHOT => await _ReadSnapshotAsync(
                    collection,
                    request,
                    cancellationToken).ConfigureAwait(false),
                CollectionAccessMode.VIRTUALIZED_RANGE => await _ReadRangeAsync(
                    collection,
                    request,
                    cancellationToken).ConfigureAwait(false),
                _ => InteractionResult.Failure<CollectionReadResult>(
                    InteractionErrorCode.COLLECTION_ACCESS_UNSUPPORTED,
                    $"Collection '{Id}' does not support {request.Mode}."),
            };
        }
        catch (TargetInvocationException exception)
        {
            return InteractionResult.Failure<CollectionReadResult>(
                InteractionErrorCode.INVOCATION_FAILED,
                exception.InnerException?.Message ?? exception.Message);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
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
        if (IsHostDisposed)
        {
            return _DisposedFailure<IReadOnlyList<ObjectHandle>>();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return InteractionResult.Failure<IReadOnlyList<ObjectHandle>>(
                InteractionErrorCode.CANCELLED,
                "Collection selection was cancelled.");
        }

        if (!TryGetTarget(out var target) || target is null)
        {
            return InteractionResult.Failure<IReadOnlyList<ObjectHandle>>(
                InteractionErrorCode.TARGET_UNAVAILABLE,
                "The containing object is no longer available.");
        }

        try
        {
            if (ReflectionMemberAccess.Read(Metadata.Member, target) is not IDictionary dictionary)
            {
                return InteractionResult.Failure<IReadOnlyList<ObjectHandle>>(
                    InteractionErrorCode.TARGET_MISSING,
                    $"Collection '{Id}' does not support string-keyed selection.");
            }

            var matchingValues = dictionary.Keys
                .Cast<object?>()
                .Where(candidate => candidate is string text && StringComparer.Ordinal.Equals(text, key))
                .Select(candidate => dictionary[candidate!])
                .ToArray();
            var handles = new List<ObjectHandle>(matchingValues.Length);
            foreach (var value in matchingValues)
            {
                if (value is null || value.GetType().IsValueType)
                {
                    return InteractionResult.Failure<IReadOnlyList<ObjectHandle>>(
                        InteractionErrorCode.TYPE_MISMATCH,
                        $"Collection '{Id}' key '{key}' does not identify a reference object.");
                }

                var encountered = await _Host.EncounterAsync(value, cancellationToken).ConfigureAwait(false);
                if (!encountered.IsSuccess)
                {
                    return InteractionResult.Failure<IReadOnlyList<ObjectHandle>>(
                        encountered.Error!.Code,
                        encountered.Error.Message);
                }

                handles.Add(encountered.Value);
            }

            return InteractionResult.Success<IReadOnlyList<ObjectHandle>>(handles);
        }
        catch (TargetInvocationException exception)
        {
            return InteractionResult.Failure<IReadOnlyList<ObjectHandle>>(
                InteractionErrorCode.INVOCATION_FAILED,
                exception.InnerException?.Message ?? exception.Message);
        }
        catch (InvalidOperationException exception)
        {
            return InteractionResult.Failure<IReadOnlyList<ObjectHandle>>(
                InteractionErrorCode.INVOCATION_FAILED,
                exception.Message);
        }
    }

    private async ValueTask<InteractionResult<CollectionReadResult>> _ReadSnapshotAsync(
        IEnumerable collection,
        CollectionReadRequest request,
        CancellationToken cancellationToken)
    {
        if (!ReflectionTypeClassifier.TryGetCollectionCount(collection, out var initialCount))
        {
            return InteractionResult.Failure<CollectionReadResult>(
                InteractionErrorCode.COLLECTION_ACCESS_UNSUPPORTED,
                $"Collection '{Id}' no longer has a finite collection shape.");
        }

        if (initialCount > request.Limit)
        {
            return InteractionResult.Failure<CollectionReadResult>(
                InteractionErrorCode.COLLECTION_LIMIT_EXCEEDED,
                $"Collection '{Id}' contains {initialCount} entries, exceeding the requested snapshot limit {request.Limit}.");
        }

        var entries = new List<CollectionEntry>(initialCount);
        foreach (var item in collection)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return InteractionResult.Failure<CollectionReadResult>(
                    InteractionErrorCode.CANCELLED,
                    "Collection enumeration was cancelled.");
            }

            if (entries.Count == request.Limit)
            {
                return InteractionResult.Failure<CollectionReadResult>(
                    InteractionErrorCode.COLLECTION_LIMIT_EXCEEDED,
                    $"Collection '{Id}' grew beyond the requested snapshot limit {request.Limit}.");
            }

            var split = ReflectionTypeClassifier.SplitDictionaryEntry(collection, item);
            var entry = await _CreateEntryAsync(
                entries.Count,
                split.Value,
                split.HasKey ? new CollectionEntryKey(split.Key) : null,
                cancellationToken).ConfigureAwait(false);
            if (!entry.IsSuccess)
            {
                return InteractionResult.Failure<CollectionReadResult>(
                    entry.Error!.Code,
                    entry.Error.Message,
                    entry.Error.Issues);
            }

            entries.Add(entry.Value);
        }

        return InteractionResult.Success(new CollectionReadResult(
            CollectionAccessMode.SNAPSHOT,
            entries,
            0,
            entries.Count,
            hasMore: false));
    }

    private async ValueTask<InteractionResult<CollectionReadResult>> _ReadRangeAsync(
        IEnumerable collection,
        CollectionReadRequest request,
        CancellationToken cancellationToken)
    {
        if (!ReflectionTypeClassifier.TryGetIndexedCollection(collection, out var count, out var readAt))
        {
            return InteractionResult.Failure<CollectionReadResult>(
                InteractionErrorCode.COLLECTION_ACCESS_UNSUPPORTED,
                $"Collection '{Id}' no longer has an indexed collection shape.");
        }

        var entries = new List<CollectionEntry>();
        if (request.Offset < count)
        {
            var end = Math.Min((long)count, request.Offset + request.Limit);
            for (var position = request.Offset; position < end; position++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return InteractionResult.Failure<CollectionReadResult>(
                        InteractionErrorCode.CANCELLED,
                        "Collection range access was cancelled.");
                }

                var entry = await _CreateEntryAsync(
                    position,
                    readAt(checked((int)position)),
                    key: null,
                    cancellationToken).ConfigureAwait(false);
                if (!entry.IsSuccess)
                {
                    return InteractionResult.Failure<CollectionReadResult>(
                        entry.Error!.Code,
                        entry.Error.Message,
                        entry.Error.Issues);
                }

                entries.Add(entry.Value);
            }
        }

        var nextOffset = request.Offset + entries.Count;
        return InteractionResult.Success(new CollectionReadResult(
            CollectionAccessMode.VIRTUALIZED_RANGE,
            entries,
            request.Offset,
            count,
            nextOffset < count));
    }

    private async ValueTask<InteractionResult<CollectionEntry>> _CreateEntryAsync(
        long position,
        object? value,
        CollectionEntryKey? key,
        CancellationToken cancellationToken)
    {
        if (value is null)
        {
            return InteractionResult.Success(CollectionEntry.Null(position, key));
        }

        if (ReflectionTypeClassifier.IsScalar(value.GetType()))
        {
            return InteractionResult.Success(CollectionEntry.Scalar(position, value, key));
        }

        var encountered = await _Host.EncounterAsync(value, cancellationToken).ConfigureAwait(false);
        if (!encountered.IsSuccess)
        {
            return InteractionResult.Failure<CollectionEntry>(
                encountered.Error!.Code,
                encountered.Error.Message,
                encountered.Error.Issues);
        }

        var domainIdentity = await _Host.GetDomainIdentityAsync(
            encountered.Value,
            cancellationToken).ConfigureAwait(false);
        if (!domainIdentity.IsSuccess)
        {
            return InteractionResult.Failure<CollectionEntry>(
                domainIdentity.Error!.Code,
                domainIdentity.Error.Message,
                domainIdentity.Error.Issues);
        }

        return InteractionResult.Success(CollectionEntry.ReferenceValue(
            position,
            encountered.Value,
            domainIdentity.Value,
            key));
    }
}

/// <summary>Binds named arguments and invokes one exposed synchronous action.</summary>
internal sealed class ReflectionActionDescriptor : ReflectionMemberDescriptor, IActionDescriptor
{
    private readonly ActionAttribute _ActionMetadata;
    private readonly bool _HasInvalidPrecondition;
    private readonly MethodInfo _Method;
    private readonly MethodInfo? _Precondition;

    public ReflectionActionDescriptor(
        UIEngineHost host,
        object target,
        ReflectionMemberMetadata metadata)
        : base(host, target, metadata)
    {
        _Method = (MethodInfo)metadata.Member;
        _ActionMetadata = _Method.GetCustomAttribute<ActionAttribute>(inherit: true) ?? new ActionAttribute();
        _Precondition = _ResolvePrecondition(_Method, _ActionMetadata.Precondition);
        _HasInvalidPrecondition = _ActionMetadata.Precondition is not null && _Precondition is null;
        Parameters = _Method
            .GetParameters()
            .Select(static parameter => (IParameterDescriptor)new ReflectionParameterDescriptor(parameter))
            .ToArray();
    }

    public IReadOnlyList<IParameterDescriptor> Parameters { get; }

    public ActionRisk Risk => _ActionMetadata.Risk;

    public bool RequiresConfirmation => _ActionMetadata.RequiresConfirmation;

    public ValueTask<InteractionResult<object?>> InvokeAsync(
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        if (IsHostDisposed)
        {
            return ValueTask.FromResult(_DisposedFailure<object?>());
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return ValueTask.FromResult(InteractionResult.Failure<object?>(
                InteractionErrorCode.CANCELLED,
                "Action invocation was cancelled."));
        }

        if (_Method.ContainsGenericParameters ||
            _Method.GetParameters().Any(static parameter => parameter.ParameterType.IsByRef) ||
            typeof(Task).IsAssignableFrom(_Method.ReturnType) ||
            _Method.ReturnType == typeof(ValueTask) ||
            _Method.ReturnType.IsGenericType &&
            _Method.ReturnType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            return ValueTask.FromResult(InteractionResult.Failure<object?>(
                InteractionErrorCode.DESCRIPTOR_UNAVAILABLE,
                $"Action '{Id}' is not a supported synchronous method."));
        }

        if (_HasInvalidPrecondition)
        {
            return ValueTask.FromResult(InteractionResult.Failure<object?>(
                InteractionErrorCode.DESCRIPTOR_UNAVAILABLE,
                $"Action '{Id}' has an invalid precondition declaration."));
        }

        if (!TryGetTarget(out var target) || target is null)
        {
            return ValueTask.FromResult(InteractionResult.Failure<object?>(
                InteractionErrorCode.TARGET_UNAVAILABLE,
                "The containing object is no longer available."));
        }

        var methodParameters = _Method.GetParameters();
        var unknownArgument = arguments.Keys.FirstOrDefault(argumentName =>
            !methodParameters.Any(parameter =>
                string.Equals(parameter.Name, argumentName, StringComparison.Ordinal)));
        if (unknownArgument is not null)
        {
            var message = $"Action '{Id}' has no parameter named '{unknownArgument}'.";
            return ValueTask.FromResult(InteractionResult.Failure<object?>(
                InteractionErrorCode.INVALID_INPUT,
                message,
                [new InteractionIssue(
                    InteractionIssueCode.ACTION_REJECTED,
                    InteractionIssueTarget.PARAMETER,
                    unknownArgument,
                    message)]));
        }

        var boundArguments = new object?[methodParameters.Length];
        for (var index = 0; index < methodParameters.Length; index++)
        {
            var parameter = methodParameters[index];
            var suppliedArgument = arguments.FirstOrDefault(argument =>
                string.Equals(argument.Key, parameter.Name, StringComparison.Ordinal));
            var wasSupplied = suppliedArgument.Key is not null;
            if (!wasSupplied)
            {
                if (!parameter.IsOptional)
                {
                    var message = $"Required parameter '{parameter.Name}' was not supplied.";
                    return ValueTask.FromResult(InteractionResult.Failure<object?>(
                        InteractionErrorCode.INVALID_INPUT,
                        message,
                        [new InteractionIssue(
                            InteractionIssueCode.REQUIRED,
                            InteractionIssueTarget.PARAMETER,
                            parameter.Name ?? $"arg{parameter.Position}",
                            message)]));
                }

                boundArguments[index] = parameter.DefaultValue;
                continue;
            }

            var converted = ReflectionValueConverter.Convert(
                suppliedArgument.Value,
                parameter.ParameterType);
            if (!converted.IsSuccess)
            {
                var error = converted.Error ?? new InteractionError(
                    InteractionErrorCode.CONVERSION_FAILED,
                    "The argument could not be converted.");
                return ValueTask.FromResult(InteractionResult.Failure<object?>(
                    error.Code,
                    $"Parameter '{parameter.Name}': {error.Message}",
                    [new InteractionIssue(
                        error.Code == InteractionErrorCode.VALIDATION_FAILED
                            ? InteractionIssueCode.NULL_NOT_ALLOWED
                            : InteractionIssueCode.CONVERSION_FAILED,
                        InteractionIssueTarget.PARAMETER,
                        parameter.Name ?? $"arg{parameter.Position}",
                        error.Message)]));
            }

            boundArguments[index] = converted.Value;
        }

        for (var index = 0; index < Parameters.Count; index++)
        {
            var parameter = (ReflectionParameterDescriptor)Parameters[index];
            var issues = parameter.Validate(boundArguments[index], target);
            if (issues.Count > 0)
            {
                return ValueTask.FromResult(InteractionResult.Failure<object?>(
                    InteractionErrorCode.VALIDATION_FAILED,
                    $"Parameter '{parameter.Id}' failed validation.",
                    issues));
            }
        }

        if (_Precondition is not null)
        {
            try
            {
                if (_Precondition.Invoke(target, null) is not true)
                {
                    var message = $"Action '{Id}' is not currently available.";
                    return ValueTask.FromResult(InteractionResult.Failure<object?>(
                        InteractionErrorCode.VALIDATION_FAILED,
                        message,
                        [new InteractionIssue(
                            InteractionIssueCode.ACTION_REJECTED,
                            InteractionIssueTarget.ACTION,
                            Id,
                            message)]));
                }
            }
            catch (TargetInvocationException exception)
            {
                return ValueTask.FromResult(_InvocationFailure(exception.InnerException ?? exception));
            }
        }

        try
        {
            return ValueTask.FromResult(InteractionResult.Success(
                _Method.Invoke(target, boundArguments)));
        }
        catch (TargetInvocationException exception)
        {
            return ValueTask.FromResult(_InvocationFailure(exception.InnerException ?? exception));
        }
        catch (ArgumentException exception)
        {
            return ValueTask.FromResult(InteractionResult.Failure<object?>(
                InteractionErrorCode.INVALID_INPUT,
                exception.Message,
                [new InteractionIssue(
                    InteractionIssueCode.ACTION_REJECTED,
                    InteractionIssueTarget.ACTION,
                    Id,
                    exception.Message)]));
        }
    }

    private InteractionResult<object?> _InvocationFailure(Exception exception)
    {
        if (exception is UnauthorizedAccessException)
        {
            return InteractionResult.Failure<object?>(
                InteractionErrorCode.PERMISSION_DENIED,
                exception.Message,
                [new InteractionIssue(
                    InteractionIssueCode.PERMISSION_DENIED,
                    InteractionIssueTarget.PERMISSION,
                    Id,
                    exception.Message)]);
        }

        return InteractionResult.Failure<object?>(
            InteractionErrorCode.INVOCATION_FAILED,
            exception.Message,
            [new InteractionIssue(
                InteractionIssueCode.ACTION_REJECTED,
                InteractionIssueTarget.ACTION,
                Id,
                exception.Message)]);
    }

    private static MethodInfo? _ResolvePrecondition(MethodInfo action, string? preconditionName)
    {
        if (preconditionName is null)
        {
            return null;
        }

        var precondition = action.DeclaringType?.GetMethod(
            preconditionName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);
        return precondition?.ReturnType == typeof(bool) ? precondition : null;
    }
}

/// <summary>Describes one reflected action parameter.</summary>
internal sealed class ReflectionParameterDescriptor : IParameterDescriptor
{
    private readonly IReadOnlyList<System.ComponentModel.DataAnnotations.ValidationAttribute> _ValidationAttributes;

    public ReflectionParameterDescriptor(ParameterInfo parameter)
    {
        Id = parameter.Name ?? $"arg{parameter.Position}";
        DisplayName = Id;
        ParameterType = parameter.ParameterType;
        IsRequired = !parameter.IsOptional;
        IsNullable = ReflectionValidationMetadata.IsNullable(parameter);
        _ValidationAttributes = ReflectionValidationMetadata.GetAttributes(parameter);
        if (_ValidationAttributes.Any(static attribute =>
                attribute is System.ComponentModel.DataAnnotations.RequiredAttribute))
        {
            IsNullable = false;
        }

        HasDefaultValue = parameter.HasDefaultValue;
        DefaultValue = parameter.HasDefaultValue ? parameter.DefaultValue : null;
        Options = ValueValidation.GetEnumOptions(ParameterType);
        Range = ReflectionValidationMetadata.GetRange(_ValidationAttributes);
        ValidationRules = ReflectionValidationMetadata.GetRules(
            _ValidationAttributes,
            Options.Count > 0);
        var interactionMetadata = parameter.GetCustomAttribute<InteractionMetadataAttribute>(inherit: true);
        Unit = interactionMetadata?.Unit;
        Tags = interactionMetadata?.Tags.ToArray() ?? [];
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

    internal IReadOnlyList<InteractionIssue> Validate(object? value, object target) =>
        ValueValidation.Validate(
            value,
            IsNullable,
            Options,
            range: null,
            _ValidationAttributes,
            target,
            InteractionIssueTarget.PARAMETER,
            Id);
}

/// <summary>Reads reflected properties, fields, and parameterless summary methods.</summary>
internal static class ReflectionMemberAccess
{
    public static bool CanRead(MemberInfo member) => member switch
    {
        PropertyInfo property => property.GetMethod?.IsPublic == true &&
            property.GetIndexParameters().Length == 0,
        FieldInfo => true,
        _ => false,
    };

    public static bool CanWrite(MemberInfo member) => member switch
    {
        PropertyInfo property => property.SetMethod?.IsPublic == true &&
            property.GetIndexParameters().Length == 0 &&
            !property.SetMethod.ReturnParameter
                .GetRequiredCustomModifiers()
                .Contains(typeof(IsExternalInit)),
        FieldInfo field => !field.IsInitOnly && !field.IsLiteral,
        _ => false,
    };

    public static object? Read(MemberInfo member, object instance) => member switch
    {
        PropertyInfo property when property.GetIndexParameters().Length == 0 => property.GetValue(instance),
        FieldInfo field => field.GetValue(instance),
        MethodInfo method when method.GetParameters().Length == 0 => method.Invoke(instance, null),
        _ => throw new InvalidOperationException($"Member '{member.Name}' cannot be read without arguments."),
    };

    public static void Write(MemberInfo member, object instance, object? value)
    {
        switch (member)
        {
            case PropertyInfo property when CanWrite(property):
                property.SetValue(instance, value);
                break;
            case FieldInfo field when CanWrite(field):
                field.SetValue(instance, value);
                break;
            default:
                throw new InvalidOperationException($"Member '{member.Name}' is read-only.");
        }
    }
}

/// <summary>Provides reflection type-shape helpers shared by descriptor implementations.</summary>
internal static class ReflectionTypeClassifier
{
    public static Type GetCollectionElementType(Type collectionType)
    {
        var dictionaryType = _FindGenericShape(
            collectionType,
            typeof(IDictionary<,>),
            typeof(IReadOnlyDictionary<,>));
        if (dictionaryType is not null)
        {
            return dictionaryType.GetGenericArguments()[1];
        }

        if (collectionType.IsArray)
        {
            return collectionType.GetElementType() ?? typeof(object);
        }

        var enumerableType = collectionType
            .GetInterfaces()
            .Append(collectionType)
            .Where(static type => type.IsGenericType)
            .FirstOrDefault(static type => type.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return enumerableType?.GetGenericArguments()[0] ?? typeof(object);
    }

    public static Type? GetDictionaryKeyType(Type collectionType)
    {
        if (typeof(IDictionary).IsAssignableFrom(collectionType))
        {
            var genericDictionary = _FindGenericShape(
                collectionType,
                typeof(IDictionary<,>),
                typeof(IReadOnlyDictionary<,>));
            return genericDictionary?.GetGenericArguments()[0] ?? typeof(object);
        }

        return _FindGenericShape(
            collectionType,
            typeof(IDictionary<,>),
            typeof(IReadOnlyDictionary<,>))?.GetGenericArguments()[0];
    }

    public static CollectionCapabilities GetCollectionCapabilities(Type collectionType)
    {
        var capabilities = CollectionCapabilities.NONE;
        if (_IsFiniteCollection(collectionType))
        {
            capabilities |= CollectionCapabilities.FINITE_SNAPSHOT;
        }

        if (_IsIndexedCollection(collectionType))
        {
            capabilities |= CollectionCapabilities.INDEXED |
                CollectionCapabilities.VIRTUALIZED_RANGE;
        }

        if (GetDictionaryKeyType(collectionType) is not null)
        {
            capabilities |= CollectionCapabilities.KEYED;
        }

        if (typeof(INotifyCollectionChanged).IsAssignableFrom(collectionType))
        {
            capabilities |= CollectionCapabilities.LIVE_OBSERVATION;
        }

        return capabilities;
    }

    public static bool IsScalar(Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        return underlyingType.IsValueType || underlyingType == typeof(string);
    }

    public static bool TryGetCollectionCount(IEnumerable collection, out int count)
    {
        if (collection is ICollection nonGenericCollection)
        {
            count = nonGenericCollection.Count;
            return true;
        }

        var collectionInterface = _FindGenericShape(
            collection.GetType(),
            typeof(ICollection<>),
            typeof(IReadOnlyCollection<>));
        if (collectionInterface?.GetProperty(nameof(ICollection<object>.Count))?.GetValue(collection) is int value)
        {
            count = value;
            return true;
        }

        count = 0;
        return false;
    }

    public static bool TryGetIndexedCollection(
        IEnumerable collection,
        out int count,
        out Func<int, object?> readAt)
    {
        if (collection is IList list)
        {
            count = list.Count;
            readAt = index => list[index];
            return true;
        }

        var listInterface = _FindGenericShape(
            collection.GetType(),
            typeof(IList<>),
            typeof(IReadOnlyList<>));
        var itemProperty = listInterface?.GetProperty("Item");
        if (itemProperty is not null && TryGetCollectionCount(collection, out var value))
        {
            count = value;
            readAt = index => itemProperty.GetValue(collection, [index]);
            return true;
        }

        count = 0;
        readAt = static _ => null;
        return false;
    }

    public static (bool HasKey, object? Key, object? Value) SplitDictionaryEntry(
        IEnumerable collection,
        object? entry)
    {
        if (GetDictionaryKeyType(collection.GetType()) is null)
        {
            return (false, null, entry);
        }

        if (entry is DictionaryEntry dictionaryEntry)
        {
            return (true, dictionaryEntry.Key, dictionaryEntry.Value);
        }

        if (entry is not null)
        {
            var type = entry.GetType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                return (
                    true,
                    type.GetProperty(nameof(KeyValuePair<object, object>.Key))!.GetValue(entry),
                    type.GetProperty(nameof(KeyValuePair<object, object>.Value))!.GetValue(entry));
            }
        }

        return (false, null, entry);
    }

    private static bool _IsFiniteCollection(Type collectionType) =>
        typeof(ICollection).IsAssignableFrom(collectionType) ||
        _FindGenericShape(
            collectionType,
            typeof(ICollection<>),
            typeof(IReadOnlyCollection<>)) is not null;

    private static bool _IsIndexedCollection(Type collectionType) =>
        typeof(IList).IsAssignableFrom(collectionType) ||
        _FindGenericShape(
            collectionType,
            typeof(IList<>),
            typeof(IReadOnlyList<>)) is not null;

    private static Type? _FindGenericShape(Type type, params Type[] genericDefinitions) =>
        type.GetInterfaces()
            .Append(type)
            .Where(static candidate => candidate.IsGenericType)
            .FirstOrDefault(candidate => genericDefinitions.Contains(candidate.GetGenericTypeDefinition()));
}
