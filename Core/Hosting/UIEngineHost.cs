using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using UIEngine.Core.Attributes;

namespace UIEngine.Core;

/// <summary>Owns roots, runtime identity, dispatch, observation, and action lifetime.</summary>
public sealed class UIEngineHost : IDisposable
{
    private readonly object _Gate = new();
    private readonly ConditionalWeakTable<object, _HandleHolder> _Handles = new();
    private readonly Dictionary<ObjectHandle, WeakReference<object>> _Objects = [];
    private readonly Dictionary<string, _RootEntry> _Roots = new(StringComparer.Ordinal);
    private readonly Dictionary<ObjectHandle, DomainIdentity?> _DomainIdentities = [];
    private readonly Dictionary<DomainIdentity, HashSet<ObjectHandle>> _DomainIdentityIndex = [];
    private readonly Dictionary<ObjectHandle, LogicalPath> _CanonicalPaths = [];
    private readonly HashSet<ObservationSubscription> _Subscriptions = [];
    private readonly HashSet<ActionInvocation> _Invocations = [];
    private readonly HostSettings _Settings;
    private readonly ILogger _Logger;
    private int _Disposed;

    public UIEngineHost(UIEngineHostOptions? options = null)
    {
        _Settings = new HostSettings(options ?? new UIEngineHostOptions());
        _Logger = _Settings.LoggerFactory.CreateLogger<UIEngineHost>();
    }

    public bool IsDisposed => Volatile.Read(ref _Disposed) != 0;

    public IReadOnlyList<RootRegistration> Roots
    {
        get
        {
            lock (_Gate)
            {
                return _Roots.Values.Select(static root => root.Registration).ToArray();
            }
        }
    }

    internal int MaxCollectionItems => _Settings.MaxCollectionItems;

    public InteractionResult<ObjectHandle> SetRoot(string identifier, object instance)
    {
        if (IsDisposed)
        {
            return _DisposedFailure<ObjectHandle>();
        }

        if (string.IsNullOrWhiteSpace(identifier))
        {
            return InteractionResult.Failure<ObjectHandle>(
                InteractionErrorCode.INVALID_INPUT,
                "A root identifier cannot be empty or whitespace.");
        }

        if (instance.GetType().IsValueType)
        {
            return InteractionResult.Failure<ObjectHandle>(
                InteractionErrorCode.TYPE_MISMATCH,
                "A root must be a reference type.");
        }

        lock (_Gate)
        {
            if (IsDisposed)
            {
                return _DisposedFailure<ObjectHandle>();
            }

            var handle = _GetOrCreateHandle(instance);
            var registration = new RootRegistration(identifier, handle);
            _Roots[identifier] = new _RootEntry(registration, instance);
            _CanonicalPaths[handle] = LogicalPath.Root.Append(identifier);
            return InteractionResult.Success(handle);
        }
    }

    public InteractionResult<RootRegistration> RemoveRoot(string identifier)
    {
        if (IsDisposed)
        {
            return _DisposedFailure<RootRegistration>();
        }

        if (string.IsNullOrWhiteSpace(identifier))
        {
            return InteractionResult.Failure<RootRegistration>(
                InteractionErrorCode.INVALID_INPUT,
                "A root identifier cannot be empty or whitespace.");
        }

        lock (_Gate)
        {
            return _Roots.Remove(identifier, out var root)
                ? InteractionResult.Success(root.Registration)
                : InteractionResult.Failure<RootRegistration>(
                    InteractionErrorCode.NOT_FOUND,
                    $"Root '{identifier}' was not found.");
        }
    }

    public ObjectHandle GetOrCreateHandle(object instance)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (instance.GetType().IsValueType)
        {
            throw new ArgumentException("Runtime handles require reference types.", nameof(instance));
        }

