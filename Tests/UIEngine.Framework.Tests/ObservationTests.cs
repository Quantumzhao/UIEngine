using System.Collections;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using UIEngine.Core;
using UIEngine.Core.Attributes;
using UIEngine.Core.Reflection;
using Xunit;

namespace UIEngine.Framework.Tests;

public sealed class ObservationTests
{
    [Fact]
    public async Task PropertyNotificationsPreserveIdentityWildcardAndUnspecifiedValues()
    {
        var model = new _NotifyingModel();
        using var host = _CreateReflectionHost();
        var handle = host.RegisterRoot("model", model).Value;
        var observed = await host.ObserveAsync(handle, new ObservationRequest
        {
            MemberId = nameof(_NotifyingModel.Name),
        });
        await using var subscription = observed.Value;
        await using var changes = subscription.ReadAllAsync().GetAsyncEnumerator();

        model.Name = "updated";
        var changed = await _NextAsync(changes);
        model.InvalidateAll();
        var invalidated = await _NextAsync(changes);

        Assert.Equal(handle.Identity, changed.RuntimeIdentity);
        Assert.Equal(new DomainIdentity("model-1"), changed.DomainIdentity);
        Assert.Equal(nameof(_NotifyingModel.Name), changed.MemberId);
        Assert.Equal(ChangeKind.MEMBER_CHANGED, changed.Kind);
        Assert.False(changed.OldValue.IsSupplied);
        Assert.False(changed.NewValue.IsSupplied);
        Assert.Equal(ChangeKind.MEMBER_INVALIDATED, invalidated.Kind);
        Assert.True(changed.OrderingToken < invalidated.OrderingToken);
    }

    [Fact]
    public async Task CollectionNotificationsCoverEveryShapeAndFollowReplacement()
    {
        var model = new _NotifyingModel();
        var original = model.Items;
        using var host = _CreateReflectionHost();
        var handle = host.RegisterRoot("model", model).Value;
        var observed = await host.ObserveAsync(handle, new ObservationRequest
        {
            MemberId = nameof(_NotifyingModel.Items),
        });
        await using var subscription = observed.Value;
        await using var changes = subscription.ReadAllAsync().GetAsyncEnumerator();

        original.AddMany("a", "b");
        var added = await _NextAsync(changes);
        original.ReplaceMany(["a", "b"], ["c", "d"]);
        var replaced = await _NextAsync(changes);
        original.MoveMany(["c", "d"], oldIndex: 0, newIndex: 1);
        var moved = await _NextAsync(changes);
        original.RemoveMany("c", "d");
        var removed = await _NextAsync(changes);
        original.Reset();
        var reset = await _NextAsync(changes);

        var replacement = new _TestObservableCollection();
        model.ReplaceItems(replacement);
        var sourceReplaced = await _NextAsync(changes);
        model.ReplaceItems(replacement);
        var sameSourceReplaced = await _NextAsync(changes);
        original.Add("stale");
        replacement.Add("current");
        var current = await _NextAsync(changes);
        replacement.Reset();
        var currentReset = await _NextAsync(changes);

        Assert.Equal(ChangeKind.COLLECTION_ITEMS_ADDED, added.Kind);
        Assert.Equal(new object?[] { "a", "b" }, Assert.IsType<object?[]>(added.NewValue.Value));
        Assert.Equal(ChangeKind.COLLECTION_ITEMS_REPLACED, replaced.Kind);
        Assert.True(replaced.OldValue.IsSupplied);
        Assert.True(replaced.NewValue.IsSupplied);
        Assert.Equal(ChangeKind.COLLECTION_ITEMS_MOVED, moved.Kind);
        Assert.Equal(0, moved.OldIndex);
        Assert.Equal(1, moved.NewIndex);
        Assert.Equal(ChangeKind.COLLECTION_ITEMS_REMOVED, removed.Kind);
        Assert.Equal(ChangeKind.COLLECTION_RESET, reset.Kind);
        Assert.Equal(ChangeKind.SOURCE_REPLACED, sourceReplaced.Kind);
        Assert.Equal(ChangeKind.SOURCE_REPLACED, sameSourceReplaced.Kind);
        Assert.Equal(ChangeKind.COLLECTION_ITEMS_ADDED, current.Kind);
        Assert.Equal(new object?[] { "current" }, Assert.IsType<object?[]>(current.NewValue.Value));
        Assert.Equal(ChangeKind.COLLECTION_RESET, currentReset.Kind);
    }

