using UIEngine.Core;
using UIEngine.Core.Attributes;
using Xunit;

namespace UIEngine.Framework.Tests;

public sealed class DispatchTests
{
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
        var disposedRoot = disposedHost.SetRoot("model", new _DispatcherModel());
        var disposedTask = disposedHost.DescribeAsync(disposedRoot.Value);
        disposedHost.Dispose();
        disposedDispatcher.Release();
        var disposed = await disposedTask;
        Assert.Equal(InteractionErrorCode.DISPOSED, disposed.Error?.Code);
    }

    private sealed class _DispatcherModel
    {
        [Expose]
        public int Value { get; set; }
    }

    private sealed class _ReentrantModel
    {
        private UIEngineHost? _Host;
        private Guid _Handle;

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

        public void Attach(UIEngineHost host, Guid handle)
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
