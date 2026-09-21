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
public sealed class TuiSessionTests
{
    [Fact]
    public async Task SynchronousIntentEntryMarshalsPresentationToToolkitDispatcher()
    {
        var backend = new InMemoryTerminalBackend(new TerminalSize(40, 10));
        using var terminal = Terminal.Open(backend, new TerminalOptions(), force: true);
        using var host = new UIEngineHost();
        host.SetRoot("model", new object());
        using var workspace = TuiFrontend.CreateWorkspace(host);
        await using var app = new TerminalApp(
            workspace.Visual,
            terminal.Instance,
            new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen });
        var intentThread = 0;
        var presentationThread = 0;

        app.Post(() =>
        {
            intentThread = Environment.CurrentManagedThreadId;
            Assert.True(workspace.Session.TryReadPath(
                LogicalPath.Root.Append("model"),
                result =>
                {
                    Assert.True(result.IsSuccess);
                    presentationThread = Environment.CurrentManagedThreadId;
                    app.Stop();
                }));
        });

        await app.RunAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.NotEqual(0, intentThread);
        Assert.Equal(intentThread, presentationThread);
    }

    [Fact]
    public async Task NewIntentRejectsAStaleReadResult()
    {
        var domainDispatcher = new _OrderedDomainDispatcher();
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            Dispatcher = domainDispatcher,
        });
        host.SetRoot("first", new object());
        host.SetRoot("second", new object());
        var session = new TuiSession(
            host,
            new TuiFrontendOptions(),
            _ImmediatePresentationDispatcher.Instance);
        var stalePresented = false;
        var currentPresented = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(session.TryReadPath(
            LogicalPath.Root.Append("first"),
            _ => stalePresented = true));
        await domainDispatcher.FirstInvocationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(session.TryReadPath(
            LogicalPath.Root.Append("second"),
            result =>
            {
                Assert.True(result.IsSuccess);
                currentPresented.TrySetResult();
            }));
        await currentPresented.Task.WaitAsync(TimeSpan.FromSeconds(5));

        domainDispatcher.ReleaseFirstInvocation();
        session.Dispose();

        Assert.False(stalePresented);
        Assert.True(domainDispatcher.InvocationCount >= 2);
        Assert.False(host.IsDisposed);
    }

    [Fact]
    public async Task DisposalCancelsAnInFlightReadWithoutDisposingTheHost()
    {
        var domainDispatcher = new _BlockingDomainDispatcher();
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            Dispatcher = domainDispatcher,
        });
        host.SetRoot("model", new object());
        var session = new TuiSession(
            host,
            new TuiFrontendOptions(),
            _ImmediatePresentationDispatcher.Instance);
        var presented = false;

        Assert.True(session.TryReadPath(
            LogicalPath.Root.Append("model"),
            _ => presented = true));
        await domainDispatcher.InvocationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await Task.Run(session.Dispose).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(domainDispatcher.CancellationObserved);
        Assert.False(presented);
        Assert.False(host.IsDisposed);
        Assert.False(session.TryReadPath(LogicalPath.Root, _ => { }));
    }

    [Fact]
    public async Task PostedPresentationDoesNotUpdateADisposedWorkspace()
    {
        using var host = new UIEngineHost();
        host.SetRoot("model", new object());
        var presentationDispatcher = new _QueuedPresentationDispatcher();
        var session = new TuiSession(
            host,
            new TuiFrontendOptions(),
            presentationDispatcher);
        var presented = false;

        Assert.True(session.TryReadPath(
            LogicalPath.Root.Append("model"),
            _ => presented = true));
        await presentationDispatcher.Posted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        session.Dispose();
        presentationDispatcher.RunPosted();

        Assert.False(presented);
        Assert.False(host.IsDisposed);
    }

    [Fact]
    public async Task DisposalDetachesOwnedObservationAndStopsPresentation()
    {
        var model = new _ObservableModel();
        using var host = new UIEngineHost();
        var root = host.SetRoot("model", model);
        var observed = await host.ObserveAsync(root.Value, nameof(_ObservableModel.Value));
        var session = new TuiSession(
            host,
            new TuiFrontendOptions(),
            _ImmediatePresentationDispatcher.Instance);
        var presented = new TaskCompletionSource<ChangeRecord>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(session.TryOwnObservation(
            observed.Value,
            change => presented.TrySetResult(change)));
        Assert.Equal(1, model.PropertyHandlerCount);

        model.Value = 1;
        var change = await presented.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(ChangeKind.MEMBER_CHANGED, change.Kind);

        session.Dispose();
        model.Value = 2;

        Assert.Equal(0, model.PropertyHandlerCount);
        Assert.False(host.IsDisposed);
    }

    [Fact]
    public async Task DisposalCancelsOwnedInvocationProgressReader()
    {
        var model = new _ActionModel();
        using var host = new UIEngineHost();
        var root = host.SetRoot("model", model);
        var described = await host.DescribeAsync(root.Value);
        var action = Assert.Single(described.Value.Members.OfType<ActionDescriptor>());
        var started = await action.InvokeAsync(new Dictionary<string, object?>());
        var session = new TuiSession(
            host,
            new TuiFrontendOptions(),
            _ImmediatePresentationDispatcher.Instance);
        var progressPresented = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);

        Assert.True(session.TryOwnInvocation(
            started.Value,
            _ => progressPresented.TrySetResult(),
            _ => { }));
        await progressPresented.Task.WaitAsync(TimeSpan.FromSeconds(5));

        session.Dispose();
        var completion = await started.Value.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(InteractionErrorCode.CANCELLED, completion.Error?.Code);
        Assert.True(model.CancellationObserved);
        Assert.False(host.IsDisposed);
    }

    private sealed class _ImmediatePresentationDispatcher : ITuiPresentationDispatcher
    {
        public static _ImmediatePresentationDispatcher Instance { get; } = new();

        public void Post(Action action) => action();
    }

    private sealed class _QueuedPresentationDispatcher : ITuiPresentationDispatcher
    {
        private Action? _Posted;

        public TaskCompletionSource Posted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Post(Action action)
        {
            _Posted = action;
            Posted.TrySetResult();
        }

        public void RunPosted() => Interlocked.Exchange(ref _Posted, null)?.Invoke();
    }

    private sealed class _OrderedDomainDispatcher : IInteractionDispatcher
    {
        private readonly AsyncLocal<bool> _Inside = new();
        private readonly TaskCompletionSource _ReleaseFirst =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _InvocationCount;

        public TaskCompletionSource FirstInvocationStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int InvocationCount => Volatile.Read(ref _InvocationCount);

        public bool CheckAccess() => _Inside.Value;

        public async Task<T> InvokeAsync<T>(
            Func<Task<T>> action,
            CancellationToken cancellationToken = default)
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

        public void ReleaseFirstInvocation() => _ReleaseFirst.TrySetResult();
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
        public bool CancellationObserved { get; private set; }

        [UIEngine.Core.Attributes.Action]
        public async Task WorkAsync(
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            progress.Report(1);
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                CancellationObserved = true;
                throw;
            }
        }
    }
}
