using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;
using UIEngine.Core.Reflection;

namespace UIEngine.Core;

internal static class ObservationSourceFactory
{
    public static async ValueTask<InteractionResult<IDisposable>> SubscribeAsync(
        UIEngineHost host,
        object source,
        ObjectHandle handle,
        DomainIdentity? domainIdentity,
        ObservationRequest request,
        Action<ObservationAdapterChange> publish,
        Action<Exception> reportFailure,
        CancellationToken cancellationToken)
    {
        IObjectDescriptor? descriptor = null;
        IMemberDescriptor? member = null;
        if (request.MemberId is not null || request.Mode == ObservationMode.POLLING)
        {
            var described = await host.DescribeAsync(handle, cancellationToken).ConfigureAwait(false);
            if (!described.IsSuccess)
            {
                return InteractionResult.Failure<IDisposable>(
                    described.Error!.Code,
                    described.Error.Message);
            }

            descriptor = described.Value;
            if (request.MemberId is not null)
            {
                member = _FindMember(descriptor, request.MemberId);
                if (member is null)
                {
                    return InteractionResult.Failure<IDisposable>(
                        InteractionErrorCode.TARGET_MISSING,
                        $"No exposed member with identifier '{request.MemberId}' exists on the observed object.");
                }

                if (member is IActionDescriptor)
                {
                    return InteractionResult.Failure<IDisposable>(
                        InteractionErrorCode.OBSERVATION_UNAVAILABLE,
                        $"Action '{request.MemberId}' cannot be observed.");
                }
            }
        }

        if (request.Mode == ObservationMode.POLLING)
        {
            if (member is null)
            {
                return InteractionResult.Failure<IDisposable>(
                    InteractionErrorCode.INVALID_INPUT,
                    "Polling requires one exposed member identifier.");
            }

            return await _SubscribePollingAsync(
                host,
                handle.Identity,
                member,
                request,
                publish,
                reportFailure,
                cancellationToken).ConfigureAwait(false);
        }

        IObservationAdapter? customAdapter;
        try
        {
            customAdapter = host.Configuration.ObservationAdapters.FirstOrDefault(
                adapter => adapter.CanObserve(source.GetType()));
        }
        catch (Exception exception)
        {
            reportFailure(exception);
            return InteractionResult.Failure<IDisposable>(
                InteractionErrorCode.OBSERVATION_FAILED,
                "Selecting a custom observation adapter failed.");
        }
        if (customAdapter is not null)
        {
            var context = new ObservationAdapterContext(
                source,
                handle,
                domainIdentity,
                request.MemberId);
            var policy = customAdapter as IInteractionDispatchPolicy;
            return await host.ExecuteInteractionAsync(
                InteractionDispatchOperation.OBSERVATION_SUBSCRIBE,
                policy?.CanExecuteDirectly(InteractionDispatchOperation.OBSERVATION_SUBSCRIBE) == true,
                () => customAdapter.SubscribeAsync(
                    context,
                    publish,
                    reportFailure,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }

        var notifications = await NotificationSourceSubscription.CreateAsync(
            host,
            source,
            request.MemberId,
            descriptor,
            member,
            publish,
            reportFailure,
            cancellationToken).ConfigureAwait(false);
        if (notifications.IsSuccess)
        {
            return notifications;
        }

        if (request.Mode == ObservationMode.AUTOMATIC && request.PollingInterval is not null && member is not null)
        {
            return await _SubscribePollingAsync(
                host,
                handle.Identity,
                member,
                request,
                publish,
                reportFailure,
                cancellationToken).ConfigureAwait(false);
        }

        return notifications;
    }

    private static IMemberDescriptor? _FindMember(IObjectDescriptor descriptor, string memberId) =>
        descriptor.Values.Cast<IMemberDescriptor>()
            .Concat(descriptor.References)
            .Concat(descriptor.Collections)
            .Concat(descriptor.Actions)
            .FirstOrDefault(member => string.Equals(member.Id, memberId, StringComparison.Ordinal));

    private static async ValueTask<InteractionResult<IDisposable>> _SubscribePollingAsync(
        UIEngineHost host,
        ObjectIdentity identity,
        IMemberDescriptor member,
        ObservationRequest request,
        Action<ObservationAdapterChange> publish,
        Action<Exception> reportFailure,
        CancellationToken cancellationToken)
    {
        Func<CancellationToken, ValueTask<InteractionResult<object?>>>? read = member switch
        {
            IValueDescriptor value => value.ReadAsync,
            IReferenceDescriptor reference => async cancellation =>
            {
                var result = await reference.ReadAsync(cancellation).ConfigureAwait(false);
                return result.IsSuccess
                    ? InteractionResult.Success<object?>(result.Value)
                    : InteractionResult.Failure<object?>(result.Error!.Code, result.Error.Message);
            },
            _ => null,
        };
        if (read is null)
        {
            return InteractionResult.Failure<IDisposable>(
                InteractionErrorCode.OBSERVATION_UNAVAILABLE,
                $"Member '{member.Id}' does not support polling.");
        }

        var initial = await read(cancellationToken).ConfigureAwait(false);
        if (!initial.IsSuccess)
        {
            return InteractionResult.Failure<IDisposable>(initial.Error!.Code, initial.Error.Message);
        }

        IDisposable polling = new PollingSourceSubscription(
            identity,
            member.Id,
            initial.Value,
            request.PollingInterval ?? host.Configuration.ObservationPollingInterval,
            read,
            publish,
            reportFailure,
            host);
        return InteractionResult.Success(polling);
    }
}

internal sealed class NotificationSourceSubscription : IDisposable
{
    private readonly object _Gate = new();
    private readonly UIEngineHost _Host;
    private readonly object _Source;
    private readonly string? _MemberId;
    private readonly HashSet<string> _ExposedMembers;
    private readonly Action<ObservationAdapterChange> _Publish;
    private readonly Action<Exception> _ReportFailure;
    private readonly ReflectionMemberMetadata? _CollectionMember;
    private INotifyCollectionChanged? _CollectionSource;
    private int _CollectionGeneration;
    private int _IsDisposed;

    private NotificationSourceSubscription(
        UIEngineHost host,
        object source,
        string? memberId,
        IObjectDescriptor? descriptor,
        ReflectionMemberMetadata? collectionMember,
        Action<ObservationAdapterChange> publish,
        Action<Exception> reportFailure)
    {
        _Host = host;
        _Source = source;
        _MemberId = memberId;
        _ExposedMembers = descriptor is null
            ? []
            : descriptor.Values.Cast<IMemberDescriptor>()
                .Concat(descriptor.References)
                .Concat(descriptor.Collections)
                .Select(static item => item.Id)
                .ToHashSet(StringComparer.Ordinal);
        _CollectionMember = collectionMember;
        _Publish = publish;
        _ReportFailure = reportFailure;
    }

    public static async ValueTask<InteractionResult<IDisposable>> CreateAsync(
        UIEngineHost host,
        object source,
        string? memberId,
        IObjectDescriptor? descriptor,
        IMemberDescriptor? member,
        Action<ObservationAdapterChange> publish,
        Action<Exception> reportFailure,
        CancellationToken cancellationToken)
    {
        ReflectionMemberMetadata? collectionMember = null;
        INotifyCollectionChanged? collectionSource = source as INotifyCollectionChanged;
        if (member is ICollectionDescriptor)
        {
            collectionMember = ReflectionTypeMetadataCache.GetOrCreate(source.GetType())
                .DescriptorMembers
                .FirstOrDefault(candidate =>
                    candidate.Kind == ReflectionMemberKind.COLLECTION &&
                    string.Equals(candidate.Id, member.Id, StringComparison.Ordinal));
            if (collectionMember is null)
            {
                return InteractionResult.Failure<IDisposable>(
                    InteractionErrorCode.OBSERVATION_UNAVAILABLE,
                    $"Collection member '{member.Id}' requires a notification adapter supplied by its provider.");
            }

            var read = await host.ExecuteInteractionAsync(
                InteractionDispatchOperation.OBSERVATION_SUBSCRIBE,
                canExecuteDirectly: false,
                () => _ReadCollectionSource(source, collectionMember),
                cancellationToken).ConfigureAwait(false);
            if (!read.IsSuccess)
            {
                return InteractionResult.Failure<IDisposable>(read.Error!.Code, read.Error.Message);
            }

            collectionSource = read.Value;
        }

        var supportsProperties = source is INotifyPropertyChanged;
        if (!supportsProperties && collectionSource is null)
        {
            return InteractionResult.Failure<IDisposable>(
                InteractionErrorCode.OBSERVATION_UNAVAILABLE,
                "The selected source does not provide change notifications.");
        }

        var subscription = new NotificationSourceSubscription(
            host,
            source,
            memberId,
            descriptor,
            collectionMember,
            publish,
            reportFailure);
        try
        {
            var attached = await host.ExecuteInteractionAsync(
                InteractionDispatchOperation.OBSERVATION_SUBSCRIBE,
                canExecuteDirectly: false,
                () =>
                {
                    subscription._Attach(collectionSource);
                    return InteractionResult.Success<IDisposable>(subscription);
                },
                cancellationToken).ConfigureAwait(false);
            if (!attached.IsSuccess)
            {
                subscription.Dispose();
            }

            return attached;
        }
        catch (Exception exception)
        {
            subscription.Dispose();
            reportFailure(exception);
            return InteractionResult.Failure<IDisposable>(
                InteractionErrorCode.OBSERVATION_FAILED,
                "Attaching domain notification handlers failed.");
        }
    }

    private void _Attach(INotifyCollectionChanged? collectionSource)
    {
        if (_Source is INotifyPropertyChanged properties)
        {
            properties.PropertyChanged += _OnPropertyChanged;
        }

        _CollectionSource = collectionSource;
        if (_CollectionSource is not null)
        {
            _CollectionSource.CollectionChanged += _OnCollectionChanged;
        }
    }

    private void _OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        try
        {
            var propertyName = args.PropertyName;
            var isWildcard = string.IsNullOrEmpty(propertyName);
            if (!isWildcard && _MemberId is not null &&
                !string.Equals(_MemberId, propertyName, StringComparison.Ordinal))
            {
                return;
            }

            if (!isWildcard && _MemberId is null &&
                _ExposedMembers.Count > 0 && !_ExposedMembers.Contains(propertyName!))
            {
                return;
            }

            if (_CollectionMember is not null &&
                (isWildcard || string.Equals(_CollectionMember.Member.Name, propertyName, StringComparison.Ordinal)))
            {
                _Publish(new ObservationAdapterChange(
                    _MemberId,
                    ChangeKind.SOURCE_REPLACED,
                    ObservationValue.NotSupplied,
                    ObservationValue.NotSupplied));
                _BeginCollectionReplacement();
                return;
            }

            _Publish(new ObservationAdapterChange(
                isWildcard ? _MemberId : propertyName,
                isWildcard ? ChangeKind.MEMBER_INVALIDATED : ChangeKind.MEMBER_CHANGED,
                ObservationValue.NotSupplied,
                ObservationValue.NotSupplied));
        }
        catch (Exception exception)
        {
            _ReportFailure(exception);
        }
    }

    private void _OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        try
        {
            lock (_Gate)
            {
                if (IsDisposed || !ReferenceEquals(sender, _CollectionSource))
                {
                    return;
                }
            }

            var oldValue = args.OldItems is null
                ? ObservationValue.NotSupplied
                : ObservationValue.Supplied(_CopyItems(args.OldItems));
            var newValue = args.NewItems is null
                ? ObservationValue.NotSupplied
                : ObservationValue.Supplied(_CopyItems(args.NewItems));
            _Publish(new ObservationAdapterChange(
                _MemberId,
                args.Action switch
                {
                    NotifyCollectionChangedAction.Add => ChangeKind.COLLECTION_ITEMS_ADDED,
                    NotifyCollectionChangedAction.Remove => ChangeKind.COLLECTION_ITEMS_REMOVED,
                    NotifyCollectionChangedAction.Replace => ChangeKind.COLLECTION_ITEMS_REPLACED,
                    NotifyCollectionChangedAction.Move => ChangeKind.COLLECTION_ITEMS_MOVED,
                    NotifyCollectionChangedAction.Reset => ChangeKind.COLLECTION_RESET,
                    _ => ChangeKind.COLLECTION_RESET,
                },
                oldValue,
                newValue)
            {
                OldIndex = args.OldStartingIndex >= 0 ? args.OldStartingIndex : null,
                NewIndex = args.NewStartingIndex >= 0 ? args.NewStartingIndex : null,
            });
        }
        catch (Exception exception)
        {
            _ReportFailure(exception);
        }
    }