        lock (_Gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return _GetOrCreateHandle(instance);
        }
    }

    public async Task<InteractionResult<DomainIdentity?>> GetDomainIdentityAsync(
        ObjectHandle handle,
        CancellationToken cancellationToken = default)
    {
        if (IsDisposed)
        {
            return _DisposedFailure<DomainIdentity?>();
        }

        lock (_Gate)
        {
            if (_DomainIdentities.TryGetValue(handle, out var cached))
            {
                return InteractionResult.Success(cached);
            }
        }

        var target = ResolveTarget(handle);
        if (!target.IsSuccess)
        {
            return InteractionResult.Failure<DomainIdentity?>(target.Error!);
        }

        var discovered = await ExecuteAsync(
            "discover domain identity",
            () => Task.FromResult(_DiscoverDomainIdentity(target.Value)),
            cancellationToken);
        if (!discovered.IsSuccess)
        {
            return discovered;
        }

        lock (_Gate)
        {
            if (_DomainIdentities.TryGetValue(handle, out var cached))
            {
                return InteractionResult.Success(cached);
            }

            _DomainIdentities[handle] = discovered.Value;
            if (discovered.Value is { } identity)
            {
                if (!_DomainIdentityIndex.TryGetValue(identity, out var handles))
                {
                    handles = [];
                    _DomainIdentityIndex.Add(identity, handles);
                }

                handles.Add(handle);
            }
        }

        return discovered;
    }

    public InteractionResult<ObjectHandle> ResolveDomainIdentity(DomainIdentity identity)
    {
        if (IsDisposed)
        {
            return _DisposedFailure<ObjectHandle>();
        }

        lock (_Gate)
        {
            if (!_DomainIdentityIndex.TryGetValue(identity, out var handles))
            {
                return _DomainIdentityNotFound(identity);
            }

            handles.RemoveWhere(handle =>
                !_Objects.TryGetValue(handle, out var reference) || !reference.TryGetTarget(out _));
            if (handles.Count == 0)
            {
                _DomainIdentityIndex.Remove(identity);
                return _DomainIdentityNotFound(identity);
            }

            return handles.Count == 1
                ? InteractionResult.Success(handles.Single())
                : InteractionResult.Failure<ObjectHandle>(
                    InteractionErrorCode.AMBIGUOUS,
                    $"Domain identity '{identity}' matches multiple live objects.");
        }
    }

    public async Task<InteractionResult<ObjectDescriptor>> DescribeAsync(
        ObjectHandle handle,
        CancellationToken cancellationToken = default)
    {
        var target = ResolveTarget(handle);
        if (!target.IsSuccess)
        {
            return InteractionResult.Failure<ObjectDescriptor>(target.Error!);
        }

        var identity = await GetDomainIdentityAsync(handle, cancellationToken);
        if (!identity.IsSuccess)
        {
            return InteractionResult.Failure<ObjectDescriptor>(identity.Error!);
        }

        return await ExecuteAsync(
            "describe object",
            () => Task.FromResult(_CreateDescriptor(target.Value, handle, identity.Value)),
            cancellationToken);
    }

    public Task<InteractionResult<ResolvedPath>> ResolvePathAsync(
        string path,
        CancellationToken cancellationToken = default)
    {
        var parsed = LogicalPath.Parse(path);
        return parsed.IsSuccess
            ? ResolvePathAsync(parsed.Value, cancellationToken)
            : Task.FromResult(InteractionResult.Failure<ResolvedPath>(parsed.Error!));
    }

    public Task<InteractionResult<ResolvedPath>> ResolvePathAsync(
        LogicalPath path,
        CancellationToken cancellationToken = default) => ExecuteAsync(
        "resolve path",
        () => PathResolution.ResolveAsync(this, path, cancellationToken),
        cancellationToken);

    public async Task<InteractionResult<ResolvedBinding>> ResolveBindingAsync(
        BindingReference binding,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(binding.Path) ||
            string.IsNullOrWhiteSpace(binding.MemberId))
        {
            return InteractionResult.Failure<ResolvedBinding>(
                InteractionErrorCode.INVALID_INPUT,
                "A binding requires a path and member identifier.");
        }

        var parsed = LogicalPath.Parse(binding.Path);
        if (!parsed.IsSuccess)
        {
            return InteractionResult.Failure<ResolvedBinding>(parsed.Error!);
        }

        ObjectHandle? identityHandle = null;
        ObjectDescriptor? identityDescriptor = null;
        LogicalPath? identityPath = null;
        if (binding.DomainIdentity is not null)
        {
            DomainIdentity identity;
            try
            {
                identity = new DomainIdentity(binding.DomainIdentity);
            }
            catch (ArgumentException exception)
            {
                return InteractionResult.Failure<ResolvedBinding>(
                    InteractionErrorCode.INVALID_INPUT,
                    exception.Message);
            }

            var resolvedIdentity = ResolveDomainIdentity(identity);
            if (resolvedIdentity.IsSuccess)
            {
                identityHandle = resolvedIdentity.Value;
                var described = await DescribeAsync(resolvedIdentity.Value, cancellationToken);
                if (!described.IsSuccess)
                {
                    return InteractionResult.Failure<ResolvedBinding>(described.Error!);
                }

                identityDescriptor = described.Value;
                TryGetCanonicalPath(resolvedIdentity.Value, out identityPath);
            }
            else if (resolvedIdentity.Error!.Code is not (
                InteractionErrorCode.NOT_FOUND or InteractionErrorCode.AMBIGUOUS))
            {
                return InteractionResult.Failure<ResolvedBinding>(resolvedIdentity.Error);
            }
        }

        var resolvedPath = await ResolvePathAsync(parsed.Value, cancellationToken);
        ObjectHandle owner;
        ObjectDescriptor descriptor;
        LogicalPath canonical;
        if (resolvedPath.IsSuccess && resolvedPath.Value.IsObject)
        {
            if (binding.DomainIdentity is not null && !StringComparer.Ordinal.Equals(
                    resolvedPath.Value.OwnerDescriptor.DomainIdentity?.Value,
                    binding.DomainIdentity))
            {
                return InteractionResult.Failure<ResolvedBinding>(
                    InteractionErrorCode.NOT_FOUND,
                    "The binding path resolves to a conflicting domain identity.");
            }

            owner = resolvedPath.Value.OwnerHandle;
            descriptor = resolvedPath.Value.OwnerDescriptor;
            canonical = resolvedPath.Value.CanonicalPath;
        }
        else if (identityHandle is not null && identityDescriptor is not null && identityPath is not null)
        {
            owner = identityHandle.Value;
            descriptor = identityDescriptor;
            canonical = identityPath;
        }
        else if (!resolvedPath.IsSuccess)
        {
            return InteractionResult.Failure<ResolvedBinding>(resolvedPath.Error!);
        }
        else
        {
            return InteractionResult.Failure<ResolvedBinding>(
                InteractionErrorCode.NOT_FOUND,
                "The binding path no longer identifies the expected domain object.");
        }

        var members = descriptor.Members
            .Where(member => StringComparer.Ordinal.Equals(member.Id, binding.MemberId))
            .ToArray();
        if (members.Length == 0)
        {
            return InteractionResult.Failure<ResolvedBinding>(
                InteractionErrorCode.NOT_FOUND,
                $"Member '{binding.MemberId}' was not found.");
        }

        if (members.Length > 1)
        {
            return InteractionResult.Failure<ResolvedBinding>(
                InteractionErrorCode.AMBIGUOUS,
                $"Member '{binding.MemberId}' is ambiguous.");
        }

        if (members[0].Kind != binding.ExpectedKind)
        {
            return InteractionResult.Failure<ResolvedBinding>(
                InteractionErrorCode.TYPE_MISMATCH,
                $"Member '{binding.MemberId}' is {members[0].Kind}, not {binding.ExpectedKind}.");
        }

        return InteractionResult.Success(new ResolvedBinding(
            owner,
            descriptor,
            members[0],
            canonical));
    }

    public async Task<InteractionResult<ObservationSubscription>> ObserveAsync(
        ObjectHandle handle,
        string? memberId = null,
        TimeSpan? pollingInterval = null,
        CancellationToken cancellationToken = default)
    {
        if (pollingInterval is { } interval && interval <= TimeSpan.Zero)
        {
            return InteractionResult.Failure<ObservationSubscription>(
                InteractionErrorCode.INVALID_INPUT,
                "A polling interval must be positive.");
        }

        var target = ResolveTarget(handle);
        if (!target.IsSuccess)
        {
            return InteractionResult.Failure<ObservationSubscription>(target.Error!);
        }

        var described = await DescribeAsync(handle, cancellationToken);
        if (!described.IsSuccess)
        {
            return InteractionResult.Failure<ObservationSubscription>(described.Error!);
        }

        MemberDescriptor? member = null;
        if (memberId is not null)
        {
            member = described.Value.Members.FirstOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.Id, memberId));
            if (member is null)
            {
                return InteractionResult.Failure<ObservationSubscription>(
                    InteractionErrorCode.NOT_FOUND,
                    $"Member '{memberId}' was not found.");
            }

            if (member is ActionDescriptor)
            {
                return InteractionResult.Failure<ObservationSubscription>(
                    InteractionErrorCode.UNSUPPORTED,
                    "Actions cannot be observed.");
            }
        }

        if (pollingInterval is not null && member is not ValueDescriptor and not ReferenceDescriptor)
        {
            return InteractionResult.Failure<ObservationSubscription>(
                InteractionErrorCode.UNSUPPORTED,
                "Polling requires one value or reference member.");
        }

        var identity = described.Value.DomainIdentity;
        long ordering = 0;
        ChangeRecord CreateChange(
            string? changedMember,
            ChangeKind kind,
            ObservationValue oldValue,
            ObservationValue newValue,
            int? oldIndex,
            int? newIndex) => new(
                handle,
                identity,
                changedMember,
                kind,
                oldValue,
                newValue,
                Interlocked.Increment(ref ordering))
            {
                OldIndex = oldIndex,
                NewIndex = newIndex,
            };

        var subscription = new ObservationSubscription(
            handle,
            memberId,
            _Settings.ObservationBufferCapacity,
            dropped => CreateChange(
                memberId,
                ChangeKind.BUFFER_OVERFLOW,
                ObservationValue.NotSupplied,
                ObservationValue.NotSupplied,
                null,
                null) with { DroppedChangeCount = dropped },
            _UntrackSubscription,
            exception => _ReportUnexpected("observation", exception));

        IDisposable sourceSubscription;
        if (pollingInterval is { } polling)
        {
            var initial = await _ReadObservableAsync(member!, cancellationToken);
            if (!initial.IsSuccess)
            {
                subscription.Dispose();
                return InteractionResult.Failure<ObservationSubscription>(initial.Error!);
            }

            sourceSubscription = new PollingObserver(
                initial.Value,
                polling,
                token => _ReadObservableAsync(member!, token),
                (oldValue, newValue) => subscription.Publish(CreateChange(
                    memberId,
                    ChangeKind.MEMBER_CHANGED,
                    ObservationValue.Supplied(oldValue),
                    ObservationValue.Supplied(newValue),
                    null,
                    null)),
                exception => _ReportUnexpected("observation polling", exception));
        }
        else
        {
            var source = await NotificationObserver.CreateAsync(
                target.Value,
                memberId,
                described.Value,
                member,
                (changedMember, kind, oldValue, newValue, oldIndex, newIndex) =>
                    subscription.Publish(CreateChange(
                        changedMember,
                        kind,
                        oldValue,
                        newValue,
                        oldIndex,
                        newIndex)),
                exception => _ReportUnexpected("observation notification", exception),
                cancellationToken);
            if (!source.IsSuccess)
            {
                subscription.Dispose();
                return InteractionResult.Failure<ObservationSubscription>(source.Error!);
            }

            sourceSubscription = source.Value;
        }

        subscription.SetSourceSubscription(sourceSubscription);
        lock (_Gate)
        {
            if (IsDisposed)
            {
                subscription.Dispose();
                return _DisposedFailure<ObservationSubscription>();
            }

            _Subscriptions.Add(subscription);
        }

        return InteractionResult.Success(subscription);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _Disposed, 1) != 0)
        {
            return;
        }

        ObservationSubscription[] subscriptions;
        ActionInvocation[] invocations;
        lock (_Gate)
        {
            subscriptions = _Subscriptions.ToArray();
            invocations = _Invocations.ToArray();
            _Subscriptions.Clear();
            _Invocations.Clear();
            _Roots.Clear();
            _Objects.Clear();
            _DomainIdentities.Clear();
            _DomainIdentityIndex.Clear();
            _CanonicalPaths.Clear();
            _Handles.Clear();
        }

        foreach (var subscription in subscriptions)
        {
            subscription.Dispose();
        }

        foreach (var invocation in invocations)
        {
            try
            {
                invocation.CompleteHostDisposed();
            }
            catch (AggregateException exception)
            {
                _ReportUnexpected("invocation cancellation", exception);
            }
        }
    }

    internal InteractionResult<object> ResolveTarget(ObjectHandle handle)
    {
        if (IsDisposed)
        {
            return _DisposedFailure<object>();
        }

        lock (_Gate)
        {
            if (_Objects.TryGetValue(handle, out var reference) && reference.TryGetTarget(out var target))
            {
                return InteractionResult.Success(target);
            }

            _Objects.Remove(handle);
            return InteractionResult.Failure<object>(
                InteractionErrorCode.UNAVAILABLE,
                "The target object is no longer available.");
        }
    }

    internal async Task<InteractionResult<T>> ExecuteAsync<T>(
        string operation,
        Func<Task<InteractionResult<T>>> action,
        CancellationToken cancellationToken)
    {
        if (IsDisposed)
        {
            return _DisposedFailure<T>();
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return InteractionResult.Failure<T>(
                InteractionErrorCode.CANCELLED,
                $"The {operation} operation was cancelled.");
        }

        try
        {
            if (_Settings.Dispatcher.CheckAccess())
            {
                return await _ExecuteCheckedAsync(action, cancellationToken);
            }

            return await _Settings.Dispatcher.InvokeAsync(
                () => _ExecuteCheckedAsync(action, cancellationToken),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return InteractionResult.Failure<T>(
                InteractionErrorCode.CANCELLED,
                $"The {operation} operation was cancelled.");
        }
        catch (ObjectDisposedException) when (IsDisposed)
        {
            return _DisposedFailure<T>();
        }
        catch (UnauthorizedAccessException exception)
        {
            return InteractionResult.Failure<T>(
                InteractionErrorCode.PERMISSION_DENIED,
                exception.Message);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            return _UnexpectedFailure<T>(operation, exception.InnerException);
        }
        catch (Exception exception)
        {
            return _UnexpectedFailure<T>(operation, exception);
        }
    }

    internal ActionInvocation CreateInvocation(string actionId, bool supportsCancellation)
    {
        var invocation = new ActionInvocation(
            actionId,
            supportsCancellation,
            _Settings.InvocationProgressBufferCapacity,
            _InvocationCompleted);
        lock (_Gate)
        {
            if (IsDisposed)
            {
                invocation.CompleteHostDisposed();
                return invocation;
            }

            _Invocations.Add(invocation);
        }

        return invocation;
    }

    internal void RecordCanonicalPath(ObjectHandle handle, LogicalPath path)
    {
        lock (_Gate)
        {
            if (!IsDisposed)
            {
                _CanonicalPaths[handle] = path;
            }
        }
    }

    internal bool TryGetCanonicalPath(ObjectHandle handle, out LogicalPath? path)
    {
        lock (_Gate)
        {
            return _CanonicalPaths.TryGetValue(handle, out path);
        }
    }

    private ObjectHandle _GetOrCreateHandle(object instance)
    {
        if (_Handles.TryGetValue(instance, out var existing))
        {
            return existing.Handle;
        }

        var handle = new ObjectHandle(Guid.NewGuid());
        _Handles.Add(instance, new _HandleHolder(handle));
        _Objects.Add(handle, new WeakReference<object>(instance));
        return handle;
    }

    private InteractionResult<DomainIdentity?> _DiscoverDomainIdentity(object instance)
    {
        if (_Settings.Exposures.TryGetValue(instance.GetType(), out var exposure))
        {
            return _NormalizeIdentity(exposure.GetDomainIdentity(instance));
        }

        var metadata = ReflectionMetadata.Get(instance.GetType());
        string? attributed = null;
        if (metadata.DomainIdentityMember is { } member)
        {
            if (member is PropertyInfo { PropertyType: var propertyType } property &&
                propertyType == typeof(string) && ReflectionMetadata.CanRead(property))
            {
                attributed = (string?)ReflectionMetadata.Read(property, instance);
            }
            else if (member is FieldInfo { FieldType: var fieldType } field &&
                fieldType == typeof(string))
            {
                attributed = (string?)ReflectionMetadata.Read(field, instance);
            }
            else
            {
                return InteractionResult.Failure<DomainIdentity?>(
                    InteractionErrorCode.INVALID_INPUT,
                    "A domain identity member must be a readable string field or property.");
            }
        }

        var fromInterface = (instance as IStableDomainIdentity)?.DomainIdentity;
        if (fromInterface is not null && attributed is not null &&
            !StringComparer.Ordinal.Equals(fromInterface, attributed))
        {
            return InteractionResult.Failure<DomainIdentity?>(
                InteractionErrorCode.AMBIGUOUS,
                "The object supplies conflicting domain identities.");
        }

        return _NormalizeIdentity(fromInterface ?? attributed);
    }

    private InteractionResult<ObjectDescriptor> _CreateDescriptor(
        object instance,
        ObjectHandle handle,
        DomainIdentity? identity)
    {
        var metadata = ReflectionMetadata.Get(instance.GetType());
        _Settings.Exposures.TryGetValue(instance.GetType(), out var exposure);
        var programmaticIds = exposure?.Values.Select(static value => value.Id)
            .ToHashSet(StringComparer.Ordinal) ?? [];
        var members = new List<MemberDescriptor>();
        foreach (var member in metadata.Members.Where(member => !programmaticIds.Contains(member.Id)))
        {
            switch (member.Kind)
            {
                case MemberKind.VALUE:
                    members.Add(new ValueDescriptor(
                        this,
                        handle,
                        member.Id,
                        member.ValueType,
                        ReflectionMetadata.CanRead(member.Member),
                        ReflectionMetadata.CanWrite(member.Member) &&
                            member.Member.GetCustomAttribute<ExposeAttribute>(inherit: true)?.ReadOnly != true,
                        member.IsNullable,
                        member.Options,
                        member.Range,
                        member.ValidationAttributes,
                        target => ReflectionMetadata.Read(member.Member, target),
                        ReflectionMetadata.CanWrite(member.Member)
                            ? (target, value) => ReflectionMetadata.Write(member.Member, target, value)
                            : null));
                    break;
                case MemberKind.REFERENCE:
                    members.Add(new ReferenceDescriptor(
                        this,
                        handle,
                        member.Id,
                        member.ValueType,
                        target => ReflectionMetadata.Read(member.Member, target)));
                    break;
                case MemberKind.COLLECTION:
                    members.Add(new CollectionDescriptor(
                        this,
                        handle,
                        member.Id,
                        member.ValueType,
                        target => ReflectionMetadata.Read(member.Member, target)));
                    break;
                case MemberKind.ACTION:
                    var action = ReflectedAction.Create((MethodInfo)member.Member);
                    if (!action.IsSuccess)
                    {
                        return InteractionResult.Failure<ObjectDescriptor>(action.Error!);
                    }

                    members.Add(new ActionDescriptor(this, handle, member.Id, action.Value));
                    break;
                default:
                    throw new InvalidOperationException("Unknown member kind.");
            }
        }

        if (exposure is not null)
        {
            members.AddRange(exposure.Values.Select(value => (MemberDescriptor)new ValueDescriptor(
                this,
                handle,
                value.Id,
                value.ValueType,
                canRead: true,
                value.CanWrite,
                value.IsNullable,
                ValueConversion.GetEnumOptions(value.ValueType),
                value.Range,
                [],
                value.Read,
                value.CanWrite ? value.Write : null)));
        }

        string? summary = null;
        if (exposure is not null)
        {
            summary = exposure.GetSummary(instance);
        }
        else if (metadata.SummaryMember is not null)
        {
            summary = ReflectionMetadata.Read(metadata.SummaryMember, instance)?.ToString();
        }

        return InteractionResult.Success(new ObjectDescriptor(
            handle,
            identity,
            instance.GetType().FullName ?? instance.GetType().Name,
            summary,
            members.OrderBy(static member => member.Id, StringComparer.Ordinal)
                .ThenBy(static member => member.Kind)
                .ToArray()));
    }

    private static async Task<InteractionResult<object?>> _ReadObservableAsync(
        MemberDescriptor member,
        CancellationToken cancellationToken) => member switch
    {
        ValueDescriptor value => await value.ReadAsync(cancellationToken),
        ReferenceDescriptor reference => await _ReadReferenceAsync(reference, cancellationToken),
        _ => InteractionResult.Failure<object?>(
            InteractionErrorCode.UNSUPPORTED,
            $"Member '{member.Id}' cannot be polled."),
    };

    private static async Task<InteractionResult<object?>> _ReadReferenceAsync(
        ReferenceDescriptor reference,
        CancellationToken cancellationToken)
    {
        var read = await reference.ReadAsync(cancellationToken);
        return read.IsSuccess
            ? InteractionResult.Success<object?>(read.Value)
            : InteractionResult.Failure<object?>(read.Error!);
    }

    private static InteractionResult<DomainIdentity?> _NormalizeIdentity(string? value)
    {
        if (value is null)
        {
            return InteractionResult.Success<DomainIdentity?>(null);
        }

        return string.IsNullOrWhiteSpace(value)
            ? InteractionResult.Failure<DomainIdentity?>(
                InteractionErrorCode.INVALID_INPUT,
                "A domain identity cannot be empty or whitespace.")
            : InteractionResult.Success<DomainIdentity?>(new DomainIdentity(value));
    }

    private async Task<InteractionResult<T>> _ExecuteCheckedAsync<T>(
        Func<Task<InteractionResult<T>>> action,
        CancellationToken cancellationToken)
    {
        if (IsDisposed)
        {
            return _DisposedFailure<T>();
        }

        cancellationToken.ThrowIfCancellationRequested();
        return await action();
    }

    private void _InvocationCompleted(ActionInvocation invocation)
    {
        lock (_Gate)
        {
            _Invocations.Remove(invocation);
        }

        if (invocation.Fault is not null)
        {
            _ReportUnexpected("action invocation", invocation.Fault);
        }
    }

    private void _UntrackSubscription(ObservationSubscription subscription)
    {
        lock (_Gate)
        {
            _Subscriptions.Remove(subscription);
        }
    }

    private void _ReportUnexpected(string operation, Exception exception) =>
        RuntimeDiagnostics.UnexpectedFailure(
            _Logger,
            operation,
            exception,
            _Settings.IncludeSensitiveDiagnosticData);

    private InteractionResult<T> _UnexpectedFailure<T>(string operation, Exception exception)
    {
        _ReportUnexpected(operation, exception);
        return InteractionResult.Failure<T>(
            InteractionErrorCode.FAULT,
            $"The {operation} operation failed unexpectedly.");
    }

    private static InteractionResult<ObjectHandle> _DomainIdentityNotFound(DomainIdentity identity) =>
        InteractionResult.Failure<ObjectHandle>(
            InteractionErrorCode.NOT_FOUND,
            $"No live object has domain identity '{identity}'.");

    private static InteractionResult<T> _DisposedFailure<T>() => InteractionResult.Failure<T>(
        InteractionErrorCode.DISPOSED,
        "The UIEngine host has been disposed.");

    private sealed record _HandleHolder(ObjectHandle Handle);

    private sealed record _RootEntry(RootRegistration Registration, object Instance);
}
