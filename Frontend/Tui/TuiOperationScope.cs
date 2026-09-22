namespace UIEngine.Frontend.Tui;

/// <summary>Starts work and owns disposable resources for one frontend lifetime.</summary>
internal sealed class TuiOperationScope : IDisposable
{
    private readonly object _Gate = new();
    private readonly HashSet<TuiOperationScope> _Children = [];
    private readonly HashSet<IDisposable> _Resources = [];
    private readonly Action<TuiOperationScope>? _OnDisposed;
    private int _Disposed;

    public TuiOperationScope(Action<TuiOperationScope>? onDisposed = null)
    {
        _OnDisposed = onDisposed;
    }

    public bool IsDisposed => Volatile.Read(ref _Disposed) != 0;

    public TuiOperationScope CreateChild()
    {
        lock (_Gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            var child = new TuiOperationScope(_RemoveChild);
            _Children.Add(child);
            return child;
        }
    }

    public Task RunAsync(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_Gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            var task = Task.Run(operation);
            _ObserveFailure(task);
            return task;
        }
    }

    public Task<T> RunAsync<T>(Func<Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_Gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            var task = Task.Run(operation);
            _ObserveFailure(task);
            return task;
        }
    }

    public bool TryOwn(IDisposable resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_Gate)
        {
            if (!IsDisposed)
            {
                _Resources.Add(resource);
                return true;
            }
        }

        resource.Dispose();
        return false;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _Disposed, 1) != 0)
        {
            return;
        }

        TuiOperationScope[] children;
        IDisposable[] resources;
        lock (_Gate)
        {
            children = [.. _Children];
            _Children.Clear();
            resources = [.. _Resources];
            _Resources.Clear();
        }

        foreach (var child in children)
        {
            child.Dispose();
        }

        foreach (var resource in resources)
        {
            resource.Dispose();
        }

        _OnDisposed?.Invoke(this);
    }

    private void _RemoveChild(TuiOperationScope child)
    {
        lock (_Gate)
        {
            _Children.Remove(child);
        }
    }

    private static void _ObserveFailure(Task task) => _ = task.ContinueWith(
        static completed => _ = completed.Exception,
        default,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);
}
