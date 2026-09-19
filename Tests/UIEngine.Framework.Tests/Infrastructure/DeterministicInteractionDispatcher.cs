using UIEngine.Core;

namespace UIEngine.Framework.Tests;

/// <summary>Queues interactions so tests decide exactly when configured work executes.</summary>
internal sealed class DeterministicInteractionDispatcher : IInteractionDispatcher
{
    private readonly Queue<Action> _Pending = new();
    private bool _HasAccess;

    public int InvocationCount { get; private set; }

    public int PendingCount => _Pending.Count;

    public bool CheckAccess() => _HasAccess;

    public ValueTask<T> InvokeAsync<T>(
        Func<T> action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        InvocationCount++;
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var cancellationRegistration = cancellationToken.Register(
            static state => ((TaskCompletionSource<T>)state!).TrySetCanceled(),
            completion);
        _Pending.Enqueue(() =>
        {
            if (completion.Task.IsCompleted)
            {
                cancellationRegistration.Dispose();
                return;
            }

            try
            {
                _HasAccess = true;
                completion.SetResult(action());
            }
            catch (Exception exception)
            {
                completion.SetException(exception);
            }
            finally
            {
                _HasAccess = false;
                cancellationRegistration.Dispose();
            }
        });
        return new ValueTask<T>(completion.Task);
    }

    public async ValueTask InvokeAsync(
        Action action,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        await InvokeAsync(
            () =>
            {
                action();
                return true;
            },
            cancellationToken).ConfigureAwait(false);
    }

    public void ExecuteNext()
    {
        if (!_Pending.TryDequeue(out var work))
        {
            throw new InvalidOperationException("No dispatched interaction is pending.");
        }

        work();
    }
}
