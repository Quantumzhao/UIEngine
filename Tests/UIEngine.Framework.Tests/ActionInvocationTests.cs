using System.Runtime.CompilerServices;
using UIEngine.Core;
using UIEngine.Core.Attributes;
using UIEngine.Core.Reflection;
using Xunit;

namespace UIEngine.Framework.Tests;

/// <summary>Verifies normalized reflection action lifecycles and infrastructure injection.</summary>
public sealed class ActionInvocationTests
{
    [Fact]
    public async Task EverySupportedReturnShapeCompletesWithOneNormalizedResult()
    {
        var (_, descriptor) = await _DescribeAsync(new _ReturnShapeModel());

        await _AssertCompletionAsync(descriptor, nameof(_ReturnShapeModel.SyncVoid), null, false, null);
        await _AssertCompletionAsync(descriptor, nameof(_ReturnShapeModel.SyncValue), 1, false, typeof(int));
        await _AssertCompletionAsync(descriptor, nameof(_ReturnShapeModel.TaskVoid), null, true, null);
        await _AssertCompletionAsync(descriptor, nameof(_ReturnShapeModel.TaskValue), 2, true, typeof(int));
        await _AssertCompletionAsync(descriptor, nameof(_ReturnShapeModel.ValueTaskVoid), null, true, null);
        await _AssertCompletionAsync(descriptor, nameof(_ReturnShapeModel.ValueTaskValue), 3, true, typeof(int));
    }

    [Fact]
    public async Task UserParametersExcludeInjectedCancellationAndProgressParameters()
    {
        var model = new _ControlledModel();
        var (_, descriptor) = await _DescribeAsync(model);
        var action = Assert.Single(
            descriptor.Actions,
            candidate => candidate.Id == nameof(_ControlledModel.RunAsync));

        Assert.True(action.IsAsynchronous);
        Assert.True(action.SupportsCancellation);
        Assert.Equal(typeof(int), action.ProgressType);
        Assert.Equal(typeof(int), action.ResultType);
        var parameter = Assert.Single(action.Parameters);
        Assert.Equal("amount", parameter.Id);

        var started = await action.InvokeAsync(
            new Dictionary<string, object?> { ["amount"] = "4" });
        Assert.True(started.IsSuccess);
        Assert.Equal(InvocationStatus.RUNNING, started.Value.Status);

        var progressTask = _ReadProgressAsync(started.Value);
        model.Release(8);
        var completion = await started.Value.Completion;
        var progress = await progressTask;

        Assert.Equal(8, completion.Value);
        Assert.Equal(InvocationStatus.SUCCEEDED, started.Value.Status);
        Assert.Null(started.Value.Fault);
        Assert.Equal([1, 4], progress.Select(static item => item.Value));
        Assert.Equal([1L, 2L], progress.Select(static item => item.OrderingToken));
        Assert.All(progress, item => Assert.Equal(typeof(int), item.ValueType));
    }

    [Fact]
    public async Task ProgressIsBoundedAndIgnoredAfterTheTerminalOutcome()
    {
        var model = new _ProgressModel();
        var options = new UIEngineHostOptions
        {
            DescriptorProviders = [new ReflectionObjectDescriptorProvider()],
            InvocationProgressBufferCapacity = 2,
        };
        using var host = new UIEngineHost(options);
        var descriptor = (await host.DescribeAsync(
            host.RegisterRoot("model", model).Value)).Value;
        var action = Assert.Single(descriptor.Actions);

        var started = await action.InvokeAsync(new Dictionary<string, object?>());
        var completion = await started.Value.Completion;
        model.ReportAfterCompletion(4);
        var progress = await _ReadProgressAsync(started.Value);

        Assert.True(completion.IsSuccess);
        Assert.Equal([2, 3], progress.Select(static item => item.Value));
    }

    [Fact]
    public async Task CancellationIsAdvertisedRequestedIdempotentlyAndNormalized()
    {
        var model = new _CancellationModel();
        var (_, descriptor) = await _DescribeAsync(model);
        var cancellable = Assert.Single(
            descriptor.Actions,
            action => action.Id == nameof(_CancellationModel.WaitAsync));
        var unsupported = Assert.Single(
            descriptor.Actions,
            action => action.Id == nameof(_CancellationModel.Immediate));

        var started = await cancellable.InvokeAsync(new Dictionary<string, object?>());
        await model.Started.Task;
        var first = started.Value.RequestCancellation();
        var second = started.Value.RequestCancellation();
        var completion = await started.Value.Completion;

        Assert.True(first.Value);
        Assert.False(second.Value);
        Assert.True(started.Value.IsCancellationRequested);
        Assert.Equal(InvocationStatus.CANCELLED, started.Value.Status);
        Assert.Equal(InteractionErrorCode.CANCELLED, completion.Error?.Code);

        var immediate = await unsupported.InvokeAsync(new Dictionary<string, object?>());
        Assert.Equal(
            InteractionErrorCode.INVALID_INPUT,
            immediate.Value.RequestCancellation().Error?.Code);
    }

