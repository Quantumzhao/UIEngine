using System.Collections;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using UIEngine.Core;
using UIEngine.Core.Attributes;
using UIEngine.Core.Exposure;
using UIEngine.Core.Reflection;
using Xunit;

namespace UIEngine.Framework.Tests;

/// <summary>Verifies the step-7 dispatch boundary for live descriptor access.</summary>
public sealed class DispatchRoutingTests
{
    [Fact]
    public async Task ReflectionSummaryReadsValidationWritesReferencesCollectionsAndCallsAreDispatched()
    {
        var dispatcher = new _RecordingDispatcher();
        var model = new _DispatchModel(dispatcher);
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            DescriptorProviders = [new ReflectionObjectDescriptorProvider()],
            Dispatcher = dispatcher,
        });
        var descriptor = (await host.DescribeAsync(host.RegisterRoot("model", model).Value)).Value;
        var count = Assert.Single(descriptor.Values);

        var read = await count.ReadAsync();
        var write = await count.WriteAsync("3");
        var reference = await Assert.Single(descriptor.References).ReadAsync();
        var collection = await Assert.Single(descriptor.Collections).ReadAsync(
            CollectionReadRequest.Snapshot(10));
        var invocation = await Assert.Single(descriptor.Actions).InvokeAsync(
            new Dictionary<string, object?> { ["amount"] = "2" });
        var completion = await invocation.Value.Completion;

        Assert.Equal("count 1", descriptor.Summary);
        Assert.Equal(1, read.Value);
        Assert.Equal(3, write.Value);
        Assert.True(reference.IsSuccess);
        Assert.Single(collection.Value.Entries);
        Assert.Equal(5, completion.Value);
        Assert.True(model.SummaryWasRead);
        Assert.True(model.ValidationWasRun);
        Assert.True(model.PreconditionWasRun);
        Assert.Equal(6, dispatcher.InvocationCount);
    }

    [Fact]
    public async Task ProgrammaticSummaryValuesValidationAndMutationAreDispatchedWithoutNestedQueueing()
    {
        var dispatcher = new _RecordingDispatcher();
        var registry = new ExposureRegistry();
        registry.For<_ProgrammaticModel>()
            .Summary(model => model.ReadSummary(dispatcher))
            .Value(
                "score",
                model => model.Read(dispatcher),
                (model, value) => model.Write(dispatcher, value))
            .Validate<int>("score", (model, _) => model.Validate(dispatcher))
            .ValidateAsync<int>("score", (model, _, _) => model.ValidateAsync(dispatcher));
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            Exposure = registry,
            Dispatcher = dispatcher,
        });
        var model = new _ProgrammaticModel();
        var descriptor = (await host.DescribeAsync(host.RegisterRoot("model", model).Value)).Value;
        var value = Assert.Single(descriptor.Values);

        var read = await value.ReadAsync();
        var write = await value.WriteAsync("4");

        Assert.Equal("score 1", descriptor.Summary);
        Assert.Equal(1, read.Value);
        Assert.Equal(4, write.Value);
        Assert.Equal(4, model.Score);
        Assert.Equal(3, dispatcher.InvocationCount);
    }

    [Fact]
    public async Task ProviderMustExplicitlyOptOutOfDispatchForDirectExecution()
    {
        var dispatcher = new DeterministicInteractionDispatcher();
        var provider = new _DirectProvider();
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            DescriptorProviders = [provider],
            Dispatcher = dispatcher,
        });
        var descriptor = (await host.DescribeAsync(host.RegisterRoot("model", new object()).Value)).Value;

        var read = await Assert.Single(descriptor.Values).ReadAsync();

        Assert.Equal(7, read.Value);
        Assert.Equal(0, dispatcher.InvocationCount);
        Assert.Equal(
            [
                InteractionDispatchOperation.DESCRIPTOR_DISCOVERY,
                InteractionDispatchOperation.SUMMARY_READ,
                InteractionDispatchOperation.VALUE_READ,
            ],
            provider.RequestedOperations);
    }

    [Fact]
    public async Task ReentrantDescriptorAccessExecutesDirectlyOnTheCurrentDispatcherContext()
    {
        var dispatcher = new _RecordingDispatcher();
        var model = new _ReentrantModel();
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            DescriptorProviders = [new ReflectionObjectDescriptorProvider()],
            Dispatcher = dispatcher,
        });
        var descriptor = (await host.DescribeAsync(host.RegisterRoot("model", model).Value)).Value;
        var inner = Assert.Single(descriptor.Values, value => value.Id == nameof(_ReentrantModel.Inner));
        var outer = Assert.Single(descriptor.Values, value => value.Id == nameof(_ReentrantModel.Outer));
        model.InnerDescriptor = inner;
        var invocationCount = dispatcher.InvocationCount;

        var result = await outer.ReadAsync();

        Assert.Equal(11, result.Value);
        Assert.Equal(invocationCount + 1, dispatcher.InvocationCount);
    }

    [Fact]
    public async Task QueuedCancellationDoesNotExecuteDomainAccess()
    {
        var dispatcher = new DeterministicInteractionDispatcher();
        var value = new _TrackingValueDescriptor();
        var provider = new _DirectProvider(value);
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            DescriptorProviders = [provider],
            Dispatcher = dispatcher,
        });
        var descriptor = (await host.DescribeAsync(host.RegisterRoot("model", new object()).Value)).Value;
        using var cancellation = new CancellationTokenSource();

        var cancelledRead = Assert.Single(descriptor.Values).ReadAsync(cancellation.Token).AsTask();
        cancellation.Cancel();
        dispatcher.ExecuteNext();
        var cancelled = await cancelledRead;

        Assert.Equal(InteractionErrorCode.CANCELLED, cancelled.Error?.Code);
        Assert.Equal(0, value.ReadCount);
    }

    [Fact]
    public async Task QueuedHostDisposalDoesNotExecuteDomainAccess()
    {
        var dispatcher = new DeterministicInteractionDispatcher();
        var value = new _TrackingValueDescriptor();
        var provider = new _DirectProvider(value);
        var host = new UIEngineHost(new UIEngineHostOptions
        {
            DescriptorProviders = [provider],
            Dispatcher = dispatcher,
        });
        var descriptor = (await host.DescribeAsync(host.RegisterRoot("model", new object()).Value)).Value;

        var disposedRead = Assert.Single(descriptor.Values).ReadAsync().AsTask();
        host.Dispose();
        dispatcher.ExecuteNext();
        var disposed = await disposedRead;

        Assert.Equal(InteractionErrorCode.HOST_DISPOSED, disposed.Error?.Code);
        Assert.Equal(0, value.ReadCount);
    }

    [Fact]
    public async Task DispatcherRejectionReturnsStructuredFailure()
    {
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            DescriptorProviders = [new ReflectionObjectDescriptorProvider()],
            Dispatcher = new _RejectingDispatcher(),
        });

        var result = await host.DescribeAsync(host.RegisterRoot("model", new object()).Value);

        Assert.Equal(InteractionErrorCode.DISPATCH_FAILED, result.Error?.Code);
    }

    [Fact]
    public async Task ObservationDiscoveryAndHandlerSetupAreDispatched()
    {
        var dispatcher = new _RecordingDispatcher();
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            DescriptorProviders = [new ReflectionObjectDescriptorProvider()],
            Dispatcher = dispatcher,
        });
        var handle = host.RegisterRoot("model", new _ObservableDispatchModel()).Value;

        var result = await host.ObserveAsync(handle, new ObservationRequest
        {
            MemberId = nameof(_ObservableDispatchModel.Value),
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(2, dispatcher.InvocationCount);
        result.Value.Dispose();
    }

    private sealed class _RecordingDispatcher : IInteractionDispatcher
    {
        private int _Depth;

        public int InvocationCount { get; private set; }

        public bool CheckAccess() => _Depth > 0;

        public ValueTask<T> InvokeAsync<T>(
            Func<T> action,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            InvocationCount++;
            _Depth++;
            try
            {
                return ValueTask.FromResult(action());
            }
            finally
            {
                _Depth--;
            }
        }

        public async ValueTask InvokeAsync(
            Action action,
            CancellationToken cancellationToken = default) => await InvokeAsync(
                () =>
                {
                    action();
                    return true;
                },
                cancellationToken);
    }

    private sealed class _RejectingDispatcher : IInteractionDispatcher
    {
        public bool CheckAccess() => false;

        public ValueTask<T> InvokeAsync<T>(
            Func<T> action,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException(
                "The dispatcher rejected the interaction.");

        public ValueTask InvokeAsync(
            Action action,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException(
                "The dispatcher rejected the interaction.");
    }

    private sealed class _DispatchModel(_RecordingDispatcher dispatcher)
    {
        private int _Count = 1;

        public bool SummaryWasRead { get; private set; }

        public bool ValidationWasRun { get; set; }

        public bool PreconditionWasRun { get; private set; }

        [Summary]
        public string Summary
        {
            get
            {
                _EnsureAccess();
                SummaryWasRead = true;
                return $"count {_Count}";
            }
        }

        [Expose]
        [_RequiresDispatch]
        public int Count
        {
            get
            {
                _EnsureAccess();
                return _Count;
            }
            set
            {
                _EnsureAccess();
                _Count = value;
            }
        }

        [Children]
        public object Child
        {
            get
            {
                _EnsureAccess();
                return new object();
            }
        }

        [Children]
        public IReadOnlyCollection<object> Items
        {
            get
            {
                _EnsureAccess();
                return new _DispatchEnumerable(dispatcher);
            }
        }

        [Action(Precondition = nameof(CanAdd))]
        public int Add([_RequiresDispatch] int amount)
        {
            _EnsureAccess();
            _Count += amount;
            return _Count;
        }

        private bool CanAdd()
        {
            _EnsureAccess();
            PreconditionWasRun = true;
            return true;
        }

        private void _EnsureAccess()
        {
            if (!dispatcher.CheckAccess())
            {
                throw new InvalidOperationException("Domain access bypassed the dispatcher.");
            }
        }
    }

    private sealed class _ObservableDispatchModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        [Expose]
        public int Value { get; set; }

        public void Raise() => PropertyChanged?.Invoke(
            this,
            new PropertyChangedEventArgs(nameof(Value)));
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter)]
    private sealed class _RequiresDispatchAttribute : ValidationAttribute
    {
        protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
        {
            if (validationContext.ObjectInstance is _DispatchModel model)
            {
                model.ValidationWasRun = true;
            }

            return ValidationResult.Success;
        }
    }

    private sealed class _DispatchEnumerable(_RecordingDispatcher dispatcher) : IReadOnlyCollection<object>
    {
        public int Count
        {
            get
            {
                if (!dispatcher.CheckAccess())
                {
                    throw new InvalidOperationException("Collection access bypassed the dispatcher.");
                }

                return 1;
            }
        }

        public IEnumerator<object> GetEnumerator()
        {
            if (!dispatcher.CheckAccess())
            {
                throw new InvalidOperationException("Collection access bypassed the dispatcher.");
            }

            return ((IEnumerable<object>)[new object()]).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class _ProgrammaticModel
    {
        public int Score { get; private set; } = 1;

        public string ReadSummary(IInteractionDispatcher dispatcher)
        {
            _EnsureAccess(dispatcher);
            return $"score {Score}";
        }

        public int Read(IInteractionDispatcher dispatcher)
        {
            _EnsureAccess(dispatcher);
            return Score;
        }

        public void Write(IInteractionDispatcher dispatcher, int value)
        {
            _EnsureAccess(dispatcher);
            Score = value;
        }

        public string? Validate(IInteractionDispatcher dispatcher)
        {
            _EnsureAccess(dispatcher);
            _ = Score;
            return null;
        }

        public ValueTask<string?> ValidateAsync(IInteractionDispatcher dispatcher)
        {
            _EnsureAccess(dispatcher);
            _ = Score;
            return ValueTask.FromResult<string?>(null);
        }

        private static void _EnsureAccess(IInteractionDispatcher dispatcher)
        {
            if (!dispatcher.CheckAccess())
            {
                throw new InvalidOperationException("Programmatic access bypassed the dispatcher.");
            }
        }
    }

    private sealed class _ReentrantModel
    {
        private readonly int _Inner = 10;

        public IValueDescriptor? InnerDescriptor { get; set; }

        [Expose]
        public int Inner => _Inner;

        [Expose]
        public int Outer => (int)(
            InnerDescriptor!.ReadAsync().AsTask().GetAwaiter().GetResult().Value ??
            throw new InvalidOperationException("The nested value was null.")) + 1;
    }

    private sealed class _DirectProvider : IObjectDescriptorProvider, IInteractionDispatchPolicy
    {
        private readonly IValueDescriptor _Value;

        public _DirectProvider(IValueDescriptor? value = null)
        {
            _Value = value ?? new _ConstantValueDescriptor();
        }

        public List<InteractionDispatchOperation> RequestedOperations { get; } = [];

        public bool CanDescribe(Type objectType) => true;

        public bool CanExecuteDirectly(InteractionDispatchOperation operation)
        {
            RequestedOperations.Add(operation);
            if (operation is InteractionDispatchOperation.DESCRIPTOR_DISCOVERY or
                InteractionDispatchOperation.SUMMARY_READ)
            {
                return true;
            }

            return operation == InteractionDispatchOperation.VALUE_READ &&
                _Value.GetType() == typeof(_ConstantValueDescriptor);
        }

        public ValueTask<InteractionResult<IObjectDescriptor>> DescribeAsync(
            UIEngineHost host,
            object instance,
            ObjectHandle handle,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                InteractionResult.Success<IObjectDescriptor>(new _ObjectDescriptor(handle, _Value)));
    }

    private sealed class _ObjectDescriptor(ObjectHandle handle, IValueDescriptor value) : IObjectDescriptor
    {
        public ObjectIdentity Identity => handle.Identity;
        public string TypeName => typeof(object).FullName!;
        public string DisplayName => "Object";
        public string? Summary => null;
        public IReadOnlyList<IValueDescriptor> Values => [value];
        public IReadOnlyList<IReferenceDescriptor> References => [];
        public IReadOnlyList<ICollectionDescriptor> Collections => [];
        public IReadOnlyList<IActionDescriptor> Actions => [];
    }

    private class _ConstantValueDescriptor : IValueDescriptor
    {
        public string Id => "value";
        public string DisplayName => "Value";
        public Type ValueType => typeof(int);
        public bool CanRead => true;
        public bool CanWrite => false;
        public bool IsNullable => false;
        public IReadOnlyList<SelectionOption> Options => [];
        public ValueRange? Range => null;
        public IReadOnlyList<ValidationRuleDescriptor> ValidationRules => [];
        public string? Unit => null;
        public IReadOnlyList<string> Tags => [];

        public virtual ValueTask<InteractionResult<object?>> ReadAsync(
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                InteractionResult.Success<object?>(7));

        public ValueTask<InteractionResult<object?>> WriteAsync(
            object? value,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(
                InteractionResult.Failure<object?>(
                    InteractionErrorCode.VALIDATION_FAILED,
                    "The value is read-only."));
    }

    private sealed class _TrackingValueDescriptor : _ConstantValueDescriptor
    {
        public int ReadCount { get; private set; }

        public override ValueTask<InteractionResult<object?>> ReadAsync(
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            return base.ReadAsync(cancellationToken);
        }
    }
}
