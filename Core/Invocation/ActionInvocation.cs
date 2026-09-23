namespace UIEngine.Core;

public enum InvocationStatus
{
    RUNNING,
    SUCCEEDED,
    FAILED,
}

/// <summary>One running or completed domain action.</summary>
public sealed class ActionInvocation
{
    private readonly object _Gate = new();
    private readonly TaskCompletionSource<InteractionResult<object?>> _Completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Action<ActionInvocation> _OnCompleted;
    private int _Terminal;
    private InvocationStatus _Status = InvocationStatus.RUNNING;
    private Exception? _Fault;

    internal ActionInvocation(Action<ActionInvocation> onCompleted)
    {
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

        _Completion.TrySetResult(result);
        _OnCompleted(this);
    }
}
