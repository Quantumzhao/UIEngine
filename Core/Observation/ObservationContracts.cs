using System.Runtime.CompilerServices;

namespace UIEngine.Core;

/// <summary>Identifies the semantic shape of one normalized domain change.</summary>
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

/// <summary>Distinguishes an omitted observation value from a supplied null value.</summary>
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

/// <summary>Describes one ordered change emitted by a host-owned observation subscription.</summary>
public sealed record ChangeRecord(
    ObjectIdentity RuntimeIdentity,
    DomainIdentity? DomainIdentity,
    string? MemberId,
    ChangeKind Kind,
    ObservationValue OldValue,
    ObservationValue NewValue,
    long OrderingToken,
    DateTimeOffset Timestamp)
{
    public int? OldIndex { get; init; }

    public int? NewIndex { get; init; }

    /// <summary>Gets the number of records discarded before a buffer-overflow record.</summary>
    public long DroppedChangeCount { get; init; }
}

public enum ObservationMode
{
    AUTOMATIC,
    NOTIFICATIONS,
    POLLING,
}

/// <summary>Selects one exposed member, or every notification when <see cref="MemberId"/> is null.</summary>
public sealed record ObservationRequest
{
    public string? MemberId { get; init; }

    public ObservationMode Mode { get; init; } = ObservationMode.AUTOMATIC;

    /// <summary>Overrides the host polling interval for this subscription.</summary>
    public TimeSpan? PollingInterval { get; init; }
}

/// <summary>Owns a bounded stream of normalized changes and its underlying domain handlers.</summary>
public interface IObservationSubscription : IDisposable, IAsyncDisposable
{
    ObjectHandle Source { get; }

    string? MemberId { get; }

    bool IsDisposed { get; }

    IAsyncEnumerable<ChangeRecord> ReadAllAsync(CancellationToken cancellationToken = default);
}

/// <summary>Provides a custom adapter with the selected live source and stable host identities.</summary>
public sealed record ObservationAdapterContext(
    object Source,
    ObjectHandle Handle,
    DomainIdentity? DomainIdentity,
    string? MemberId);

/// <summary>A provider-produced change before the host assigns ordering and source identity.</summary>
public sealed record ObservationAdapterChange(
    string? MemberId,
    ChangeKind Kind,
    ObservationValue OldValue,
    ObservationValue NewValue)
{
    public int? OldIndex { get; init; }

    public int? NewIndex { get; init; }
}

/// <summary>Adapts a host-configured notification source to normalized changes.</summary>
public interface IObservationAdapter
{
    bool CanObserve(Type objectType);

    ValueTask<InteractionResult<IDisposable>> SubscribeAsync(
        ObservationAdapterContext context,
        Action<ObservationAdapterChange> publish,
        Action<Exception> reportFailure,
        CancellationToken cancellationToken = default);
}

internal sealed class ObservationSubscription : IObservationSubscription
{
    private readonly object _Gate = new();
    private readonly Queue<ChangeRecord> _Changes = [];
    private readonly SemaphoreSlim _Signal = new(0, 1);
    private readonly int _Capacity;
    private readonly Action<ObservationSubscription> _OnDisposed;
    private readonly Action<Exception> _ReportFailure;
    private IDisposable? _SourceSubscription;
    private ChangeRecord? _Overflow;
    private long _DroppedChangeCount;
    private bool _SignalPending;
    private bool _IsCompleted;
    private int _ReaderStarted;
    private int _IsDisposed;

    public ObservationSubscription(
        ObjectHandle source,
        string? memberId,
        int capacity,
        Action<ObservationSubscription> onDisposed,
        Action<Exception> reportFailure)
    {
        Source = source;
        MemberId = memberId;
        _Capacity = capacity;
        _OnDisposed = onDisposed;
        _ReportFailure = reportFailure;
    }

    public ObjectHandle Source { get; }

    public string? MemberId { get; }

    public bool IsDisposed => Volatile.Read(ref _IsDisposed) != 0;

    internal void SetSourceSubscription(IDisposable sourceSubscription)
    {
        ArgumentNullException.ThrowIfNull(sourceSubscription);
        if (Interlocked.CompareExchange(ref _SourceSubscription, sourceSubscription, null) is not null)
        {
            try
            {
                sourceSubscription.Dispose();
            }
            catch (Exception exception)
            {
                _ReportFailure(exception);
            }

            throw new InvalidOperationException("The observation source subscription is already set.");
        }

        if (IsDisposed)
        {
            _DisposeSourceSubscription();
        }
    }

    internal void Publish(ChangeRecord change)
    {
        var release = false;
        lock (_Gate)
        {
            if (_IsCompleted)
            {
                return;
            }

            if (_Changes.Count == _Capacity)
            {
                _Changes.Dequeue();
                _DroppedChangeCount++;
                _Overflow ??= change with
                {
                    Kind = ChangeKind.BUFFER_OVERFLOW,
                    OldValue = ObservationValue.NotSupplied,
                    NewValue = ObservationValue.NotSupplied,
                    OldIndex = null,
                    NewIndex = null,
                };
            }

            _Changes.Enqueue(change);
            if (!_SignalPending)
            {
                _SignalPending = true;
                release = true;
            }
        }

        if (release)
        {
            _Signal.Release();
        }
    }

    public async IAsyncEnumerable<ChangeRecord> ReadAllAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _ReaderStarted, 1) != 0)
        {
            throw new InvalidOperationException("An observation subscription supports one stream reader.");
        }

        while (true)
        {
            await _Signal.WaitAsync(cancellationToken).ConfigureAwait(false);

            ChangeRecord? overflow;
            ChangeRecord[] changes;
            bool completed;
            lock (_Gate)
            {
                overflow = _Overflow is null
                    ? null
                    : _Overflow with { DroppedChangeCount = _DroppedChangeCount };
                _Overflow = null;
                _DroppedChangeCount = 0;
                changes = _Changes.ToArray();
                _Changes.Clear();
                completed = _IsCompleted;
                _SignalPending = false;
            }

            if (overflow is not null)
            {
                yield return overflow;
            }

            foreach (var change in changes)
            {
                yield return change;
            }

            if (completed)
            {
                yield break;
            }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _IsDisposed, 1) != 0)
        {
            return;
        }

        _DisposeSourceSubscription();

        var release = false;
        lock (_Gate)
        {
            _IsCompleted = true;
            if (!_SignalPending)
            {
                _SignalPending = true;
                release = true;
            }
        }

        if (release)
        {
            _Signal.Release();
        }

        _OnDisposed(this);
    }

    private void _DisposeSourceSubscription()
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

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
