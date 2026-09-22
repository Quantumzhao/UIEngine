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
    private readonly Dictionary<Guid, WeakReference<object>> _Objects = [];
    private readonly Dictionary<string, _RootEntry> _Roots = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, string?> _DomainIdentities = [];
    private readonly Dictionary<string, HashSet<Guid>> _DomainIdentityIndex = [];
    private readonly Dictionary<Guid, LogicalPath> _CanonicalPaths = [];
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

    public InteractionResult<Guid> SetRoot(string identifier, object instance)
    {
        if (IsDisposed)
        {
            return _DisposedFailure<Guid>();
        }

        if (string.IsNullOrWhiteSpace(identifier))
        {
            return InteractionResult.Failure<Guid>(
                InteractionErrorCode.INVALID_INPUT,
                "A root identifier cannot be empty or whitespace.");
        }

        if (instance.GetType().IsValueType)
        {
            return InteractionResult.Failure<Guid>(
                InteractionErrorCode.TYPE_MISMATCH,
                "A root must be a reference type.");
        }

        lock (_Gate)
        {
            if (IsDisposed)
            {
                return _DisposedFailure<Guid>();
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

    public Guid GetOrCreateHandle(object instance)
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

    public async Task<InteractionResult<string?>> GetDomainIdentityAsync(Guid handle)
    {
        if (IsDisposed)
        {
            return _DisposedFailure<string?>();
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
            return InteractionResult.Failure<string?>(target.Error!);
        }

        var discovered = await ExecuteAsync(
            "discover domain identity",
            () => Task.FromResult(_DiscoverDomainIdentity(target.Value)));
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

    public InteractionResult<Guid> ResolveDomainIdentity(string identity)
    {
        if (IsDisposed)
        {
            return _DisposedFailure<Guid>();
        }

        if (string.IsNullOrWhiteSpace(identity))
        {
            return InteractionResult.Failure<Guid>(
                InteractionErrorCode.INVALID_INPUT,
                "A domain identity cannot be empty or whitespace.");
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
                : InteractionResult.Failure<Guid>(
                    InteractionErrorCode.AMBIGUOUS,
                    $"Domain identity '{identity}' matches multiple live objects.");
        }
    }

    public async Task<InteractionResult<ObjectDescriptor>> DescribeAsync(Guid handle)
    {
        var target = ResolveTarget(handle);
        if (!target.IsSuccess)
        {
            return InteractionResult.Failure<ObjectDescriptor>(target.Error!);
        }

        var identity = await GetDomainIdentityAsync(handle);
        if (!identity.IsSuccess)
        {
            return InteractionResult.Failure<ObjectDescriptor>(identity.Error!);
        }

        return await ExecuteAsync(
            "describe object",
            () => Task.FromResult(_CreateDescriptor(target.Value, handle, identity.Value)));
    }

    public async Task<InteractionResult<ResolvedNode>> ResolveRootNodeAsync(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return InteractionResult.Failure<ResolvedNode>(
                InteractionErrorCode.INVALID_INPUT,
                "A root identifier cannot be empty or whitespace.");
        }

        _RootEntry root;
        lock (_Gate)
        {
            if (IsDisposed)
            {
                return _DisposedFailure<ResolvedNode>();
            }

            if (!_Roots.TryGetValue(identifier, out root!))
            {
                return InteractionResult.Failure<ResolvedNode>(
                    InteractionErrorCode.NOT_FOUND,
                    $"Root '{identifier}' was not found.");
            }
        }

        var described = await DescribeAsync(root.Registration.Handle);
        if (!described.IsSuccess)
        {
            return InteractionResult.Failure<ResolvedNode>(described.Error!);
        }

        _Settings.Exposures.TryGetValue(root.Instance.GetType(), out var exposure);
        var programmaticValueIds = exposure?.Values
            .Select(static value => value.Id)
            .ToHashSet(StringComparer.Ordinal) ?? [];
        var node = new LiveObjectNode(
            this,
            identifier,
            root.Instance.GetType(),
            described.Value,
            programmaticValueIds);
        return InteractionResult.Success(new ResolvedNode(
            LogicalPath.Root.Append(identifier),
            node));
    }

    public Task<InteractionResult<ResolvedPath>> ResolvePathAsync(string path)
    {
        var parsed = LogicalPath.Parse(path);
        return parsed.IsSuccess
            ? ResolvePathAsync(parsed.Value)
            : Task.FromResult(InteractionResult.Failure<ResolvedPath>(parsed.Error!));
    }

    public Task<InteractionResult<ResolvedPath>> ResolvePathAsync(LogicalPath path) => ExecuteAsync(
        "resolve path",
        () => PathResolution.ResolveAsync(this, path));

    public async Task<InteractionResult<ResolvedBinding>> ResolveBindingAsync(
        BindingReference binding)
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

        Guid? identityHandle = null;
        ObjectDescriptor? identityDescriptor = null;
        LogicalPath? identityPath = null;
        if (binding.DomainIdentity is not null)
        {
            var resolvedIdentity = ResolveDomainIdentity(binding.DomainIdentity);
            if (resolvedIdentity.IsSuccess)
            {
                identityHandle = resolvedIdentity.Value;
                var described = await DescribeAsync(resolvedIdentity.Value);
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

        var resolvedPath = await ResolvePathAsync(parsed.Value);
        Guid owner;
        ObjectDescriptor descriptor;
        LogicalPath canonical;
        if (resolvedPath.IsSuccess && resolvedPath.Value.IsObject)
        {
            if (binding.DomainIdentity is not null && !StringComparer.Ordinal.Equals(
                    resolvedPath.Value.OwnerDescriptor.DomainIdentity,
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
        Guid handle,
        string? memberId = null,
        TimeSpan? pollingInterval = null)
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

        var described = await DescribeAsync(handle);
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
            var initial = await _ReadObservableAsync(member!);
            if (!initial.IsSuccess)
            {
                subscription.Dispose();
                return InteractionResult.Failure<ObservationSubscription>(initial.Error!);
            }

            sourceSubscription = new PollingObserver(
                initial.Value,
                polling,
                () => _ReadObservableAsync(member!),
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
                exception => _ReportUnexpected("observation notification", exception));
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

    public Task<InteractionResult<ObservationSubscription>> ObserveAsync(
        BaseNode node,
        TimeSpan? pollingInterval = null)
    {
        return node switch
        {
            LiveObjectNode objectNode => ObserveAsync(
                objectNode.Handle,
                pollingInterval: pollingInterval),
            LiveValueNode valueNode => ObserveAsync(
                valueNode.Descriptor.Owner,
                valueNode.Id,
                pollingInterval),
            LiveReflectedMemberNode memberNode => ObserveAsync(
                memberNode.Descriptor.Owner,
                memberNode.Id,
                pollingInterval),
            _ => Task.FromResult(InteractionResult.Failure<ObservationSubscription>(
                InteractionErrorCode.UNSUPPORTED,
                "Only live object and member nodes can be observed.")),
        };
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
                _ReportUnexpected("invocation completion", exception);
            }
        }
    }

    internal InteractionResult<object> ResolveTarget(Guid handle)
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
        Func<Task<InteractionResult<T>>> action)
    {
        if (IsDisposed)
        {
            return _DisposedFailure<T>();
        }

        try
        {
            if (_Settings.Dispatcher.CheckAccess())
            {
                return await _ExecuteCheckedAsync(action);
            }

            return await _Settings.Dispatcher.InvokeAsync(() => _ExecuteCheckedAsync(action));
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

    internal ActionInvocation CreateInvocation(string actionId)
    {
        var invocation = new ActionInvocation(
            actionId,
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

    internal void RecordCanonicalPath(Guid handle, LogicalPath path)
    {
        lock (_Gate)
        {
            if (!IsDisposed)
            {
                _CanonicalPaths[handle] = path;
            }
        }
    }

    internal bool TryGetCanonicalPath(Guid handle, out LogicalPath? path)
    {
        lock (_Gate)
        {
            return _CanonicalPaths.TryGetValue(handle, out path);
        }
    }

    private Guid _GetOrCreateHandle(object instance)
    {
        if (_Handles.TryGetValue(instance, out var existing))
        {
            return existing.Handle;
        }

        var handle = Guid.NewGuid();
        _Handles.Add(instance, new _HandleHolder(handle));
        _Objects.Add(handle, new WeakReference<object>(instance));
        return handle;
    }

    private InteractionResult<string?> _DiscoverDomainIdentity(object instance)
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
                return InteractionResult.Failure<string?>(
                    InteractionErrorCode.INVALID_INPUT,
                    "A domain identity member must be a readable string field or property.");
            }
        }

        return _NormalizeIdentity(attributed);
    }

    private InteractionResult<ObjectDescriptor> _CreateDescriptor(
        object instance,
        Guid handle,
        string? identity)
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
        MemberDescriptor member) => member switch
    {
        ValueDescriptor value => await value.ReadAsync(),
        ReferenceDescriptor reference => await _ReadReferenceAsync(reference),
        _ => InteractionResult.Failure<object?>(
            InteractionErrorCode.UNSUPPORTED,
            $"Member '{member.Id}' cannot be polled."),
    };

    private static async Task<InteractionResult<object?>> _ReadReferenceAsync(
        ReferenceDescriptor reference)
    {
        var read = await reference.ReadAsync();
        return read.IsSuccess
            ? InteractionResult.Success<object?>(read.Value)
            : InteractionResult.Failure<object?>(read.Error!);
    }

    private static InteractionResult<string?> _NormalizeIdentity(string? value)
    {
        if (value is null)
        {
            return InteractionResult.Success<string?>(null);
        }

        return string.IsNullOrWhiteSpace(value)
            ? InteractionResult.Failure<string?>(
                InteractionErrorCode.INVALID_INPUT,
                "A domain identity cannot be empty or whitespace.")
            : InteractionResult.Success<string?>(value);
    }

    private async Task<InteractionResult<T>> _ExecuteCheckedAsync<T>(
        Func<Task<InteractionResult<T>>> action)
    {
        if (IsDisposed)
        {
            return _DisposedFailure<T>();
        }

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

    private static InteractionResult<Guid> _DomainIdentityNotFound(string identity) =>
        InteractionResult.Failure<Guid>(
            InteractionErrorCode.NOT_FOUND,
            $"No live object has domain identity '{identity}'.");

    private static InteractionResult<T> _DisposedFailure<T>() => InteractionResult.Failure<T>(
        InteractionErrorCode.DISPOSED,
        "The UIEngine host has been disposed.");

    private sealed record _HandleHolder(Guid Handle);

    private sealed record _RootEntry(RootRegistration Registration, object Instance);
}
