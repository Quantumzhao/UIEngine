using Microsoft.Extensions.Logging;
using UIEngine.Core;
using UIEngine.Core.Attributes;
using UIEngine.Core.Reflection;
using Xunit;

namespace UIEngine.Framework.Tests;

public sealed class HostServicesTests
{
    [Fact]
    public void HostSnapshotsConfiguredServicesLimitsAndPrecedence()
    {
        var providers = new List<IObjectDescriptorProvider>
        {
            new _UnavailableDescriptorProvider(),
            new ReflectionObjectDescriptorProvider(),
        };
        var identityProviders = new List<IDomainIdentityProvider> { new _IdentityProvider() };
        var observationAdapters = new List<IObservationAdapter> { new _ObservationAdapter() };
        var dispatcher = new DeterministicInteractionDispatcher();
        var loggerFactory = new RecordingLoggerFactory();
        var limits = new CollectionAccessLimits
        {
            MaxPageSize = 25,
            MaxSnapshotSize = 75,
            ObservationBufferCapacity = 8,
        };

        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            DescriptorProviders = providers,
            Dispatcher = dispatcher,
            DomainIdentityProviders = identityProviders,
            ObservationAdapters = observationAdapters,
            CollectionLimits = limits,
            LoggerFactory = loggerFactory,
        });
        providers.Clear();
        identityProviders.Clear();
        observationAdapters.Clear();

        Assert.Equal(2, host.Configuration.DescriptorProviders.Count);
        Assert.IsType<_UnavailableDescriptorProvider>(host.Configuration.DescriptorProviders[0]);
        Assert.IsType<ReflectionObjectDescriptorProvider>(host.Configuration.DescriptorProviders[1]);
        Assert.Single(host.Configuration.DomainIdentityProviders);
        Assert.Single(host.Configuration.ObservationAdapters);
        Assert.Same(dispatcher, host.Configuration.Dispatcher);
        Assert.Same(loggerFactory, host.Configuration.LoggerFactory);
        Assert.NotSame(limits, host.Configuration.CollectionLimits);
        Assert.Equal(limits, host.Configuration.CollectionLimits);
    }

    [Fact]
    public void HostUsesInlineDispatchAndSafeCollectionLimitsByDefault()
    {
        using var host = new UIEngineHost();

        Assert.Same(InlineInteractionDispatcher.Instance, host.Configuration.Dispatcher);
        Assert.Equal(100, host.Configuration.CollectionLimits.MaxPageSize);
        Assert.Equal(1_000, host.Configuration.CollectionLimits.MaxSnapshotSize);
        Assert.Equal(256, host.Configuration.CollectionLimits.ObservationBufferCapacity);
        Assert.False(host.Configuration.IncludeSensitiveDiagnosticData);
    }

    [Fact]
    public async Task FirstSupportingDescriptorProviderHasPrecedence()
    {
        var first = new _TrackingDescriptorProvider("first");
        var second = new _TrackingDescriptorProvider("second");
        using var host = new UIEngineHost([first, second]);
        var handle = host.RegisterRoot("root", new object()).Value;

        var result = await host.DescribeAsync(handle);

        Assert.Equal("first", result.Error?.Message);
        Assert.Equal(1, first.InvocationCount);
        Assert.Equal(0, second.InvocationCount);
    }

    [Fact]
    public async Task InlineDispatcherExecutesImmediatelyAndHonorsCancellation()
    {
        var callerThread = Environment.CurrentManagedThreadId;
        var executionThread = await InlineInteractionDispatcher.Instance.InvokeAsync(
            static () => Environment.CurrentManagedThreadId);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Equal(callerThread, executionThread);
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await InlineInteractionDispatcher.Instance.InvokeAsync(
                static () => 1,
                cancellation.Token));
    }

    [Fact]
    public async Task DeterministicDispatcherRunsOnlyWhenAdvanced()
    {
        var dispatcher = new DeterministicInteractionDispatcher();
        var wasRun = false;

        var pending = dispatcher.InvokeAsync(() =>
        {
            wasRun = true;
            return 42;
        });

        Assert.False(wasRun);
        Assert.Equal(1, dispatcher.InvocationCount);
        Assert.Equal(1, dispatcher.PendingCount);

        dispatcher.ExecuteNext();

        Assert.True(wasRun);
        Assert.Equal(42, await pending);
    }

    [Fact]
    public async Task DisposalIsIdempotentAndExistingOperationsFailPredictably()
    {
        var model = new _DisposableModel();
        var host = new UIEngineHost([new ReflectionObjectDescriptorProvider()]);
        var handle = host.RegisterRoot("model", model).Value;
        var descriptor = (await host.DescribeAsync(handle)).Value;
        var value = Assert.Single(descriptor.Values);
        var action = Assert.Single(descriptor.Actions);

        host.Dispose();
        host.Dispose();

        Assert.True(host.IsDisposed);
        Assert.Empty(host.Roots);
        Assert.Equal(
            InteractionErrorCode.HOST_DISPOSED,
            host.RegisterRoot("replacement", new object()).Error?.Code);
        Assert.Equal(
            InteractionErrorCode.HOST_DISPOSED,
            (await host.DescribeAsync(handle)).Error?.Code);
        Assert.Equal(
            InteractionErrorCode.HOST_DISPOSED,
            (await value.ReadAsync()).Error?.Code);
        Assert.Equal(
            InteractionErrorCode.HOST_DISPOSED,
            (await action.InvokeAsync(new Dictionary<string, object?>())).Error?.Code);
        Assert.Throws<ObjectDisposedException>(() => host.GetOrCreateHandle(new object()));
    }

    [Fact]
    public async Task DiagnosticsUseStableIdsAndRedactExceptionsByDefault()
    {
        const string SECRET = "secret-domain-value";
        var loggerFactory = new RecordingLoggerFactory();
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            DescriptorProviders = [new _ThrowingDescriptorProvider(SECRET)],
            LoggerFactory = loggerFactory,
        });
        var handle = host.RegisterRoot("root", new object()).Value;

        var result = await host.DescribeAsync(handle);

        Assert.Equal(InteractionErrorCode.DESCRIPTOR_UNAVAILABLE, result.Error?.Code);
        var failure = Assert.Single(
            loggerFactory.Entries,
            entry => entry.EventId == UIEngineDiagnosticEventIds.DISCOVERY_FAILED);
        Assert.Equal(nameof(UIEngineDiagnosticEventIds.DISCOVERY_FAILED), failure.EventId.Name);
        Assert.Equal(handle.Identity.RuntimeId, failure.Properties["RuntimeId"]);
        Assert.Equal(InteractionErrorCode.DESCRIPTOR_UNAVAILABLE, failure.Properties["ErrorCode"]);
        Assert.DoesNotContain(SECRET, failure.Message, StringComparison.Ordinal);
        Assert.Null(failure.Exception);
    }

    [Theory]
    [InlineData(0, 10, 10)]
    [InlineData(10, 0, 10)]
    [InlineData(10, 10, 0)]
    public void HostRejectsNonPositiveCollectionLimits(
        int maxPageSize,
        int maxSnapshotSize,
        int observationBufferCapacity)
    {
        var options = new UIEngineHostOptions
        {
            CollectionLimits = new CollectionAccessLimits
            {
                MaxPageSize = maxPageSize,
                MaxSnapshotSize = maxSnapshotSize,
                ObservationBufferCapacity = observationBufferCapacity,
            },
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => new UIEngineHost(options));
    }

    private sealed class _UnavailableDescriptorProvider : IObjectDescriptorProvider
    {
        public bool CanDescribe(Type objectType) => false;

        public ValueTask<InteractionResult<IObjectDescriptor>> DescribeAsync(
            UIEngineHost host,
            object instance,
            ObjectHandle handle,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class _ThrowingDescriptorProvider(string message) : IObjectDescriptorProvider
    {
        public bool CanDescribe(Type objectType) => true;

        public ValueTask<InteractionResult<IObjectDescriptor>> DescribeAsync(
            UIEngineHost host,
            object instance,
            ObjectHandle handle,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException(message);
    }

    private sealed class _TrackingDescriptorProvider(string message) : IObjectDescriptorProvider
    {
        public int InvocationCount { get; private set; }

        public bool CanDescribe(Type objectType) => true;

        public ValueTask<InteractionResult<IObjectDescriptor>> DescribeAsync(
            UIEngineHost host,
            object instance,
            ObjectHandle handle,
            CancellationToken cancellationToken = default)
        {
            InvocationCount++;
            return ValueTask.FromResult(InteractionResult.Failure<IObjectDescriptor>(
                InteractionErrorCode.DESCRIPTOR_UNAVAILABLE,
                message));
        }
    }

    private sealed class _IdentityProvider : IDomainIdentityProvider
    {
        public bool CanProvideIdentity(Type objectType) => true;

        public ValueTask<InteractionResult<string?>> GetIdentityAsync(
            object instance,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                InteractionResult.Success<string?>("identity"));
    }

    private sealed class _ObservationAdapter : IObservationAdapter
    {
        public bool CanObserve(Type objectType) => true;
    }

    private sealed class _DisposableModel
    {
        [Expose]
        public int Value { get; set; }

        [Action]
        public void Run()
        {
            Value++;
        }
    }
}
