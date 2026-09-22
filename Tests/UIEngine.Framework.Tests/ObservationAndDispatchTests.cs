using System.Collections.ObjectModel;
using System.ComponentModel;
using UIEngine.Core;
using UIEngine.Core.Attributes;
using Xunit;

namespace UIEngine.Framework.Tests;

public sealed class ObservationAndDispatchTests
{
    [Fact]
    public async Task NotificationObservationReportsPropertiesAndCollections()
    {
        var model = new _ObservableModel();
        using var host = new UIEngineHost();
        var root = host.SetRoot("model", model);

        var valueResult = await host.ObserveAsync(root.Value, nameof(_ObservableModel.Value));
        using var valueSubscription = valueResult.Value;
        await using var valueReader = valueSubscription.ReadAllAsync().GetAsyncEnumerator();
        model.Value = 4;

        Assert.True(await valueReader.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(ChangeKind.MEMBER_CHANGED, valueReader.Current.Kind);
        Assert.Equal(nameof(_ObservableModel.Value), valueReader.Current.MemberId);

        var itemsResult = await host.ObserveAsync(root.Value, nameof(_ObservableModel.Items));
        using var itemsSubscription = itemsResult.Value;
        await using var itemsReader = itemsSubscription.ReadAllAsync().GetAsyncEnumerator();
        model.Items.Add("new");

        Assert.True(await itemsReader.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(ChangeKind.COLLECTION_ITEMS_ADDED, itemsReader.Current.Kind);
        Assert.Equal(0, itemsReader.Current.NewIndex);
    }

    [Fact]
    public async Task CollectionObservationFollowsReplacementAndDetachesOnDispose()
    {
        var model = new _ObservableModel();
        using var host = new UIEngineHost();
        var root = host.SetRoot("model", model);
        var observed = await host.ObserveAsync(root.Value, nameof(_ObservableModel.Items));
        var subscription = observed.Value;
        await using var reader = subscription.ReadAllAsync().GetAsyncEnumerator();

        model.Items = [];
        Assert.True(await reader.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(ChangeKind.SOURCE_REPLACED, reader.Current.Kind);

        await Task.Delay(25);
        model.Items.Add("replacement");
        Assert.True(await reader.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(ChangeKind.COLLECTION_ITEMS_ADDED, reader.Current.Kind);

        Assert.Equal(1, model.PropertyHandlerCount);
        subscription.Dispose();
        Assert.Equal(0, model.PropertyHandlerCount);
    }

    [Fact]
    public async Task ExplicitPollingReportsOldAndNewValues()
    {
        var model = new _PollingModel();
        using var host = new UIEngineHost();
        var root = host.SetRoot("model", model);
        var observed = await host.ObserveAsync(
            root.Value,
            nameof(_PollingModel.Value),
            TimeSpan.FromMilliseconds(10));
        using var subscription = observed.Value;
        await using var reader = subscription.ReadAllAsync().GetAsyncEnumerator();

        model.Value = 9;

        Assert.True(await reader.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(0, reader.Current.OldValue.Value);
        Assert.Equal(9, reader.Current.NewValue.Value);
    }

    [Fact]
    public async Task ObservationOverflowIsVisible()
    {
        var model = new _ObservableModel();
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            ObservationBufferCapacity = 1,
        });
        var root = host.SetRoot("model", model);
        var observed = await host.ObserveAsync(root.Value, nameof(_ObservableModel.Value));
        using var subscription = observed.Value;

        model.Value = 1;
        model.Value = 2;
        model.Value = 3;

        await using var reader = subscription.ReadAllAsync().GetAsyncEnumerator();
        Assert.True(await reader.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(ChangeKind.BUFFER_OVERFLOW, reader.Current.Kind);
        Assert.Equal(2, reader.Current.DroppedChangeCount);
        Assert.True(await reader.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(ChangeKind.MEMBER_CHANGED, reader.Current.Kind);
    }

    [Fact]
    public async Task DispatcherRoutesOperationsAndAllowsReentrantAccess()
    {
        var dispatcher = new _RecordingDispatcher();
        using var host = new UIEngineHost(new UIEngineHostOptions { Dispatcher = dispatcher });
        var model = new _ReentrantModel();
        var root = host.SetRoot("model", model);
        model.Attach(host, root.Value);
        var described = await host.DescribeAsync(root.Value);
        var value = Assert.Single(described.Value.Members.OfType<ValueDescriptor>());

        var read = await value.ReadAsync();

        Assert.True(read.IsSuccess);
        Assert.Equal(17, read.Value);
        Assert.True(dispatcher.InvocationCount > 0);
        Assert.True(model.ReentrantDescribeSucceeded);
    }

    [Fact]
    public async Task QueuedDisposalIsNormalizedByTheHost()
    {
        var disposedDispatcher = new _QueuedDispatcher();
        var disposedHost = new UIEngineHost(new UIEngineHostOptions
        {
            Dispatcher = disposedDispatcher,
        });
        var disposedRoot = disposedHost.SetRoot("model", new _PollingModel());
        var disposedTask = disposedHost.DescribeAsync(disposedRoot.Value);
        disposedHost.Dispose();
        disposedDispatcher.Release();
        var disposed = await disposedTask;
        Assert.Equal(InteractionErrorCode.DISPOSED, disposed.Error?.Code);
    }

    private sealed class _ObservableModel : INotifyPropertyChanged
    {
        private PropertyChangedEventHandler? _PropertyChanged;
        private ObservableCollection<string> _Items = [];
        private int _Value;

        public event PropertyChangedEventHandler? PropertyChanged
        {
            add
            {
                _PropertyChanged += value;
                PropertyHandlerCount++;
            }
            remove
            {
                _PropertyChanged -= value;
                PropertyHandlerCount--;
            }
        }

        public int PropertyHandlerCount { get; private set; }

        [Expose]
        public int Value
        {
            get => _Value;
            set
            {
                if (_Value == value)
                {
                    return;
                }

                _Value = value;
                _PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        [Children]
        public ObservableCollection<string> Items
        {
            get => _Items;
            set
            {
                _Items = value;
                _PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Items)));
            }
        }
    }

    private sealed class _PollingModel
    {
        [Expose]
        public int Value { get; set; }
    }

    private sealed class _ReentrantModel
    {
        private UIEngineHost? _Host;
        private ObjectHandle _Handle;

        public bool ReentrantDescribeSucceeded { get; private set; }

        [Expose]
        public int Value
        {
            get
            {
                ReentrantDescribeSucceeded = _Host!.DescribeAsync(_Handle)
                    .GetAwaiter()
                    .GetResult()
                    .IsSuccess;
                return 17;
            }
        }

        public void Attach(UIEngineHost host, ObjectHandle handle)
        {
            _Host = host;
            _Handle = handle;
        }
    }

    private sealed class _RecordingDispatcher : IInteractionDispatcher
    {
        private readonly AsyncLocal<bool> _Inside = new();

        public int InvocationCount { get; private set; }

        public bool CheckAccess() => _Inside.Value;

        public async Task<T> InvokeAsync<T>(Func<Task<T>> action)
        {
            InvocationCount++;
            _Inside.Value = true;
            try
            {
                return await action();
            }
            finally
            {
                _Inside.Value = false;
            }
        }
    }

    private sealed class _QueuedDispatcher : IInteractionDispatcher
    {
        private readonly TaskCompletionSource _Released =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CheckAccess() => false;

        public async Task<T> InvokeAsync<T>(Func<Task<T>> action)
        {
            await _Released.Task;
            return await action();
        }

        public void Release() => _Released.TrySetResult();
    }
}