    [Fact]
    public async Task CustomAdapterHasPrecedenceAndPreservesSuppliedNull()
    {
        var adapter = new _CustomAdapter();
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            ObservationAdapters = [adapter],
        });
        var handle = host.RegisterRoot("custom", new _CustomSource()).Value;
        var observed = await host.ObserveAsync(handle);
        await using var subscription = observed.Value;
        await using var changes = subscription.ReadAllAsync().GetAsyncEnumerator();

        adapter.Publish(new ObservationAdapterChange(
            "Value",
            ChangeKind.MEMBER_CHANGED,
            ObservationValue.Supplied(null),
            ObservationValue.Supplied(3)));
        var change = await _NextAsync(changes);

        Assert.True(change.OldValue.IsSupplied);
        Assert.Null(change.OldValue.Value);
        Assert.Equal(3, change.NewValue.Value);

        subscription.Dispose();
        Assert.True(adapter.WasDisposed);
    }

    [Fact]
    public async Task PollingUsesExposedReadsAndStopsAtConsumerDisposal()
    {
        var model = new _PollingModel();
        using var host = _CreateReflectionHost();
        var handle = host.RegisterRoot("polling", model).Value;
        var observed = await host.ObserveAsync(handle, new ObservationRequest
        {
            MemberId = nameof(_PollingModel.Count),
            Mode = ObservationMode.POLLING,
            PollingInterval = TimeSpan.FromMilliseconds(10),
        });
        await using var subscription = observed.Value;
        await using var changes = subscription.ReadAllAsync().GetAsyncEnumerator();

        model.Count = 4;
        var change = await _NextAsync(changes);

        Assert.Equal(0, change.OldValue.Value);
        Assert.Equal(4, change.NewValue.Value);
        Assert.True(change.OldValue.IsSupplied);
        Assert.True(change.NewValue.IsSupplied);
    }

    [Fact]
    public async Task SlowConsumerReceivesObservableOverflowAndNewestChanges()
    {
        var adapter = new _CustomAdapter();
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            ObservationAdapters = [adapter],
            CollectionLimits = new CollectionAccessLimits { ObservationBufferCapacity = 2 },
        });
        var handle = host.RegisterRoot("custom", new _CustomSource()).Value;
        var observed = await host.ObserveAsync(handle);
        await using var subscription = observed.Value;

        for (var value = 1; value <= 5; value++)
        {
            adapter.Publish(new ObservationAdapterChange(
                "Value",
                ChangeKind.MEMBER_CHANGED,
                ObservationValue.NotSupplied,
                ObservationValue.Supplied(value)));
        }

        await using var changes = subscription.ReadAllAsync().GetAsyncEnumerator();
        var overflow = await _NextAsync(changes);
        var fourth = await _NextAsync(changes);
        var fifth = await _NextAsync(changes);

        Assert.Equal(ChangeKind.BUFFER_OVERFLOW, overflow.Kind);
        Assert.Equal(3, overflow.DroppedChangeCount);
        Assert.Equal(4, fourth.NewValue.Value);
        Assert.Equal(5, fifth.NewValue.Value);
        Assert.True(overflow.OrderingToken < fourth.OrderingToken);
    }

    [Fact]
    public async Task HostDisposalDetachesHandlersAndCompletesSubscription()
    {
        var adapter = new _CustomAdapter();
        var host = new UIEngineHost(new UIEngineHostOptions
        {
            ObservationAdapters = [adapter],
        });
        var handle = host.RegisterRoot("custom", new _CustomSource()).Value;
        var subscription = (await host.ObserveAsync(handle)).Value;
        await using var changes = subscription.ReadAllAsync().GetAsyncEnumerator();

        host.Dispose();

        Assert.True(subscription.IsDisposed);
        Assert.True(adapter.WasDisposed);
        Assert.False(await changes.MoveNextAsync());
        Assert.Equal(
            InteractionErrorCode.HOST_DISPOSED,
            (await host.ObserveAsync(handle)).Error?.Code);
    }

    [Fact]
    public async Task AdapterFailuresUseStructuredRedactedDiagnostics()
    {
        const string SECRET = "secret-observation-value";
        var adapter = new _CustomAdapter();
        var loggerFactory = new RecordingLoggerFactory();
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            ObservationAdapters = [adapter],
            LoggerFactory = loggerFactory,
        });
        var handle = host.RegisterRoot("custom", new _CustomSource()).Value;
        var subscription = (await host.ObserveAsync(handle)).Value;
        await using var changes = subscription.ReadAllAsync().GetAsyncEnumerator();

        adapter.Fail(new InvalidOperationException(SECRET));
        adapter.Publish(new ObservationAdapterChange(
            "Value",
            ChangeKind.MEMBER_CHANGED,
            ObservationValue.NotSupplied,
            ObservationValue.Supplied(9)));
        var change = await _NextAsync(changes);

        var diagnostic = Assert.Single(
            loggerFactory.Entries,
            entry => entry.EventId == UIEngineDiagnosticEventIds.OBSERVATION_FAILED);
        Assert.Equal(handle.Identity.RuntimeId, diagnostic.Properties["RuntimeId"]);
        Assert.Equal(InteractionErrorCode.OBSERVATION_FAILED, diagnostic.Properties["ErrorCode"]);
        Assert.DoesNotContain(SECRET, diagnostic.Message, StringComparison.Ordinal);
        Assert.Null(diagnostic.Exception);
        Assert.Equal(9, change.NewValue.Value);
        subscription.Dispose();
    }

    private static UIEngineHost _CreateReflectionHost() =>
        new([new ReflectionObjectDescriptorProvider()]);

    private static async Task<ChangeRecord> _NextAsync(IAsyncEnumerator<ChangeRecord> changes)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        Assert.True(await changes.MoveNextAsync().AsTask().WaitAsync(timeout.Token));
        return changes.Current;
    }

    private sealed class _NotifyingModel : INotifyPropertyChanged, IStableDomainIdentity
    {
        private string _Name = "initial";

        public event PropertyChangedEventHandler? PropertyChanged;

        public string DomainIdentity => "model-1";

        [Expose]
        public string Name
        {
            get => _Name;
            set
            {
                _Name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        [Children]
        public _TestObservableCollection Items { get; private set; } = new();

        public void InvalidateAll() =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));

        public void ReplaceItems(_TestObservableCollection replacement)
        {
            Items = replacement;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Items)));
        }
    }

    private sealed class _PollingModel
    {
        [Expose]
        public int Count { get; set; }
    }

    private sealed class _TestObservableCollection : ObservableCollection<object?>
    {
        public void AddMany(params object?[] items) => OnCollectionChanged(
            new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Add,
                (IList)items,
                Count));

        public void RemoveMany(params object?[] items) => OnCollectionChanged(
            new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Remove,
                (IList)items,
                0));

        public void ReplaceMany(object?[] oldItems, object?[] newItems) => OnCollectionChanged(
            new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Replace,
                (IList)newItems,
                (IList)oldItems,
                0));

        public void MoveMany(object?[] items, int oldIndex, int newIndex) => OnCollectionChanged(
            new NotifyCollectionChangedEventArgs(
                NotifyCollectionChangedAction.Move,
                (IList)items,
                newIndex,
                oldIndex));

        public void Reset() => OnCollectionChanged(
            new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    private sealed class _CustomSource;

    private sealed class _CustomAdapter : IObservationAdapter
    {
        private Action<ObservationAdapterChange>? _Publish;
        private Action<Exception>? _ReportFailure;

        public bool WasDisposed { get; private set; }

        public bool CanObserve(Type objectType) => objectType == typeof(_CustomSource);

        public ValueTask<InteractionResult<IDisposable>> SubscribeAsync(
            ObservationAdapterContext context,
            Action<ObservationAdapterChange> publish,
            Action<Exception> reportFailure,
            CancellationToken cancellationToken = default)
        {
            _Publish = publish;
            _ReportFailure = reportFailure;
            return ValueTask.FromResult(InteractionResult.Success<IDisposable>(
                new _CallbackDisposable(() => WasDisposed = true)));
        }

        public void Publish(ObservationAdapterChange change) => _Publish!(change);

        public void Fail(Exception exception) => _ReportFailure!(exception);
    }

    private sealed class _CallbackDisposable(Action callback) : IDisposable
    {
        private Action? _Callback = callback;

        public void Dispose() => Interlocked.Exchange(ref _Callback, null)?.Invoke();
    }
}