    private void _BeginCollectionReplacement()
    {
        int generation;
        lock (_Gate)
        {
            generation = ++_CollectionGeneration;
            if (_CollectionSource is not null)
            {
                _CollectionSource.CollectionChanged -= _OnCollectionChanged;
                _CollectionSource = null;
            }
        }

        _ = _AttachReplacementAsync(generation);
    }

    private async Task _AttachReplacementAsync(int generation)
    {
        try
        {
            var read = await _Host.ExecuteInteractionAsync(
                InteractionDispatchOperation.OBSERVATION_SUBSCRIBE,
                canExecuteDirectly: false,
                () => _ReadCollectionSource(_Source, _CollectionMember!),
                CancellationToken.None).ConfigureAwait(false);
            if (!read.IsSuccess)
            {
                _Host.ReportObservationFailure(
                    _Host.GetOrCreateHandle(_Source).Identity,
                    _MemberId,
                    read.Error!.Code);
                return;
            }

            lock (_Gate)
            {
                if (IsDisposed || generation != _CollectionGeneration)
                {
                    return;
                }

                _CollectionSource = read.Value;
                if (_CollectionSource is not null)
                {
                    _CollectionSource.CollectionChanged += _OnCollectionChanged;
                }
            }
        }
        catch (ObjectDisposedException) when (_Host.IsDisposed)
        {
        }
        catch (Exception exception)
        {
            _ReportFailure(exception);
        }
    }

