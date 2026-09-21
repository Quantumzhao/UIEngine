using System.Collections;
using System.Collections.Specialized;
using System.ComponentModel;

namespace UIEngine.Core;

public enum ChangeKind
{
    MEMBER_CHANGED,
    MEMBER_INVALIDATED,
    COLLECTION_ITEMS_ADDED,
    COLLECTION_ITEMS_REMOVED,
    COLLECTION_ITEMS_REPLACED,
    COLLECTION_ITEMS_MOVED,
    COLLECTION_RESET,
    SOURCE_REPLACED,
    BUFFER_OVERFLOW,
}

public readonly record struct ObservationValue
{
    private ObservationValue(bool isSupplied, object? value)
    {
        IsSupplied = isSupplied;
        Value = value;
    }

    public bool IsSupplied { get; }

    public object? Value { get; }

    public static ObservationValue NotSupplied => default;

    public static ObservationValue Supplied(object? value) => new(true, value);
}

public sealed record ChangeRecord(
    ObjectHandle Source,
    DomainIdentity? DomainIdentity,
    string? MemberId,
    ChangeKind Kind,
    ObservationValue OldValue,
    ObservationValue NewValue,
    long OrderingToken)
{
    public int? OldIndex { get; init; }

    public int? NewIndex { get; init; }

    public long DroppedChangeCount { get; init; }
}

public sealed class ObservationSubscription : IDisposable
{
    private readonly BoundedAsyncStream<ChangeRecord> _Changes;
    private readonly Action<ObservationSubscription> _OnDisposed;
    private readonly Action<Exception> _ReportFailure;
    private IDisposable? _SourceSubscription;
    private int _Disposed;

    internal ObservationSubscription(
        ObjectHandle source,
        string? memberId,
        int capacity,
        Func<long, ChangeRecord> createOverflow,
        Action<ObservationSubscription> onDisposed,
        Action<Exception> reportFailure)
    {
        Source = source;
        MemberId = memberId;
        _Changes = new BoundedAsyncStream<ChangeRecord>(capacity, createOverflow);
        _OnDisposed = onDisposed;
        _ReportFailure = reportFailure;
    }

    public ObjectHandle Source { get; }

    public string? MemberId { get; }

    public bool IsDisposed => Volatile.Read(ref _Disposed) != 0;

    public IAsyncEnumerable<ChangeRecord> ReadAllAsync(
        CancellationToken cancellationToken = default) =>
        _Changes.ReadAllAsync(cancellationToken);

    internal void Publish(ChangeRecord change) => _Changes.Publish(change);

    internal void SetSourceSubscription(IDisposable sourceSubscription)
    {
        if (Interlocked.CompareExchange(ref _SourceSubscription, sourceSubscription, null) is not null)
        {
            sourceSubscription.Dispose();
            throw new InvalidOperationException("The observation source is already set.");
        }

        if (IsDisposed)
        {
            _DisposeSource();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _Disposed, 1) != 0)
        {
            return;
        }

        _DisposeSource();
        _Changes.Complete();
        _OnDisposed(this);
    }

    private void _DisposeSource()
    {
        try
        {
            Interlocked.Exchange(ref _SourceSubscription, null)?.Dispose();
        }
        catch (Exception exception)
        {
            _ReportFailure(exception);
        }
    }
}

internal sealed class NotificationObserver : IDisposable
{
    private readonly object _Gate = new();
    private readonly object _Source;
    private readonly string? _MemberId;
    private readonly HashSet<string> _ExposedMembers;
    private readonly CollectionDescriptor? _Collection;
    private readonly Action<string?, ChangeKind, ObservationValue, ObservationValue, int?, int?> _Publish;
    private readonly Action<Exception> _ReportFailure;
    private INotifyCollectionChanged? _CollectionSource;
    private int _CollectionGeneration;
    private int _Disposed;

    private NotificationObserver(
        object source,
        string? memberId,
        ObjectDescriptor descriptor,
        CollectionDescriptor? collection,
        Action<string?, ChangeKind, ObservationValue, ObservationValue, int?, int?> publish,
        Action<Exception> reportFailure)
    {
        _Source = source;
        _MemberId = memberId;
        _ExposedMembers = descriptor.Members.Select(static member => member.Id)
            .ToHashSet(StringComparer.Ordinal);
        _Collection = collection;
        _Publish = publish;
        _ReportFailure = reportFailure;
    }