    [Fact]
    public async Task CancellationAfterSuccessCannotReplaceTheTerminalOutcome()
    {
        var (_, descriptor) = await _DescribeAsync(new _CancellationRaceModel());
        var action = Assert.Single(descriptor.Actions);

        var started = await action.InvokeAsync(new Dictionary<string, object?>());
        var completion = await started.Value.Completion;
        var cancellation = started.Value.RequestCancellation();

        Assert.Equal(7, completion.Value);
        Assert.Equal(InvocationStatus.SUCCEEDED, started.Value.Status);
        Assert.False(cancellation.Value);
    }

    [Fact]
    public async Task TaskFaultAndUnassociatedCancellationAreCapturedAsFailures()
    {
        var (_, descriptor) = await _DescribeAsync(new _FaultModel());
        var faultAction = Assert.Single(
            descriptor.Actions,
            action => action.Id == nameof(_FaultModel.FailAsync));
        var cancellationAction = Assert.Single(
            descriptor.Actions,
            action => action.Id == nameof(_FaultModel.CancelWithoutRequestAsync));

        var fault = (await faultAction.InvokeAsync(new Dictionary<string, object?>())).Value;
        var faultCompletion = await fault.Completion;
        var unassociated = (await cancellationAction.InvokeAsync(
            new Dictionary<string, object?>())).Value;
        var unassociatedCompletion = await unassociated.Completion;

        Assert.Equal(InvocationStatus.FAILED, fault.Status);
        Assert.Equal(InteractionErrorCode.INVOCATION_FAILED, faultCompletion.Error?.Code);
        Assert.Equal(typeof(InvalidOperationException).FullName, fault.Fault?.ExceptionType);
        Assert.IsType<InvalidOperationException>(fault.Fault?.Exception);
        Assert.Equal(InvocationStatus.FAILED, unassociated.Status);
        Assert.Equal(InteractionErrorCode.INVOCATION_FAILED, unassociatedCompletion.Error?.Code);
    }

    [Fact]
    public async Task HostDisposalCancelsAndTerminatesAnActiveInvocationAndItsProgressStream()
    {
        var model = new _CancellationModel();
        var (host, descriptor) = await _DescribeAsync(model);
        var action = Assert.Single(
            descriptor.Actions,
            candidate => candidate.Id == nameof(_CancellationModel.WaitAsync));
        var started = await action.InvokeAsync(new Dictionary<string, object?>());
        await model.Started.Task;
        var progressTask = _ReadProgressAsync(started.Value);

        host.Dispose();
        var completion = await started.Value.Completion;
        var progress = await progressTask;

        Assert.True(started.Value.IsCancellationRequested);
        Assert.Equal(InvocationStatus.CANCELLED, started.Value.Status);
        Assert.Equal(InteractionErrorCode.HOST_DISPOSED, completion.Error?.Code);
        Assert.Empty(progress);
    }

    [Fact]
    public async Task InvocationRefusesATargetRemovedFromTheHost()
    {
        var (host, action, target) = _CreateRemovedTarget();
        using (host)
        {
            for (var attempt = 0; attempt < 3 && target.IsAlive; attempt++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }

            Assert.False(target.IsAlive);
            var started = await action.InvokeAsync(new Dictionary<string, object?>());

            Assert.Equal(InteractionErrorCode.TARGET_UNAVAILABLE, started.Error?.Code);
        }
    }

    [Theory]
    [InlineData(typeof(_RefParameterModel))]
    [InlineData(typeof(_OpenGenericModel))]
    [InlineData(typeof(_AmbiguousCancellationModel))]
    [InlineData(typeof(_AmbiguousProgressModel))]
    [InlineData(typeof(_AsyncVoidModel))]
    public async Task UnsupportedActionSignaturesAreRejectedDuringDiscovery(Type modelType)
    {
        using var host = new UIEngineHost([new ReflectionObjectDescriptorProvider()]);
        var model = Activator.CreateInstance(modelType)!;
        var handle = host.RegisterRoot("model", model).Value;

        var descriptor = await host.DescribeAsync(handle);

        Assert.Equal(InteractionErrorCode.DESCRIPTOR_UNAVAILABLE, descriptor.Error?.Code);
    }

