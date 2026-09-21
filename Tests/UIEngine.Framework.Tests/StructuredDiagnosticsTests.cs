using UIEngine.Core;
using UIEngine.Core.Exposure;
using Xunit;

namespace UIEngine.Framework.Tests;

public sealed class StructuredDiagnosticsTests
{
    [Fact]
    public async Task RuntimeOperationsEmitStableStructuredAndRedactedDiagnostics()
    {
        const string SECRET = "secret-domain-value";
        var loggerFactory = new RecordingLoggerFactory();
        var registry = new ExposureRegistry();
        registry.For<_DiagnosticModel>()
            .Value("count", static model => model.Count, static (model, value) => model.Count = value)
            .Range("count", 0, 10)
            .Collection("items", static model => model.Items)
            .Action(
                "increment",
                [],
                static (model, _) => ++model.Count);
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            Exposure = registry,
            LoggerFactory = loggerFactory,
        });
        var handle = host.RegisterRoot("root", new _DiagnosticModel()).Value;
        var descriptor = (await host.DescribeAsync(handle)).Value;
        var value = Assert.Single(descriptor.Values);
        var collection = Assert.Single(descriptor.Collections);
        var action = Assert.Single(descriptor.Actions);

        Assert.False((await value.WriteAsync(20)).IsSuccess);
        Assert.True((await value.WriteAsync(2)).IsSuccess);
        Assert.False((await value.WriteAsync(SECRET)).IsSuccess);
        Assert.False((await collection.ReadAsync(CollectionReadRequest.Page(0, 1))).IsSuccess);
        var invocation = await action.InvokeAsync(new Dictionary<string, object?>());
        Assert.True((await invocation.Value.Completion).IsSuccess);
        Assert.False((await action.InvokeAsync(
            new Dictionary<string, object?> { [SECRET] = 1 })).IsSuccess);
        Assert.True((await host.Bindings.ResolveAsync(_Binding("count"))).IsResolved);
        Assert.False((await host.Bindings.ResolveAsync(_Binding("missing"))).IsResolved);

        var eventIds = loggerFactory.Entries.Select(static entry => entry.EventId).ToArray();
        Assert.Contains(UIEngineDiagnosticEventIds.VALIDATION_FAILED, eventIds);
        Assert.Contains(UIEngineDiagnosticEventIds.MUTATION_COMPLETED, eventIds);
        Assert.Contains(UIEngineDiagnosticEventIds.MUTATION_FAILED, eventIds);
        Assert.Contains(UIEngineDiagnosticEventIds.COLLECTION_ACCESS_FAILED, eventIds);
        Assert.Contains(UIEngineDiagnosticEventIds.CAPABILITY_MISMATCH, eventIds);
        Assert.Contains(UIEngineDiagnosticEventIds.INVOCATION_STARTED, eventIds);
        Assert.Contains(UIEngineDiagnosticEventIds.INVOCATION_COMPLETED, eventIds);
        Assert.Contains(UIEngineDiagnosticEventIds.INVOCATION_FAILED, eventIds);
        Assert.Contains(UIEngineDiagnosticEventIds.BINDING_RESOLVED, eventIds);
        Assert.Contains(UIEngineDiagnosticEventIds.BINDING_BROKEN, eventIds);
        Assert.DoesNotContain(
            loggerFactory.Entries,
            entry => entry.Message.Contains(SECRET, StringComparison.Ordinal) || entry.Exception is not null);
    }

    private static BindingReference _Binding(string memberId) => new(
        null,
        "/root",
        memberId,
        DescriptorKind.VALUE,
        typeof(int).FullName,
        BindingFallbackPolicy.PATH_ONLY);

    private sealed class _DiagnosticModel
    {
        public int Count { get; set; }

        public List<int> Items { get; } = [1, 2];
    }
}