    public static async Task<InteractionResult<IDisposable>> CreateAsync(
        object source,
        string? memberId,
        ObjectDescriptor descriptor,
        MemberDescriptor? member,
        Action<string?, ChangeKind, ObservationValue, ObservationValue, int?, int?> publish,
        Action<Exception> reportFailure,
        CancellationToken cancellationToken)
    {
        var collection = member as CollectionDescriptor;
        INotifyCollectionChanged? collectionSource = source as INotifyCollectionChanged;
        if (collection is not null)
        {
            var read = await collection.ReadSourceAsync(cancellationToken);
            if (!read.IsSuccess)
            {
                return InteractionResult.Failure<IDisposable>(read.Error!);
            }

            collectionSource = read.Value as INotifyCollectionChanged;
        }

        if (source is not INotifyPropertyChanged && collectionSource is null)
        {
            return InteractionResult.Failure<IDisposable>(
                InteractionErrorCode.UNSUPPORTED,
                "The selected source does not provide change notifications.");
        }

        var observer = new NotificationObserver(
            source,
            memberId,
            descriptor,
            collection,
            publish,
            reportFailure);
        observer._Attach(collectionSource);
        return InteractionResult.Success<IDisposable>(observer);
    }

    private bool IsDisposed => Volatile.Read(ref _Disposed) != 0;

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
            var wildcard = string.IsNullOrEmpty(propertyName);
            if (!wildcard && _MemberId is not null &&
                !StringComparer.Ordinal.Equals(_MemberId, propertyName))
            {
                return;
            }

            if (!wildcard && _MemberId is null && !_ExposedMembers.Contains(propertyName!))
            {
                return;
            }

            if (_Collection is not null &&
                (wildcard || StringComparer.Ordinal.Equals(_Collection.Id, propertyName)))
            {
                _Publish(
                    _MemberId,
                    ChangeKind.SOURCE_REPLACED,
                    ObservationValue.NotSupplied,
                    ObservationValue.NotSupplied,
                    null,
                    null);
                _BeginCollectionReplacement();
                return;
            }

            _Publish(
                wildcard ? _MemberId : propertyName,
                wildcard ? ChangeKind.MEMBER_INVALIDATED : ChangeKind.MEMBER_CHANGED,
                ObservationValue.NotSupplied,
                ObservationValue.NotSupplied,
                null,
                null);
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

            _Publish(
                _MemberId,
                args.Action switch
                {
                    NotifyCollectionChangedAction.Add => ChangeKind.COLLECTION_ITEMS_ADDED,
                    NotifyCollectionChangedAction.Remove => ChangeKind.COLLECTION_ITEMS_REMOVED,
                    NotifyCollectionChangedAction.Replace => ChangeKind.COLLECTION_ITEMS_REPLACED,
                    NotifyCollectionChangedAction.Move => ChangeKind.COLLECTION_ITEMS_MOVED,
                    _ => ChangeKind.COLLECTION_RESET,
                },
                args.OldItems is null
                    ? ObservationValue.NotSupplied
                    : ObservationValue.Supplied(_Copy(args.OldItems)),
                args.NewItems is null
                    ? ObservationValue.NotSupplied
                    : ObservationValue.Supplied(_Copy(args.NewItems)),
                args.OldStartingIndex >= 0 ? args.OldStartingIndex : null,
                args.NewStartingIndex >= 0 ? args.NewStartingIndex : null);
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
            var read = await _Collection!.ReadSourceAsync();
            if (!read.IsSuccess)
            {
                return;
            }

            lock (_Gate)
            {
                if (IsDisposed || generation != _CollectionGeneration)
                {
                    return;
                }

                _CollectionSource = read.Value as INotifyCollectionChanged;
                if (_CollectionSource is not null)
                {
                    _CollectionSource.CollectionChanged += _OnCollectionChanged;
                }
            }
        }
        catch (Exception exception)
        {
            _ReportFailure(exception);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _Disposed, 1) != 0)
        {
            return;
        }

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

    private static object?[] _Copy(IList items)
    {
        var copy = new object?[items.Count];
        items.CopyTo(copy, 0);
        return copy;
    }
}

internal sealed class PollingObserver : IDisposable
{
    private readonly CancellationTokenSource _Cancellation = new();
    private int _Disposed;

    public PollingObserver(
        object? initialValue,
        TimeSpan interval,
        Func<CancellationToken, Task<InteractionResult<object?>>> read,
        Action<object?, object?> publish,
        Action<Exception> reportFailure)
    {
        _ = _RunAsync();

        async Task _RunAsync()
        {
            var previous = initialValue;
            try
            {
                while (!_Cancellation.IsCancellationRequested)
                {
                    await Task.Delay(interval, _Cancellation.Token);
                    var current = await read(_Cancellation.Token);
                    if (!current.IsSuccess)
                    {
                        continue;
                    }

                    if (!Equals(previous, current.Value))
                    {
                        publish(previous, current.Value);
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
        if (Interlocked.Exchange(ref _Disposed, 1) == 0)
        {
            _Cancellation.Cancel();
            _Cancellation.Dispose();
        }
    }
}
