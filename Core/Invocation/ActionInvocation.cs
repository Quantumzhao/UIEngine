namespace UIEngine.Core;

public enum InvocationStatus
{
    RUNNING,
    SUCCEEDED,
    FAILED,
}

public sealed record InvocationProgress(long OrderingToken, object? Value, Type ValueType);

/// <summary>One running or completed domain action.</summary>
public sealed class ActionInvocation
{
    private readonly object _Gate = new();
    private readonly BoundedAsyncStream<InvocationProgress> _Progress;
    private readonly TaskCompletionSource<InteractionResult<object?>> _Completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<ActionInvocation> _OnCompleted;
    private int _Terminal;
    private long _OrderingToken;
    private InvocationStatus _Status = InvocationStatus.RUNNING;
    private Exception? _Fault;

    internal ActionInvocation(
        int progressCapacity,
        Action<ActionInvocation> onCompleted)
    {
        _Progress = new BoundedAsyncStream<InvocationProgress>(progressCapacity);
        _OnCompleted = onCompleted;
    }

    public InvocationStatus Status
    {
        get
        {
            lock (_Gate)
            {
                return _Status;
            }
        }
    }

    public Exception? Fault
    {
        get
        {
            lock (_Gate)
            {
                return _Fault;
            }
        }
    }

    public Task<InteractionResult<object?>> Completion => _Completion.Task;

    internal object CreateProgressReporter(Type progressType) =>
        Activator.CreateInstance(typeof(_ProgressReporter<>).MakeGenericType(progressType), this)!;

    internal void CompleteSuccess(object? value) => _Complete(
        InvocationStatus.SUCCEEDED,
        InteractionResult.Success(value),
        null);

    internal void CompleteFailure(Exception exception)
    {
        _Complete(
            InvocationStatus.FAILED,
            InteractionResult.Failure<object?>(
                exception is UnauthorizedAccessException
                    ? InteractionErrorCode.PERMISSION_DENIED
                    : InteractionErrorCode.FAULT,
                exception.Message),
            exception);
    }

    internal void CompleteHostDisposed()
    {
        _Complete(
            InvocationStatus.FAILED,
            InteractionResult.Failure<object?>(
                InteractionErrorCode.DISPOSED,
                "The UIEngine host was disposed during action invocation."),
            null);
    }

    public IAsyncEnumerable<InvocationProgress> ReadProgressAsync() => _Progress.ReadAllAsync();

    internal void ReportProgress(object? value, Type valueType) => _Progress.Publish(
        new InvocationProgress(Interlocked.Increment(ref _OrderingToken), value, valueType));

    private void _Complete(
        InvocationStatus status,
        InteractionResult<object?> result,
        Exception? fault)
    {
        if (Interlocked.Exchange(ref _Terminal, 1) != 0)
        {
            return;
        }

        lock (_Gate)
        {
            _Status = status;
            _Fault = fault;
        }

        _Progress.Complete();
        _Completion.TrySetResult(result);
        _OnCompleted(this);
    }

    private sealed class _ProgressReporter<T>(ActionInvocation owner) : IProgress<T>
    {
        public void Report(T value) => owner.ReportProgress(value, typeof(T));
    }
}
