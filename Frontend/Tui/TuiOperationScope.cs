namespace UIEngine.Frontend.Tui;

/// <summary>Owns work and resources for one independent frontend lifetime.</summary>
internal sealed class TuiOperationScope : IDisposable
{
    private readonly object _Gate = new();
    private readonly CancellationTokenSource _Cancellation = new();
    private readonly HashSet<TuiOperationScope> _Children = [];
    private readonly HashSet<Task> _Tasks = [];
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

    public Task RunAsync(Func<CancellationToken, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return _Start(() => operation(_Cancellation.Token));
    }

    public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return _Start(() => operation(_Cancellation.Token));
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

        _Cancellation.Cancel();
        foreach (var child in children)
        {
            child.Dispose();
        }

        foreach (var resource in resources)
        {
            resource.Dispose();
        }

        _WaitForTasks();
        _Cancellation.Dispose();
        _OnDisposed?.Invoke(this);
    }

    private Task _Start(Func<Task> operation)
    {
        lock (_Gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            var task = Task.Run(operation);
            _Track(task);
            return task;
        }
    }

    private Task<T> _Start<T>(Func<Task<T>> operation)
    {
        lock (_Gate)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            var task = Task.Run(operation);
            _Track(task);
            return task;
        }
    }

    private void _Track(Task task)
    {
        _Tasks.Add(task);
        _ = task.ContinueWith(
            completed =>
            {
                _ = completed.Exception;
                lock (_Gate)
                {
                    _Tasks.Remove(completed);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void _WaitForTasks()
    {
        while (true)
        {
            Task[] tasks;
            lock (_Gate)
            {
                tasks = [.. _Tasks];
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
                // Cancellation is the expected result of ending an owning scope.
            }
            catch (Exception)
            {
                // The continuation observes faults. Disposal must still join remaining work.
            }
        }
    }

    private void _RemoveChild(TuiOperationScope child)
    {
        lock (_Gate)
        {
            _Children.Remove(child);
        }
    }
}
