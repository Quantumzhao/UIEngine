using UIEngine.Core;
using UIEngine.Core.Attributes;
using UIEngine.Core.Exposure;
using UIEngine.Core.Reflection;
using Xunit;

namespace UIEngine.Framework.Tests;

public sealed class ProgrammaticExposureTests
{
    [Fact]
    public async Task UnannotatedTypeCanBeExposedProgrammatically()
    {
        var registry = new ExposureRegistry();
        registry.For<_ThirdPartyModel>()
            .Value(
                "count",
                static model => model.Count,
                static (model, value) => model.Count = value,
                static model => $"Count at {model.Count}")
            .Summary(static model => $"Third-party count: {model.Count}");
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            Exposure = registry,
        });
        var model = new _ThirdPartyModel { Count = 3 };
        var handle = host.RegisterRoot("third-party", model).Value;

        var descriptor = (await host.DescribeAsync(handle)).Value;
        var value = Assert.Single(descriptor.Values);

        Assert.Equal("Third-party count: 3", descriptor.Summary);
        Assert.Equal("count", value.Id);
        Assert.Equal("Count at 3", value.DisplayName);
        Assert.Equal(3, (await value.ReadAsync()).Value);
        Assert.Equal(7, (await value.WriteAsync("7")).Value);
        Assert.Equal(7, model.Count);
    }

    [Fact]
    public async Task ProgrammaticMembersSupplementAndOverrideReflection()
    {
        var registry = new ExposureRegistry();
        registry.For<_AnnotatedModel>()
            .Value("Count", static model => model.Count * 2)
            .Value("Computed", static model => model.Count + 1)
            .Summary(static model => $"Registered {model.Count}");
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            Exposure = registry,
            DescriptorProviders = [new ReflectionObjectDescriptorProvider()],
        });
        var handle = host.RegisterRoot("model", new _AnnotatedModel()).Value;

        var descriptor = (await host.DescribeAsync(handle)).Value;

        Assert.Equal("Registered 2", descriptor.Summary);
        Assert.Equal(["Count", "Computed", "Name"], descriptor.Values.Select(static value => value.Id));
        Assert.Equal(4, (await descriptor.Values[0].ReadAsync()).Value);
        Assert.False(descriptor.Values[0].CanWrite);
    }

    [Fact]
    public async Task ProviderTiersHaveDeterministicPrecedence()
    {
        var registry = new ExposureRegistry();
        registry.For<_ThirdPartyModel>().Value("registered", static model => model.Count);
        var customProvider = new _MarkerProvider("custom");
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            Exposure = registry,
            DescriptorProviders = [new ReflectionObjectDescriptorProvider(), customProvider],
        });
        var registeredHandle = host.RegisterRoot("registered", new _ThirdPartyModel()).Value;
        var customHandle = host.RegisterRoot("custom", new _CustomModel()).Value;

        var registered = (await host.DescribeAsync(registeredHandle)).Value;
        var custom = await host.DescribeAsync(customHandle);

        Assert.Equal("registered", Assert.Single(registered.Values).Id);
        Assert.Equal("custom", custom.Error?.Message);
        Assert.Equal(1, customProvider.InvocationCount);
    }

    [Fact]
    public async Task HostsUseIndependentRegistrationSnapshots()
    {
        var registry = new ExposureRegistry();
        var builder = registry.For<_ThirdPartyModel>()
            .Value("first", static model => model.Count);
        using var firstHost = new UIEngineHost(new UIEngineHostOptions { Exposure = registry });
        builder.Value("second", static model => model.Count + 1);
        using var secondHost = new UIEngineHost(new UIEngineHostOptions { Exposure = registry });
        var model = new _ThirdPartyModel();

        var first = (await firstHost.DescribeAsync(firstHost.RegisterRoot("model", model).Value)).Value;
        var second = (await secondHost.DescribeAsync(secondHost.RegisterRoot("model", model).Value)).Value;

        Assert.Equal(["first"], first.Values.Select(static value => value.Id));
        Assert.Equal(["first", "second"], second.Values.Select(static value => value.Id));
    }

    [Fact]
    public void DuplicateProgrammaticMemberIdentifiersAreRejectedWhenHostIsBuilt()
    {
        var registry = new ExposureRegistry();
        registry.For<_ThirdPartyModel>()
            .Value("duplicate", static model => model.Count)
            .Value("duplicate", static model => model.Count + 1);

        var exception = Assert.Throws<ArgumentException>(() =>
            new UIEngineHost(new UIEngineHostOptions { Exposure = registry }));

        Assert.Contains("duplicate member identifier 'duplicate'", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MultipleReflectionFallbacksAreRejectedWhenHostIsBuilt()
    {
        var loggerFactory = new RecordingLoggerFactory();
        var options = new UIEngineHostOptions
        {
            DescriptorProviders =
            [
                new ReflectionObjectDescriptorProvider(),
                new ReflectionObjectDescriptorProvider(),
            ],
            LoggerFactory = loggerFactory,
        };

        Assert.Throws<ArgumentException>(() => new UIEngineHost(options));
        var diagnostic = Assert.Single(
            loggerFactory.Entries,
            entry => entry.EventId == UIEngineDiagnosticEventIds.CONFIGURATION_INVALID);
        Assert.Equal("HostOptions", diagnostic.Properties["ConfigurationArea"]);
        Assert.Null(diagnostic.Exception);
    }

    [Fact]
    public async Task ProgrammaticReferencesCollectionsKeysAndActionsUseSemanticContracts()
    {
        var registry = new ExposureRegistry();
        registry.For<_ProgrammaticModel>()
            .Reference("current", static model => model.Current)
            .Collection("items", static model => model.Items, static child => child.Key)
            .Action(
                "increase",
                [new ProgrammaticParameter(
                    "amount",
                    typeof(int),
                    options: [new SelectionOption(1, "One"), new SelectionOption(2, "Two")],
                    range: new ValueRange(1, 2),
                    unit: "items",
                    tags: ["mutation"])],
                static (model, arguments) => model.Count += (int)arguments["amount"]!);
        using var host = new UIEngineHost(new UIEngineHostOptions { Exposure = registry });
        var child = new _Child("child-1");
        var model = new _ProgrammaticModel { Current = child };
        model.Items.Add(child);
        var descriptor = (await host.DescribeAsync(host.RegisterRoot("model", model).Value)).Value;

        var reference = Assert.Single(descriptor.References);
        var collection = Assert.Single(descriptor.Collections);
        var action = Assert.Single(descriptor.Actions);
        var referenceResult = await reference.ReadAsync();
        var range = await collection.ReadAsync(CollectionReadRequest.Range(0, 10));
        var keyed = await ((ICollectionPathSelector)collection).SelectByKeyAsync("child-1");
        var invocation = await action.InvokeAsync(new Dictionary<string, object?> { ["amount"] = "2" });
        var completion = await invocation.Value.Completion;

        Assert.NotNull(referenceResult.Value);
        Assert.Equal(CollectionCapabilities.KEYED, collection.Capabilities & CollectionCapabilities.KEYED);
        Assert.Equal("child-1", Assert.Single(range.Value.Entries).Key?.Value);
        Assert.Single(keyed.Value);
        var parameter = Assert.Single(action.Parameters);
        Assert.Equal("items", parameter.Unit);
        Assert.Equal(["mutation"], parameter.Tags);
        Assert.Equal(2, completion.Value);
        Assert.Equal(2, model.Count);
    }

    [Fact]
    public async Task ProgrammaticDescriptorFactoryCanSupplySpecializedRoles()
    {
        var registry = new ExposureRegistry();
        registry.For<_ThirdPartyModel>().DescriptorFactory(
            static (_, _, handle, _) => ValueTask.FromResult(
                InteractionResult.Success<IObjectDescriptor>(new _FactoryDescriptor(handle.Identity))));
        using var host = new UIEngineHost(new UIEngineHostOptions { Exposure = registry });

        var descriptor = (await host.DescribeAsync(
            host.RegisterRoot("model", new _ThirdPartyModel()).Value)).Value;

        Assert.Equal("factory", descriptor.DisplayName);
    }

    [Fact]
    public void ProgrammaticDefinitionsValidateIdentifiersAndActionParametersAtHostConstruction()
    {
        var duplicateRegistry = new ExposureRegistry();
        duplicateRegistry.For<_ProgrammaticModel>()
            .Value("duplicate", static model => model.Count)
            .Reference("duplicate", static model => model.Current);
        var invalidActionRegistry = new ExposureRegistry();
        invalidActionRegistry.For<_ProgrammaticModel>().Action(
            "invalid",
            [new ProgrammaticParameter("open", typeof(List<>))],
            static (_, _) => 0);

        Assert.Throws<ArgumentException>(() =>
            new UIEngineHost(new UIEngineHostOptions { Exposure = duplicateRegistry }));
        Assert.Throws<ArgumentException>(() =>
            new UIEngineHost(new UIEngineHostOptions { Exposure = invalidActionRegistry }));
    }

    private sealed class _ThirdPartyModel
    {
        public int Count { get; set; }
    }

    private sealed class _ProgrammaticModel
    {
        public int Count;

        public _Child? Current { get; set; }

        public List<_Child> Items { get; } = [];
    }

    private sealed record _Child(string Key);

    private sealed class _FactoryDescriptor(ObjectIdentity identity) : IObjectDescriptor
    {
        public ObjectIdentity Identity { get; } = identity;
        public string TypeName => nameof(_ThirdPartyModel);
        public string DisplayName => "factory";
        public string? Summary => null;
        public IReadOnlyList<IValueDescriptor> Values => [];
        public IReadOnlyList<IReferenceDescriptor> References => [];
        public IReadOnlyList<ICollectionDescriptor> Collections => [];
        public IReadOnlyList<IActionDescriptor> Actions => [];
    }

    private sealed class _CustomModel;

    private sealed class _AnnotatedModel
    {
        [Expose]
        public int Count { get; set; } = 2;

        [Expose]
        public string Name { get; set; } = "model";

        [Summary]
        public string Description => $"reflected {Name}";
    }

    private sealed class _MarkerProvider(string message) : IObjectDescriptorProvider
    {
        public int InvocationCount { get; private set; }

        public bool CanDescribe(Type objectType) => objectType == typeof(_ThirdPartyModel) ||
            objectType == typeof(_CustomModel);

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
}
