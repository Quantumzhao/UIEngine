using System.Collections;
using System.ComponentModel.DataAnnotations;
using UIEngine.Core;
using UIEngine.Core.Attributes;
using UIEngine.Examples.CyclicDomain;
using Xunit;

namespace UIEngine.Framework.Tests;

public sealed class RuntimeBehaviorTests
{
    [Fact]
    public async Task ReflectionExposesConcreteMembersAndValidatesWrites()
    {
        var model = new _EditingModel();
        using var host = new UIEngineHost();
        var root = host.SetRoot("model", model);

        var described = await host.DescribeAsync(root.Value);

        Assert.True(described.IsSuccess);
        var members = described.Value.Members;
        var count = Assert.Single(members.OfType<ValueDescriptor>(), value => value.Id == "Count");
        var name = Assert.Single(members.OfType<ValueDescriptor>(), value => value.Id == "Name");
        var optional = Assert.Single(members.OfType<ValueDescriptor>(), value => value.Id == "Optional");
        var state = Assert.Single(members.OfType<ValueDescriptor>(), value => value.Id == "State");
        var field = Assert.Single(members.OfType<ValueDescriptor>(), value => value.Id == "Field");
        var readOnly = Assert.Single(members.OfType<ValueDescriptor>(), value => value.Id == "ReadOnly");

        Assert.False(count.IsNullable);
        Assert.True(optional.IsNullable);
        Assert.Equal(2, state.Options.Count);
        Assert.True(field.CanWrite);
        Assert.False(readOnly.CanWrite);

        var converted = await count.WriteAsync("7");
        var outOfRange = await count.WriteAsync("12");
        var required = await name.WriteAsync(null);
        var enumWrite = await state.WriteAsync("READY");
        var rejectedReadOnly = await readOnly.WriteAsync("changed");

        Assert.True(converted.IsSuccess);
        Assert.Equal(7, model.Count);
        Assert.Equal(InteractionErrorCode.VALIDATION_FAILED, outOfRange.Error?.Code);
        Assert.Equal(ValidationIssueCode.OUT_OF_RANGE, Assert.Single(outOfRange.Error!.Issues).Code);
        Assert.Equal(InteractionErrorCode.VALIDATION_FAILED, required.Error?.Code);
        Assert.Equal(_EditingState.READY, enumWrite.Value);
        Assert.Equal(InteractionErrorCode.VALIDATION_FAILED, rejectedReadOnly.Error?.Code);
    }

