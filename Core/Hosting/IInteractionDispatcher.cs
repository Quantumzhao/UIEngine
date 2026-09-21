namespace UIEngine.Core;

/// <summary>Runs domain interactions on the execution context selected by the host.</summary>
public interface IInteractionDispatcher
{
    bool CheckAccess();

    Task<T> InvokeAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken = default);
}

public sealed class InlineInteractionDispatcher : IInteractionDispatcher
{
    private InlineInteractionDispatcher()
    {
    }

    public static InlineInteractionDispatcher Instance { get; } = new();

    public bool CheckAccess() => true;

    public Task<T> InvokeAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return action();
    }
}