    private static InteractionResult<INotifyCollectionChanged?> _ReadCollectionSource(
        object source,
        ReflectionMemberMetadata metadata)
    {
        try
        {
            var value = ReflectionMemberAccess.Read(metadata.Member, source);
            return InteractionResult.Success(value as INotifyCollectionChanged);
        }
        catch (Exception exception)
        {
            return InteractionResult.Failure<INotifyCollectionChanged?>(
                InteractionErrorCode.OBSERVATION_FAILED,
                $"Reading observed collection member '{metadata.Id}' failed: {exception.Message}");
        }
    }

    private static object?[] _CopyItems(IList items)
    {
        var copy = new object?[items.Count];
        items.CopyTo(copy, 0);
        return copy;
    }

    private bool IsDisposed => Volatile.Read(ref _IsDisposed) != 0;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _IsDisposed, 1) != 0)
        {
            return;
        }

        try
        {
            if (_Source is INotifyPropertyChanged properties)
            {
                properties.PropertyChanged -= _OnPropertyChanged;
            }

            lock (_Gate)
            {
                _CollectionGeneration++;
                if (_CollectionSource is not null)
                {
                    _CollectionSource.CollectionChanged -= _OnCollectionChanged;
                    _CollectionSource = null;
                }
            }
        }
        catch (Exception exception)
        {
            _ReportFailure(exception);
        }
    }
}

