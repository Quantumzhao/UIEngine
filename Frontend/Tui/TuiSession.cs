using System.Threading.Channels;
using UIEngine.Core;

namespace UIEngine.Frontend.Tui;

/// <summary>Owns the state and resources for one TUI workspace.</summary>
/// <remarks>
/// Disposing a session releases only frontend-owned resources. The <see cref="UIEngineHost"/>
/// supplied by the caller remains caller-owned and is never disposed by this type.
/// </remarks>
public sealed class TuiSession : IDisposable
{
    private readonly object _Gate = new();
    private readonly CancellationTokenSource _LifetimeCancellation = new();
    private readonly Channel<QueuedTuiIntent> _Intents = Channel.CreateUnbounded<QueuedTuiIntent>(
        new UnboundedChannelOptions { SingleReader = true });
    private readonly HashSet<Task> _OwnedTasks = [];
    private readonly ITuiPresentationDispatcher _PresentationDispatcher;
    private readonly Task _PumpTask;
    private CancellationTokenSource? _GenerationCancellation;
    private List<CancellationTokenSource> _RetiredGenerationCancellations = [];
    private List<IDisposable> _GenerationResources = [];
    private List<ActionInvocation> _GenerationInvocations = [];
    private int _PendingIntentCount;
    private long _Generation;
    private int _Disposed;