    private static async Task _AssertCompletionAsync(
        IObjectDescriptor descriptor,
        string actionId,
        object? expected,
        bool isAsynchronous,
        Type? resultType)
    {
        var action = Assert.Single(descriptor.Actions, candidate => candidate.Id == actionId);
        Assert.Equal(isAsynchronous, action.IsAsynchronous);
        Assert.Equal(resultType, action.ResultType);

        var started = await action.InvokeAsync(new Dictionary<string, object?>());
        var first = await started.Value.Completion;
        var second = await started.Value.Completion;

        Assert.Equal(expected, first.Value);
        Assert.Same(first, second);
        Assert.Equal(InvocationStatus.SUCCEEDED, started.Value.Status);
    }

    private static async Task<List<InvocationProgress>> _ReadProgressAsync(IActionInvocation invocation)
    {
        var progress = new List<InvocationProgress>();
        await foreach (var item in invocation.ReadProgressAsync())
        {
            progress.Add(item);
        }

        return progress;
    }

    private static async Task<(UIEngineHost Host, IObjectDescriptor Descriptor)> _DescribeAsync(
        object model)
    {
        var host = new UIEngineHost([new ReflectionObjectDescriptorProvider()]);
        var descriptor = (await host.DescribeAsync(
            host.RegisterRoot("model", model).Value)).Value;
        return (host, descriptor);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (UIEngineHost Host, IActionDescriptor Action, WeakReference Target)
        _CreateRemovedTarget()
    {
        var host = new UIEngineHost([new ReflectionObjectDescriptorProvider()]);
        var model = new _ReturnShapeModel();
        var handle = host.RegisterRoot("model", model).Value;
        var descriptor = host.DescribeAsync(handle).AsTask().GetAwaiter().GetResult().Value;
        var action = Assert.Single(
            descriptor.Actions,
            candidate => candidate.Id == nameof(_ReturnShapeModel.SyncValue));
        var target = new WeakReference(model);
        Assert.True(host.UnregisterRoot("model").IsSuccess);
        return (host, action, target);
    }

#pragma warning disable CA1822 // Reflection actions must remain instance methods.
    private sealed class _ReturnShapeModel
    {
        [Action]
        public void SyncVoid()
        {
        }

        [Action]
        public int SyncValue() => 1;

        [Action]
        public Task TaskVoid() => Task.CompletedTask;

        [Action]
        public Task<int> TaskValue() => Task.FromResult(2);

        [Action]
        public ValueTask ValueTaskVoid() => ValueTask.CompletedTask;

        [Action]
        public ValueTask<int> ValueTaskValue() => ValueTask.FromResult(3);
    }

    private sealed class _ControlledModel
    {
        private readonly TaskCompletionSource<int> _Release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        [Action]
        public async Task<int> RunAsync(
            int amount,
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            progress.Report(1);
            var result = await _Release.Task.WaitAsync(cancellationToken);
            progress.Report(amount);
            return result;
        }

        public void Release(int result) => _Release.TrySetResult(result);
    }

    private sealed class _ProgressModel
    {
        private IProgress<int>? _Progress;

        [Action]
        public void Report(IProgress<int> progress)
        {
            _Progress = progress;
            progress.Report(1);
            progress.Report(2);
            progress.Report(3);
        }

        public void ReportAfterCompletion(int value) => _Progress!.Report(value);
    }

    private sealed class _CancellationModel
    {
        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        [Action]
        public int Immediate() => 1;

        [Action]
        public async Task WaitAsync(CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class _CancellationRaceModel
    {
        [Action]
        public Task<int> CompleteAsync(CancellationToken cancellationToken) => Task.FromResult(7);
    }

    private sealed class _FaultModel
    {
        [Action]
        public Task FailAsync() => Task.FromException(new InvalidOperationException("Expected failure."));

        [Action]
        public Task CancelWithoutRequestAsync() => Task.FromCanceled(new CancellationToken(canceled: true));
    }

    private sealed class _RefParameterModel
    {
        [Action]
        public void Invalid(ref int value) => value++;
    }

    private sealed class _OpenGenericModel
    {
        [Action]
        public T Invalid<T>(T value) => value;
    }

    private sealed class _AmbiguousCancellationModel
    {
        [Action]
        public void Invalid(CancellationToken first, CancellationToken second)
        {
        }
    }

    private sealed class _AmbiguousProgressModel
    {
        [Action]
        public void Invalid(IProgress<int> first, IProgress<string> second)
        {
        }
    }

    private sealed class _AsyncVoidModel
    {
        [Action]
        public async void Invalid()
        {
            await Task.Yield();
        }
    }
#pragma warning restore CA1822
}
