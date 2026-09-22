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
            var operation = scope.RunAsync(cancellationToken => host.ResolvePathAsync(
                LogicalPath.Root.Append("model"),
                cancellationToken));
            _ = operation.ContinueWith(
                completed => app.Post(() =>
                {
                    Assert.True(completed.Result.IsSuccess);
                    stateChangeThread = Environment.CurrentManagedThreadId;
                    app.Stop();
                }),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        });

        await app.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotEqual(0, commandThread);
        Assert.Equal(commandThread, stateChangeThread);
    }

    [Fact]
    public async Task DisposingOneScopeDoesNotCancelAnUnrelatedRead()
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

        var retiredRead = retiredScope.RunAsync(cancellationToken => host.ResolvePathAsync(
            LogicalPath.Root.Append("first"),
            cancellationToken));
        await domainDispatcher.FirstInvocationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var currentRead = await currentScope.RunAsync(cancellationToken => host.ResolvePathAsync(
            LogicalPath.Root.Append("second"),
            cancellationToken));
        await Task.Run(retiredScope.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
        var retiredResult = await retiredRead.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(currentRead.IsSuccess);
        Assert.Equal(InteractionErrorCode.CANCELLED, retiredResult.Error?.Code);
        Assert.True(retiredScope.IsDisposed);
        Assert.False(currentScope.IsDisposed);
        Assert.False(host.IsDisposed);
    }

    [Fact]
    public async Task WorkspaceDisposalCancelsAnInFlightReadWithoutDisposingTheHost()
    {
        var domainDispatcher = new _BlockingDomainDispatcher();
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            Dispatcher = domainDispatcher,
        });
        host.SetRoot("model", new object());
        var workspace = TuiFrontend.CreateWorkspace(host);
        var scope = workspace.CreateOperationScope();
        var read = scope.RunAsync(cancellationToken => host.ResolvePathAsync(
            LogicalPath.Root.Append("model"),
            cancellationToken));
        await domainDispatcher.InvocationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Task.Run(workspace.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
        var result = await read.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(InteractionErrorCode.CANCELLED, result.Error?.Code);
        Assert.True(domainDispatcher.CancellationObserved);
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
        var reader = scope.RunAsync(async cancellationToken =>
        {
            await foreach (var change in observed.Value.ReadAllAsync(cancellationToken))
            {
                presented.TrySetResult(change);
            }
        });

        Assert.Equal(1, model.PropertyHandlerCount);
        model.Value = 1;
        var change = await presented.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ChangeKind.MEMBER_CHANGED, change.Kind);

        await Task.Run(scope.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader);
        model.Value = 2;

        Assert.Equal(0, model.PropertyHandlerCount);
        Assert.False(host.IsDisposed);
    }

    [Fact]
    public async Task ScopeDisposalStopsProgressReaderWithoutCancellingInvocation()
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
        var reader = scope.RunAsync(async cancellationToken =>
        {
            await foreach (var _ in started.Value.ReadProgressAsync(cancellationToken))
            {
                progressRead.TrySetResult();
            }
        });
        await progressRead.Task.WaitAsync(TimeSpan.FromSeconds(5));

        scope.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader);

        Assert.Equal(InvocationStatus.RUNNING, started.Value.Status);
        Assert.False(started.Value.IsCancellationRequested);
        Assert.False(model.CancellationObserved);
        Assert.False(host.IsDisposed);

        model.Release();
        var completion = await started.Value.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(completion.IsSuccess);
        Assert.Equal(InvocationStatus.SUCCEEDED, started.Value.Status);
    }

    [Fact]
    public async Task DisposedScopeRejectsNewWorkAndDisposesNewResources()
    {
        var scope = new TuiOperationScope();
        scope.Dispose();
        var resource = new _TrackedResource();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            scope.RunAsync(_ => Task.CompletedTask));
        Assert.Throws<ObjectDisposedException>(scope.CreateChild);
        Assert.False(scope.TryOwn(resource));
        Assert.True(resource.IsDisposed);
    }

    private sealed class _OrderedDomainDispatcher : IInteractionDispatcher
    {
        private readonly AsyncLocal<bool> _Inside = new();
        private readonly TaskCompletionSource _Never =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _InvocationCount;

        public TaskCompletionSource FirstInvocationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CheckAccess() => _Inside.Value;

        public async Task<T> InvokeAsync<T>(
            Func<Task<T>> action,
            CancellationToken cancellationToken = default)
        {
            var invocation = Interlocked.Increment(ref _InvocationCount);
            if (invocation == 1)
            {
                FirstInvocationStarted.TrySetResult();
                await _Never.Task.WaitAsync(cancellationToken);
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
    }

    private sealed class _BlockingDomainDispatcher : IInteractionDispatcher
    {
        private readonly TaskCompletionSource _Never =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource InvocationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CancellationObserved { get; private set; }

        public bool CheckAccess() => false;

        public async Task<T> InvokeAsync<T>(
            Func<Task<T>> action,
            CancellationToken cancellationToken = default)
        {
            InvocationStarted.TrySetResult();
            try
            {
                await _Never.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }

            return await action();
        }
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

        public bool CancellationObserved { get; private set; }

        public void Release() => _Release.TrySetResult();

        [UIEngine.Core.Attributes.Action]
        public async Task WorkAsync(
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            progress.Report(1);
            try
            {
                await _Release.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }
}