    [Fact]
    public async Task ImmutableExposureUsesTheSameLiveValueDescriptor()
    {
        var profile = new EconomicProfile("N1", 100m);
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            Exposures = CyclicWorldFactory.CreateExposures(),
        });
        var root = host.SetRoot("economy", profile);

        var described = await host.DescribeAsync(root.Value);
        var value = Assert.Single(described.Value.Members.OfType<ValueDescriptor>());
        var write = await value.WriteAsync("125.5");
        var rejected = await value.WriteAsync("10000001");

        Assert.Equal("economy/N1", described.Value.DomainIdentity?.Value);
        Assert.Equal(125.5m, write.Value);
        Assert.Equal(125.5m, profile.GrossDomesticProduct);
        Assert.Equal(InteractionErrorCode.VALIDATION_FAILED, rejected.Error?.Code);
    }

    [Fact]
    public async Task CollectionReadsAreBoundedAndUseClosedEntryTypes()
    {
        var model = new _CollectionModel();
        using var host = new UIEngineHost(new UIEngineHostOptions { MaxCollectionItems = 3 });
        var root = host.SetRoot("model", model);
        var described = await host.DescribeAsync(root.Value);
        var members = described.Value.Members.OfType<CollectionDescriptor>().ToDictionary(
            static member => member.Id,
            StringComparer.Ordinal);

        var items = await members["Items"].ReadAsync(0, 3);
        var dictionary = await members["ByName"].ReadAsync(0, 3);
        var lazy = await members["Lazy"].ReadAsync(1, 2);
        var tooLarge = await members["Items"].ReadAsync(0, 4);

        Assert.IsType<NullCollectionEntry>(items.Value.Entries[0]);
        Assert.Equal(7, Assert.IsType<ScalarCollectionEntry>(items.Value.Entries[1]).Value);
        Assert.IsType<ReferenceCollectionEntry>(items.Value.Entries[2]);
        Assert.Equal("none", dictionary.Value.Entries[0].Key);
        Assert.Equal(1, Assert.IsType<ScalarCollectionEntry>(lazy.Value.Entries[0]).Value);
        Assert.Equal(2, Assert.IsType<ScalarCollectionEntry>(lazy.Value.Entries[1]).Value);
        Assert.Equal(InteractionErrorCode.INVALID_INPUT, tooLarge.Error?.Code);
    }

    [Fact]
    public async Task PathsPreserveCyclesAndBindingsSurviveCompatibleReplacement()
    {
        using var host = _CreateWorldHost();
        var nation = await host.ResolvePathAsync("/world/Nations[index=0]");
        var cycled = await host.ResolvePathAsync(
            "/world/Nations[index=0]/Capital/OwnerNation");
        var legacyAlias = await host.ResolvePathAsync("/world/Nations/0");

        Assert.True(nation.IsSuccess);
        Assert.True(cycled.IsSuccess);
        Assert.Equal(nation.Value.OwnerHandle, cycled.Value.OwnerHandle);
        Assert.Equal(
            "/world/Nations[index=0]/Capital/OwnerNation",
            cycled.Value.CanonicalPath.ToString());
        Assert.False(legacyAlias.IsSuccess);

        var binding = new BindingReference(
            "/world/Nations[index=0]",
            nameof(Nation.Population),
            MemberKind.VALUE,
            "nation/N1");
        var original = await host.ResolveBindingAsync(binding);
        host.SetRoot("world", CyclicWorldFactory.Create());
        var replacement = await host.ResolveBindingAsync(binding);

        Assert.True(original.IsSuccess);
        Assert.True(replacement.IsSuccess);
        Assert.NotEqual(original.Value.OwnerHandle, replacement.Value.OwnerHandle);

        host.SetRoot("world", new World("Mars"));
        var conflict = await host.ResolveBindingAsync(new BindingReference(
            "/world",
            nameof(World.Name),
            MemberKind.VALUE,
            "world/Earth"));
        Assert.Equal(InteractionErrorCode.NOT_FOUND, conflict.Error?.Code);
    }

    [Fact]
    public async Task KeyAndIdentitySelectorsResolveReferenceEntries()
    {
        var first = new _Identified("first");
        var model = new _SelectorModel(first);
        using var host = new UIEngineHost();
        host.SetRoot("model", model);

        var byKey = await host.ResolvePathAsync("/model/ByKey[key=a]");
        var byIdentity = await host.ResolvePathAsync("/model/Items[identity=item%2Ffirst]");

        Assert.True(byKey.IsSuccess);
        Assert.True(byIdentity.IsSuccess);
        Assert.Equal(byKey.Value.OwnerHandle, byIdentity.Value.OwnerHandle);
    }

    [Fact]
    public async Task ActionsSupportSyncTaskProgressCancellationAndDomainFaults()
    {
        var model = new _ActionModel();
        using var host = new UIEngineHost();
        var root = host.SetRoot("model", model);
        var described = await host.DescribeAsync(root.Value);
        var actions = described.Value.Members.OfType<ActionDescriptor>().ToDictionary(
            static action => action.Id,
            StringComparer.Ordinal);

        var added = await actions["Add"].InvokeAsync(new Dictionary<string, object?>
        {
            ["left"] = "2",
            ["right"] = 3,
        });
        Assert.Equal(5, (await added.Value.Completion).Value);

        var worked = await actions["WorkAsync"].InvokeAsync(new Dictionary<string, object?>
        {
            ["steps"] = 3,
        });
        var progress = new List<InvocationProgress>();
        await foreach (var update in worked.Value.ReadProgressAsync())
        {
            progress.Add(update);
        }

        Assert.Equal([1, 2, 3], progress.Select(static update => (int)update.Value!).ToArray());
        Assert.Equal(3, (await worked.Value.Completion).Value);

        var waiting = await actions["WaitAsync"].InvokeAsync(new Dictionary<string, object?>());
        Assert.True(waiting.Value.Cancel());
        var cancelled = await waiting.Value.Completion;
        Assert.Equal(InvocationStatus.CANCELLED, waiting.Value.Status);
        Assert.Equal(InteractionErrorCode.CANCELLED, cancelled.Error?.Code);

        var failed = await actions["Fail"].InvokeAsync(new Dictionary<string, object?>());
        var fault = await failed.Value.Completion;
        Assert.Equal(InvocationStatus.FAILED, failed.Value.Status);
        Assert.Equal(InteractionErrorCode.FAULT, fault.Error?.Code);
        Assert.IsType<InvalidOperationException>(failed.Value.Fault);
    }

    [Fact]
    public async Task HostDisposalCancelsRunningInvocationsAndRejectsFurtherWork()
    {
        var host = new UIEngineHost();
        var root = host.SetRoot("model", new _ActionModel());
        var described = await host.DescribeAsync(root.Value);
        var wait = Assert.Single(
            described.Value.Members.OfType<ActionDescriptor>(),
            action => action.Id == "WaitAsync");
        var started = await wait.InvokeAsync(new Dictionary<string, object?>());

        host.Dispose();

        var completion = await started.Value.Completion;
        var afterDispose = await host.DescribeAsync(root.Value);
        Assert.Equal(InteractionErrorCode.DISPOSED, completion.Error?.Code);
        Assert.Equal(InteractionErrorCode.DISPOSED, afterDispose.Error?.Code);
    }

    [Fact]
    public void PublicApiContainsOnlyIntentionalExtensionAndNodeSemanticInterfaces()
    {
        var assembly = typeof(UIEngineHost).Assembly;
        var interfaces = assembly.GetExportedTypes()
            .Where(static type => type.IsInterface)
            .Select(static type => type.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        var removed = new[]
        {
            "IObjectDescriptorProvider",
            "IObjectDescriptorFactory",
            "IObservationAdapter",
            "IObjectIdentityProvider",
            "IPathSelector",
            "IInteractionDispatchPolicy",
            "ObjectIdentity",
            "BindingResolution",
            "PathResolutionState",
        };

        Assert.Equal(
            [
                "IBooleanNode",
                "ICharacterNode",
                "ICollectionNode",
                "IEnumNode",
                "IFieldNode",
                "IInteractionDispatcher",
                "IMemberNode",
                "IMethodNode",
                "INavigableNode",
                "INullableValueNode",
                "INumberNode",
                "IObjectNode",
                "IProgrammaticValueNode",
                "IPropertyNode",
                "IReadableValueNode",
                "IReferenceNode",
                "IStableDomainIdentity",
                "IStringNode",
                "IWritableValueNode",
            ],
            interfaces);
        foreach (var name in removed)
        {
            Assert.DoesNotContain(assembly.GetExportedTypes(), type => type.Name == name);
        }
    }

    private static UIEngineHost _CreateWorldHost()
    {
        var host = new UIEngineHost(new UIEngineHostOptions
        {
            Exposures = CyclicWorldFactory.CreateExposures(),
        });
        host.SetRoot("world", CyclicWorldFactory.Create());
        return host;
    }

    private enum _EditingState
    {
        NEW,
        READY,
    }

    private sealed class _EditingModel
    {
        [Expose]
        [Range(0, 10)]
        public int Count { get; set; }

        [Expose]
        [Required]
        public string Name { get; set; } = "initial";

        [Expose]
        public string? Optional { get; set; }

        [Expose]
        public _EditingState State { get; set; }

        [Expose]
        public int Field = 0;

        [Expose(ReadOnly = true)]
        public string ReadOnly { get; set; } = "fixed";
    }

    private sealed class _CollectionModel(int start = 0)
    {
        private readonly int _Start = start;

        [Children]
        public object?[] Items { get; } = [null, 7, new _Identified("entry")];

        [Children]
        public Dictionary<string, object?> ByName { get; } = new()
        {
            ["none"] = null,
            ["number"] = 2,
        };

        [Children]
        public IEnumerable<int> Lazy => _Yield();

        private IEnumerable<int> _Yield()
        {
            for (var index = 0; index < 10; index++)
            {
                yield return _Start + index;
            }
        }
    }

    private sealed class _Identified(string id) : IStableDomainIdentity
    {
        public string DomainIdentity => $"item/{Id}";

        [Expose]
        public string Id { get; } = id;
    }

    private sealed class _SelectorModel(_Identified item)
    {
        [Children]
        public Dictionary<string, _Identified> ByKey { get; } = new() { ["a"] = item };

        [Children]
        public IReadOnlyList<_Identified> Items { get; } = [item];
    }

    private sealed class _ActionModel
    {
        private int _InvocationCount;

        [Action]
        public int Add(int left, int right = 1)
        {
            _InvocationCount++;
            return left + right;
        }

        [Action]
        public async Task<int> WorkAsync(
            [Range(1, 5)] int steps,
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            _InvocationCount++;
            for (var step = 1; step <= steps; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                progress.Report(step);
            }

            return steps;
        }

        [Action]
        public Task WaitAsync(CancellationToken cancellationToken)
        {
            _InvocationCount++;
            return Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }

        [Action]
        public void Fail()
        {
            _InvocationCount++;
            throw new InvalidOperationException("domain failure");
        }
    }
}
