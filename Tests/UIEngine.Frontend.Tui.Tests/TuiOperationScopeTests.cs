using System.ComponentModel;
using UIEngine.Core;
using UIEngine.Core.Attributes;
using UIEngine.Frontend.Tui;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Hosting;
using Xunit;

namespace UIEngine.Frontend.Tui.Tests;

[Collection("Terminal application")]
public sealed class TuiOperationScopeTests
{
    [Fact]
    public async Task DirectCoreCallMarshalsStateChangeWithToolkitDispatcher()
    {
        var backend = new InMemoryTerminalBackend(new TerminalSize(40, 10));
        using var terminal = Terminal.Open(backend, new TerminalOptions(), force: true);
        using var host = new UIEngineHost();
        host.SetRoot("model", new object());
        using var workspace = TuiFrontend.CreateWorkspace(host);
        using var scope = workspace.CreateOperationScope();
        await using var app = new TerminalApp(
            workspace.Visual,
            terminal.Instance,
            new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen });
        var commandThread = 0;
        var stateChangeThread = 0;

        app.Post(() =>
        {
            commandThread = Environment.CurrentManagedThreadId;
            var operation = scope.RunAsync(() => host.ResolvePathAsync(
                LogicalPath.Root.Append("model")));
            _ = operation.ContinueWith(
                completed => app.Post(() =>
                {
                    Assert.True(completed.Result.IsSuccess);
                    stateChangeThread = Environment.CurrentManagedThreadId;
                    app.Stop();
                }),
                default,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        });

        await app.RunAsync(default).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotEqual(0, commandThread);
        Assert.Equal(commandThread, stateChangeThread);
    }