    internal TuiSession(
        UIEngineHost host,
        TuiFrontendOptions options,
        ITuiPresentationDispatcher? presentationDispatcher = null)
    {
        Host = host;
        Options = options;
        _PresentationDispatcher = presentationDispatcher ?? ToolkitTuiPresentationDispatcher.Instance;
        _GenerationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _LifetimeCancellation.Token);
        _PumpTask = Task.Run(_PumpAsync);
    }

    /// <summary>Gets the immutable configuration snapshot used by this session.</summary>
    public TuiFrontendOptions Options { get; }

    /// <summary>Gets whether this session has been disposed.</summary>
    public bool IsDisposed => Volatile.Read(ref _Disposed) != 0;

    internal UIEngineHost Host { get; }

    internal long CurrentGeneration => Interlocked.Read(ref _Generation);

    internal bool TryReadPath(
        LogicalPath path,
        Action<InteractionResult<ResolvedPath>> present)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(present);
        return _TryEnqueue(new ReadPathIntent(path, present));
    }

    internal bool TryOwnObservation(
        ObservationSubscription subscription,
        Action<ChangeRecord> present)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        ArgumentNullException.ThrowIfNull(present);

        var generation = CurrentGeneration;
        if (!_TryAddGenerationResource(generation, subscription, out var cancellationToken))
        {
            subscription.Dispose();
            return false;
        }

        return _StartOwnedTask(() => _ReadObservationAsync(
            subscription,
            present,
            generation,
            cancellationToken));
    }

    internal bool TryOwnInvocation(
        ActionInvocation invocation,
        Action<InvocationProgress> presentProgress,
        Action<InteractionResult<object?>> presentCompletion)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        ArgumentNullException.ThrowIfNull(presentProgress);
        ArgumentNullException.ThrowIfNull(presentCompletion);

        var generation = CurrentGeneration;
        CancellationToken cancellationToken;
        lock (_Gate)
        {
            if (IsDisposed || generation != _Generation || _GenerationCancellation is null)
            {
                invocation.Cancel();
                return false;
            }

            _GenerationInvocations.Add(invocation);
            cancellationToken = _GenerationCancellation.Token;
        }

        var progressStarted = _StartOwnedTask(() => _ReadProgressAsync(
            invocation,
            presentProgress,
            generation,
            cancellationToken));
        var completionStarted = _StartOwnedTask(() => _ReadCompletionAsync(
            invocation,
            presentCompletion,
            generation,
            cancellationToken));
        return progressStarted && completionStarted;
    }

    /// <summary>Releases frontend-owned resources without disposing the caller-owned host.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _Disposed, 1) != 0)
        {
            return;
        }

        CancellationTokenSource? generationCancellation;
        List<CancellationTokenSource> retiredCancellations;
        List<IDisposable> resources;
        List<ActionInvocation> invocations;
        lock (_Gate)
        {
            generationCancellation = _GenerationCancellation;
            _GenerationCancellation = null;
            retiredCancellations = _RetiredGenerationCancellations;
            _RetiredGenerationCancellations = [];
            resources = _GenerationResources;
            _GenerationResources = [];
            invocations = _GenerationInvocations;
            _GenerationInvocations = [];
        }

        generationCancellation?.Cancel();
        _LifetimeCancellation.Cancel();
        _Intents.Writer.TryComplete();
        _CancelAndDispose(invocations, resources);

        _PumpTask.GetAwaiter().GetResult();
        _WaitForOwnedTasks();

        generationCancellation?.Dispose();
        foreach (var retiredCancellation in retiredCancellations)
        {
            retiredCancellation.Dispose();
        }

        _LifetimeCancellation.Dispose();
    }

    private bool _TryEnqueue(TuiIntent intent)
    {
        CancellationTokenSource? previousCancellation;
        List<IDisposable> previousResources;
        List<ActionInvocation> previousInvocations;
        CancellationTokenSource cancellation;
        long generation;

        lock (_Gate)
        {
            if (IsDisposed)
            {
                return false;
            }

            generation = ++_Generation;
            previousCancellation = _GenerationCancellation;
            if (previousCancellation is not null)
            {
                _RetiredGenerationCancellations.Add(previousCancellation);
            }

            previousResources = _GenerationResources;
            previousInvocations = _GenerationInvocations;
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _LifetimeCancellation.Token);
            _GenerationCancellation = cancellation;
            _GenerationResources = [];
            _GenerationInvocations = [];
            _PendingIntentCount++;
        }

        previousCancellation?.Cancel();
        _CancelAndDispose(previousInvocations, previousResources);

        if (_Intents.Writer.TryWrite(new QueuedTuiIntent(
                intent,
                generation,
                cancellation.Token)))
        {
            return true;
        }

        cancellation.Cancel();
        cancellation.Dispose();
        return false;
    }

    private async Task _PumpAsync()
    {
        try
        {
            await foreach (var queued in _Intents.Reader.ReadAllAsync(
                               _LifetimeCancellation.Token))
            {
                _StartOwnedTask(() => _ExecuteAsync(queued));
                _IntentStarted();
            }
        }
        catch (OperationCanceledException) when (_LifetimeCancellation.IsCancellationRequested)
        {
            // Session disposal is the normal way the pump stops.
        }
    }

    private async Task _ExecuteAsync(QueuedTuiIntent queued)
    {
        try
        {
            switch (queued.Intent)
            {
                case ReadPathIntent read:
                    var result = await Host.ResolvePathAsync(read.Path, queued.CancellationToken);
                    _PostPresentation(queued.Generation, () => read.Present(result));
                    break;
                default:
                    throw new InvalidOperationException(
                        $"Unsupported TUI intent type '{queued.Intent.GetType().Name}'.");
            }
        }
        catch (OperationCanceledException) when (queued.CancellationToken.IsCancellationRequested)
        {
            // Superseded and disposed operations do not publish presentation updates.
        }
    }

    private async Task _ReadObservationAsync(
        ObservationSubscription subscription,
        Action<ChangeRecord> present,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var change in subscription.ReadAllAsync(cancellationToken))
            {
                _PostPresentation(generation, () => present(change));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The owning screen changed or the session was disposed.
        }
    }

    private async Task _ReadProgressAsync(
        ActionInvocation invocation,
        Action<InvocationProgress> present,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var progress in invocation.ReadProgressAsync(cancellationToken))
            {
                _PostPresentation(generation, () => present(progress));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The owning screen changed or the session was disposed.
        }
    }

    private async Task _ReadCompletionAsync(
        ActionInvocation invocation,
        Action<InteractionResult<object?>> present,
        long generation,
        CancellationToken cancellationToken)
    {
        try
        {
            var completion = await invocation.Completion.WaitAsync(cancellationToken);
            _PostPresentation(generation, () => present(completion));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The owning screen changed or the session was disposed.
        }
    }

    private bool _TryAddGenerationResource(
        long generation,
        IDisposable resource,
        out CancellationToken cancellationToken)
    {
        lock (_Gate)
        {
            if (IsDisposed || generation != _Generation || _GenerationCancellation is null)
            {
                cancellationToken = default;
                return false;
            }

            _GenerationResources.Add(resource);
            cancellationToken = _GenerationCancellation.Token;
            return true;
        }
    }

    private void _PostPresentation(long generation, Action present)
    {
        if (IsDisposed || generation != CurrentGeneration)
        {
            return;
        }

        _PresentationDispatcher.Post(() =>
        {
            if (!IsDisposed && generation == CurrentGeneration)
            {
                present();
            }
        });
    }

    private bool _StartOwnedTask(Func<Task> operation)
    {
        Task task;
        lock (_Gate)
        {
            if (IsDisposed)
            {
                return false;
            }

            task = Task.Run(operation);
            _OwnedTasks.Add(task);
        }

        _ = task.ContinueWith(
            completed =>
            {
                _ = completed.Exception;
                List<CancellationTokenSource> retiredCancellations;
                lock (_Gate)
                {
                    _OwnedTasks.Remove(completed);
                    retiredCancellations = _TakeRetiredCancellationsIfIdle();
                }

                foreach (var retiredCancellation in retiredCancellations)
                {
                    retiredCancellation.Dispose();
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return true;
    }

    private void _IntentStarted()
    {
        List<CancellationTokenSource> retiredCancellations;
        lock (_Gate)
        {
            _PendingIntentCount--;
            retiredCancellations = _TakeRetiredCancellationsIfIdle();
        }

        foreach (var retiredCancellation in retiredCancellations)
        {
            retiredCancellation.Dispose();
        }
    }

    private List<CancellationTokenSource> _TakeRetiredCancellationsIfIdle()
    {
        if (_PendingIntentCount != 0 || _OwnedTasks.Count != 0)
        {
            return [];
        }

        var retiredCancellations = _RetiredGenerationCancellations;
        _RetiredGenerationCancellations = [];
        return retiredCancellations;
    }

    private void _WaitForOwnedTasks()
    {
        while (true)
        {
            Task[] tasks;
            lock (_Gate)
            {
                tasks = [.. _OwnedTasks];
            }

            if (tasks.Length == 0)
            {
                return;
            }

            try
            {
                Task.WhenAll(tasks).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                // Cancellation is expected during session disposal.
            }
            catch (Exception)
            {
                // Owned-task faults are observed by their completion continuation. Disposal still
                // has to join every remaining task and release all session resources.
            }
        }
    }

    private static void _CancelAndDispose(
        IEnumerable<ActionInvocation> invocations,
        IEnumerable<IDisposable> resources)
    {
        foreach (var invocation in invocations)
        {
            invocation.Cancel();
        }

        foreach (var resource in resources)
        {
            resource.Dispose();
        }
    }
}
