using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace UIEngine.Core;

/// <summary>Identifies the lifecycle state of one normalized action invocation.</summary>
public enum InvocationStatus
{
    CREATED,
    RUNNING,
    SUCCEEDED,
    FAILED,
    CANCELLED,
}

/// <summary>Describes one ordered progress value emitted by an action.</summary>
public sealed record InvocationProgress(
    long OrderingToken,
    object? Value,
    Type ValueType,
    DateTimeOffset Timestamp);

/// <summary>Retains trusted local fault details without requiring frontends to parse them.</summary>
public sealed record InvocationFault(string ExceptionType, string Message, Exception Exception);

/// <summary>Represents one action execution and its single terminal outcome.</summary>
public interface IActionInvocation
{
    InvocationStatus Status { get; }

    bool SupportsCancellation { get; }

    bool IsCancellationRequested { get; }

    InvocationFault? Fault { get; }

    Task<InteractionResult<object?>> Completion { get; }

    IAsyncEnumerable<InvocationProgress> ReadProgressAsync(
        CancellationToken cancellationToken = default);

    InteractionResult<bool> RequestCancellation();
}

/// <summary>Owns terminal-state, progress, and cancellation normalization for one action.</summary>
internal sealed class ActionInvocation : IActionInvocation
{
    private readonly object _Gate = new();
    private readonly Channel<InvocationProgress> _Progress;
    private readonly TaskCompletionSource<InteractionResult<object?>> _Completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource? _CancellationSource;
    private readonly Action<ActionInvocation> _OnCompleted;
    private readonly string _ActionId;
    private int _ProgressReaderStarted;
    private int _CancellationRequested;
    private int _Terminal;
    private long _OrderingToken;
    private InvocationStatus _Status = InvocationStatus.CREATED;
    private InvocationFault? _Fault;

    internal ActionInvocation(
        string actionId,
        bool supportsCancellation,
        int progressCapacity,
        Action<ActionInvocation> onCompleted)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(progressCapacity);
        ArgumentNullException.ThrowIfNull(onCompleted);
        _ActionId = actionId;
        SupportsCancellation = supportsCancellation;
        _Progress = Channel.CreateBounded<InvocationProgress>(new BoundedChannelOptions(progressCapacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false,
        });
        _OnCompleted = onCompleted;
        _CancellationSource = supportsCancellation ? new CancellationTokenSource() : null;
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

    public bool SupportsCancellation { get; }

    public bool IsCancellationRequested => Volatile.Read(ref _CancellationRequested) != 0;

    public InvocationFault? Fault
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

    internal CancellationToken CancellationToken => _CancellationSource?.Token ?? CancellationToken.None;

    internal object CreateProgressReporter(Type progressType)
    {
        var reporterType = typeof(_ProgressReporter<>).MakeGenericType(progressType);
        return Activator.CreateInstance(reporterType, this)!;
    }

    internal void Start()
    {
        lock (_Gate)
        {
            if (_Status == InvocationStatus.CREATED)
            {
                _Status = InvocationStatus.RUNNING;
            }
        }
    }

    internal void ReportProgress(object? value, Type valueType)
    {
        lock (_Gate)
        {
            if (_Terminal != 0)
            {
                return;
            }

            _Progress.Writer.TryWrite(new InvocationProgress(
                Interlocked.Increment(ref _OrderingToken),
                value,
                valueType,
                DateTimeOffset.UtcNow));
        }
    }

    internal void CompleteSuccess(object? value) => _Complete(
        InvocationStatus.SUCCEEDED,
        InteractionResult.Success(value),
        fault: null);

    internal void CompleteFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is OperationCanceledException cancelled &&
            SupportsCancellation &&
            IsCancellationRequested &&
            (cancelled.CancellationToken == CancellationToken ||
                cancelled.CancellationToken == default))
        {
            _Complete(
                InvocationStatus.CANCELLED,
                InteractionResult.Failure<object?>(
                    InteractionErrorCode.CANCELLED,
                    "Action invocation was cancelled."),
                fault: null);
            return;
        }

        var fault = new InvocationFault(
            exception.GetType().FullName ?? exception.GetType().Name,
            exception.Message,
            exception);
        var code = exception is UnauthorizedAccessException
            ? InteractionErrorCode.PERMISSION_DENIED
            : InteractionErrorCode.INVOCATION_FAILED;
        var issueCode = exception is UnauthorizedAccessException
            ? InteractionIssueCode.PERMISSION_DENIED
            : InteractionIssueCode.ACTION_REJECTED;
        var issueTarget = exception is UnauthorizedAccessException
            ? InteractionIssueTarget.PERMISSION
            : InteractionIssueTarget.ACTION;
        _Complete(
            InvocationStatus.FAILED,
            InteractionResult.Failure<object?>(
                code,
                exception.Message,
                [new InteractionIssue(issueCode, issueTarget, _ActionId, exception.Message)]),
            fault);
    }

    internal void CompleteHostDisposed()
    {
        if (SupportsCancellation)
        {
            Interlocked.Exchange(ref _CancellationRequested, 1);
        }

        _Complete(
            InvocationStatus.CANCELLED,
            InteractionResult.Failure<object?>(
                InteractionErrorCode.HOST_DISPOSED,
                "The UIEngine host was disposed during action invocation."),
            fault: null);

        if (SupportsCancellation)
        {
            try
            {
                _CancellationSource!.Cancel();
            }
            catch (AggregateException)
            {
                // The public invocation is already terminal; domain callback faults cannot replace it.
            }
        }
    }

    public InteractionResult<bool> RequestCancellation()
    {
        if (!SupportsCancellation)
        {
            return InteractionResult.Failure<bool>(
                InteractionErrorCode.INVALID_INPUT,
                "This action invocation does not support cancellation.");
        }

        if (Volatile.Read(ref _Terminal) != 0 ||
            Interlocked.Exchange(ref _CancellationRequested, 1) != 0)
        {
            return InteractionResult.Success(false);
        }

        try
        {
            _CancellationSource!.Cancel();
            return InteractionResult.Success(true);
        }
        catch (AggregateException exception)
        {
            CompleteFailure(exception);
            return InteractionResult.Failure<bool>(
                InteractionErrorCode.INVOCATION_FAILED,
                "One or more domain cancellation callbacks failed.");
        }
    }

    public async IAsyncEnumerable<InvocationProgress> ReadProgressAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _ProgressReaderStarted, 1) != 0)
        {
            throw new InvalidOperationException("An action invocation supports one progress stream reader.");
        }

        await foreach (var progress in _Progress.Reader
            .ReadAllAsync(cancellationToken)
            .ConfigureAwait(false))
        {
            yield return progress;
        }
    }

    private void _Complete(
        InvocationStatus status,
        InteractionResult<object?> result,
        InvocationFault? fault)
    {
        if (Interlocked.Exchange(ref _Terminal, 1) != 0)
        {
            return;
        }

        lock (_Gate)
        {
            _Status = status;
            _Fault = fault;
            _Progress.Writer.TryComplete();
        }

        _Completion.TrySetResult(result);
        _OnCompleted(this);
    }

    private sealed class _ProgressReporter<T>(ActionInvocation owner) : IProgress<T>
    {
        public void Report(T value) => owner.ReportProgress(value, typeof(T));
    }
}