internal sealed class PollingSourceSubscription : IDisposable
{
    private readonly CancellationTokenSource _Cancellation = new();
    private readonly Task _Polling;
    private int _IsDisposed;

    public PollingSourceSubscription(
        ObjectIdentity identity,
        string memberId,
        object? initialValue,
        TimeSpan interval,
        Func<CancellationToken, ValueTask<InteractionResult<object?>>> read,
        Action<ObservationAdapterChange> publish,
        Action<Exception> reportFailure,
        UIEngineHost host)
    {
        _Polling = _RunAsync();

        async Task _RunAsync()
        {
            var previous = initialValue;
            try
            {
                while (!_Cancellation.IsCancellationRequested && !host.IsDisposed)
                {
                    await Task.Delay(interval, _Cancellation.Token).ConfigureAwait(false);
                    var current = await read(_Cancellation.Token).ConfigureAwait(false);
                    if (!current.IsSuccess)
                    {
                        host.ReportObservationFailure(
                            identity,
                            memberId,
                            current.Error!.Code);
                        continue;
                    }

                    if (!Equals(previous, current.Value))
                    {
                        publish(new ObservationAdapterChange(
                            memberId,
                            ChangeKind.MEMBER_CHANGED,
                            ObservationValue.Supplied(previous),
                            ObservationValue.Supplied(current.Value)));
                        previous = current.Value;
                    }
                }
            }
            catch (OperationCanceledException) when (_Cancellation.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                reportFailure(exception);
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _IsDisposed, 1) == 0)
        {
            _Cancellation.Cancel();
        }
    }
}
