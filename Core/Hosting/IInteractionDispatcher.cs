namespace UIEngine.Core;

/// <summary>Runs domain interactions on the execution context selected by the host.</summary>
public interface IInteractionDispatcher
{
    /// <summary>Returns whether the caller is already running on this dispatcher's context.</summary>
    bool CheckAccess();

    ValueTask<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken = default);

    ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default);
}

/// <summary>Executes interactions immediately on the calling thread.</summary>
public sealed class InlineInteractionDispatcher : IInteractionDispatcher
{
    private InlineInteractionDispatcher()
    {
    }

    public static InlineInteractionDispatcher Instance { get; } = new();

    public bool CheckAccess() => true;

    public ValueTask<T> InvokeAsync<T>(
        Func<T> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(action());
    }

    public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        cancellationToken.ThrowIfCancellationRequested();
        action();
        return ValueTask.CompletedTask;
    }
}