    [Fact]
    public async Task DisposingOneScopeDoesNotStopInFlightOrUnrelatedReads()
    {
        var domainDispatcher = new _OrderedDomainDispatcher();
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            Dispatcher = domainDispatcher,
        });
        host.SetRoot("first", new object());
        host.SetRoot("second", new object());
        using var workspace = TuiFrontend.CreateWorkspace(host);
        var retiredScope = workspace.CreateOperationScope();
        using var currentScope = workspace.CreateOperationScope();

        var retiredRead = retiredScope.RunAsync(() => host.ResolvePathAsync(
            LogicalPath.Root.Append("first")));
        await domainDispatcher.FirstInvocationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var currentRead = await currentScope.RunAsync(() => host.ResolvePathAsync(
            LogicalPath.Root.Append("second")));
        await Task.Run(retiredScope.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(retiredRead.IsCompleted);
        domainDispatcher.ReleaseFirst();
        var retiredResult = await retiredRead.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(currentRead.IsSuccess);
        Assert.True(retiredResult.IsSuccess);
        Assert.True(retiredScope.IsDisposed);
        Assert.False(currentScope.IsDisposed);
        Assert.False(host.IsDisposed);
    }

    [Fact]
    public async Task WorkspaceDisposalLeavesAnInFlightReadAndHostAlive()
    {
        var domainDispatcher = new _BlockingDomainDispatcher();
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            Dispatcher = domainDispatcher,
        });
        host.SetRoot("model", new object());
        var workspace = TuiFrontend.CreateWorkspace(host);
        var scope = workspace.CreateOperationScope();
        var read = scope.RunAsync(() => host.ResolvePathAsync(
            LogicalPath.Root.Append("model")));
        await domainDispatcher.InvocationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Task.Run(workspace.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(read.IsCompleted);
        domainDispatcher.Release();
        var result = await read.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(result.IsSuccess);
        Assert.True(scope.IsDisposed);
        Assert.False(host.IsDisposed);
        Assert.Throws<ObjectDisposedException>(workspace.CreateOperationScope);
    }

    [Fact]
    public async Task ScopeDisposalDetachesOwnedObservationAndStopsItsReader()
    {
        var model = new _ObservableModel();
        using var host = new UIEngineHost();
        var root = host.SetRoot("model", model);
        var observed = await host.ObserveAsync(root.Value, nameof(_ObservableModel.Value));
        using var workspace = TuiFrontend.CreateWorkspace(host);
        var scope = workspace.CreateOperationScope();
        Assert.True(scope.TryOwn(observed.Value));
        var presented = new TaskCompletionSource<ChangeRecord>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = scope.RunAsync(async () =>
        {
            await foreach (var change in observed.Value.ReadAllAsync())
            {
                presented.TrySetResult(change);
            }
        });

        Assert.Equal(1, model.PropertyHandlerCount);
        model.Value = 1;
        var change = await presented.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ChangeKind.MEMBER_CHANGED, change.Kind);

        await Task.Run(scope.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
        await reader.WaitAsync(TimeSpan.FromSeconds(5));
        model.Value = 2;

        Assert.Equal(0, model.PropertyHandlerCount);
        Assert.False(host.IsDisposed);
    }

    [Fact]
    public async Task ScopeDisposalDoesNotStopAHostOwnedInvocationOrProgressReader()
    {
        var model = new _ActionModel();
        using var host = new UIEngineHost();
        var root = host.SetRoot("model", model);
        var described = await host.DescribeAsync(root.Value);
        var action = Assert.Single(described.Value.Members.OfType<ActionDescriptor>());
        var started = await action.InvokeAsync(new Dictionary<string, object?>());
        using var workspace = TuiFrontend.CreateWorkspace(host);
        var scope = workspace.CreateOperationScope();
        var progressRead = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var reader = scope.RunAsync(async () =>
        {
            await foreach (var _ in started.Value.ReadProgressAsync())
            {
                progressRead.TrySetResult();
            }
        });
        await progressRead.Task.WaitAsync(TimeSpan.FromSeconds(5));

        scope.Dispose();
        Assert.False(reader.IsCompleted);

        Assert.Equal(InvocationStatus.RUNNING, started.Value.Status);
        Assert.False(host.IsDisposed);

        model.Release();
        var completion = await started.Value.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(completion.IsSuccess);
        Assert.Equal(InvocationStatus.SUCCEEDED, started.Value.Status);
        await reader.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task DisposedScopeRejectsNewWorkAndDisposesNewResources()
    {
        var scope = new TuiOperationScope();
        scope.Dispose();
        var resource = new _TrackedResource();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            scope.RunAsync(() => Task.CompletedTask));
        Assert.Throws<ObjectDisposedException>(scope.CreateChild);
        Assert.False(scope.TryOwn(resource));
        Assert.True(resource.IsDisposed);
    }

    private sealed class _OrderedDomainDispatcher : IInteractionDispatcher
    {
        private readonly AsyncLocal<bool> _Inside = new();
        private readonly TaskCompletionSource _ReleaseFirst =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _InvocationCount;

        public TaskCompletionSource FirstInvocationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CheckAccess() => _Inside.Value;

        public async Task<T> InvokeAsync<T>(Func<Task<T>> action)
        {
            var invocation = Interlocked.Increment(ref _InvocationCount);
            if (invocation == 1)
            {
                FirstInvocationStarted.TrySetResult();
                await _ReleaseFirst.Task;
            }

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

        public void ReleaseFirst() => _ReleaseFirst.TrySetResult();
    }

    private sealed class _BlockingDomainDispatcher : IInteractionDispatcher
    {
        private readonly TaskCompletionSource _Never =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource InvocationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CheckAccess() => false;

        public async Task<T> InvokeAsync<T>(Func<Task<T>> action)
        {
            InvocationStarted.TrySetResult();
            await _Never.Task;
            return await action();
        }

        public void Release() => _Never.TrySetResult();
    }

    private sealed class _TrackedResource : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public void Dispose() => IsDisposed = true;
    }

    private sealed class _ObservableModel : INotifyPropertyChanged
    {
        private PropertyChangedEventHandler? _PropertyChanged;
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
                _Value = value;
                _PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }
    }

    private sealed class _ActionModel
    {
        private readonly TaskCompletionSource _Release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _Release.TrySetResult();

        [UIEngine.Core.Attributes.Action]
        public async Task WorkAsync(IProgress<int> progress)
        {
            progress.Report(1);
            await _Release.Task;
        }
    }
}
