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
        var options = new UIEngineHostOptions
        {
            DescriptorProviders =
            [
                new ReflectionObjectDescriptorProvider(),
                new ReflectionObjectDescriptorProvider(),
            ],
        };

        Assert.Throws<ArgumentException>(() => new UIEngineHost(options));
    }

    private sealed class _ThirdPartyModel
    {
        public int Count { get; set; }
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
