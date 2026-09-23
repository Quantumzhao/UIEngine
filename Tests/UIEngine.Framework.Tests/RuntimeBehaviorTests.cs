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
    public void ReflectionExposesConcreteMembersAndValidatesWrites()
    {
        var model = new _EditingModel();
        using var host = new UIEngineHost();
        host.SetRoot("model", model);

        var resolved = host.ResolveRootNode("model");

        Assert.True(resolved.IsSuccess);
        var members = ((IObjectNode)resolved.Value.Node).Members;
        var count = Assert.Single(members, value => value.Name == "Count");
        var name = Assert.Single(members, value => value.Name == "Name");
        var optional = Assert.Single(members, value => value.Name == "Optional");
        var state = Assert.Single(members, value => value.Name == "State");
        var field = Assert.Single(members, value => value.Name == "Field");
        var readOnly = Assert.Single(members, value => value.Name == "ReadOnly");

        Assert.False(count is INullableValueNode);
        Assert.True(optional is INullableValueNode);
        Assert.Equal(2, ((IEnumNode)state).Options.Count);
        Assert.True(field is IWritableValueNode);
        Assert.False(readOnly is IWritableValueNode);

        var converted = ((IWritableValueNode)count).WriteValue("7");
        var outOfRange = ((IWritableValueNode)count).WriteValue("12");
        var required = ((IWritableValueNode)name).WriteValue(null);
        var enumWrite = ((IWritableValueNode)state).WriteValue("READY");

        Assert.True(converted.IsSuccess);
        Assert.Equal(7, model.Count);
        Assert.Equal(InteractionErrorCode.VALIDATION_FAILED, outOfRange.Error?.Code);
        Assert.Equal(ValidationIssueCode.OUT_OF_RANGE, Assert.Single(outOfRange.Error!.Issues).Code);
        Assert.Equal(InteractionErrorCode.VALIDATION_FAILED, required.Error?.Code);
        Assert.Equal(_EditingState.READY, enumWrite.Value);
    }

    [Fact]
    public void ImmutableExposureUsesTheSameLiveValueNode()
    {
        var profile = new EconomicProfile("N1", 100m);
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            Exposures = CyclicWorldFactory.CreateExposures(),
        });
        host.SetRoot("economy", profile);

        var resolved = host.ResolveRootNode("economy");
        var objectNode = (IObjectNode)resolved.Value.Node;
        var valueNode = Assert.Single(objectNode.Members);
        Assert.True(valueNode is IWritableValueNode);
        var value = (IWritableValueNode)valueNode;
        var write = value.WriteValue("125.5");
        var rejected = value.WriteValue("10000001");

        Assert.Equal(125.5m, write.Value);
        Assert.Equal(125.5m, profile.GrossDomesticProduct);
        Assert.Equal(InteractionErrorCode.VALIDATION_FAILED, rejected.Error?.Code);
    }

    [Fact]
    public void ProgrammaticExposureRetainsItsTypedRange()
    {
        var range = new ValueRange<decimal>(0m, 10m);
        var exposure = new ValueExposure<EconomicProfile, decimal>(
            "GrossDomesticProduct",
            static profile => profile.GrossDomesticProduct,
            range: range);

        var exposedRange = Assert.IsType<ValueRange<decimal>>(exposure.Range);
        Assert.Same(range, exposedRange);
        Assert.Equal(0m, exposedRange.Minimum);
        Assert.Equal(10m, exposedRange.Maximum);
        Assert.Same(range, ((ValueExposure)exposure).Range);
        Assert.Throws<ArgumentException>(() => new ValueExposure<EconomicProfile, decimal>(
            "GrossDomesticProduct",
            static profile => profile.GrossDomesticProduct,
            range: new ValueRange<decimal>(10m, 0m)));
    }

    [Fact]
    public void CollectionReadsAreBoundedAndUseClosedEntryTypes()
    {
        var model = new _CollectionModel();
        using var host = new UIEngineHost(new UIEngineHostOptions { MaxCollectionItems = 3 });
        host.SetRoot("model", model);
        var resolved = host.ResolveRootNode("model");
        var members = ((IObjectNode)resolved.Value.Node).Members
            .Where(static member => member is ICollectionNode)
            .ToDictionary(static member => member.Name, static member => (ICollectionNode)member);

        var items = members["Items"].ReadEntries(0, 3);
        var dictionary = members["ByName"].ReadEntries(0, 3);
        var lazy = members["Lazy"].ReadEntries(1, 2);
        var tooLarge = members["Items"].ReadEntries(0, 4);

        Assert.IsType<NullCollectionEntry>(items.Value.Entries[0]);
        Assert.Equal(7, Assert.IsType<ScalarCollectionEntry>(items.Value.Entries[1]).Value);
        Assert.IsType<ReferenceCollectionEntry>(items.Value.Entries[2]);
        Assert.Equal("none", dictionary.Value.Entries[0].Key);
        Assert.Equal(1, Assert.IsType<ScalarCollectionEntry>(lazy.Value.Entries[0]).Value);
        Assert.Equal(2, Assert.IsType<ScalarCollectionEntry>(lazy.Value.Entries[1]).Value);
        Assert.Equal(InteractionErrorCode.INVALID_INPUT, tooLarge.Error?.Code);
    }

    [Fact]
    public void PathsPreserveCyclesAndFollowReplacementObjects()
    {
        using var host = _CreateWorldHost();
        var nation = host.ResolvePath("/world/Nations[index=0]");
        var cycled = host.ResolvePath(
            "/world/Nations[index=0]/Capital/OwnerNation");
        var legacyAlias = host.ResolvePath("/world/Nations/0");

        Assert.True(nation.IsSuccess);
        Assert.True(cycled.IsSuccess);
        Assert.Equal(
            ((IObjectNode)nation.Value.Node).Handle,
            ((IObjectNode)cycled.Value.Node).Handle);
        Assert.Equal(
            "/world/Nations[index=0]/Capital/OwnerNation",
            cycled.Value.CanonicalPath.ToString());
        Assert.False(legacyAlias.IsSuccess);

        var original = host.ResolvePath("/world/Nations[index=0]");
        host.SetRoot("world", CyclicWorldFactory.Create());
        var replacement = host.ResolvePath("/world/Nations[index=0]");

        Assert.True(original.IsSuccess);
        Assert.True(replacement.IsSuccess);
        Assert.NotEqual(
            ((IObjectNode)original.Value.Node).Handle,
            ((IObjectNode)replacement.Value.Node).Handle);
    }

    [Fact]
    public void IndexSelectorsFollowOrderAndKeySelectorsFollowCorrespondence()
    {
        var first = new _DomainObject("first");
        var second = new _DomainObject("second");
        var model = new _SelectorModel(first, second);
        using var host = new UIEngineHost();
        host.SetRoot("model", model);

        var initialByKey = host.ResolvePath("/model/ByKey[key=a]");
        var initialByIndex = host.ResolvePath("/model/Items[index=0]");
        model.ByKey["a"] = second;
        model.Items.Reverse();
        var remappedByKey = host.ResolvePath("/model/ByKey[key=a]");
        var reorderedByIndex = host.ResolvePath("/model/Items[index=0]");

        Assert.True(initialByKey.IsSuccess);
        Assert.True(initialByIndex.IsSuccess);
        Assert.True(remappedByKey.IsSuccess);
        Assert.True(reorderedByIndex.IsSuccess);
        Assert.Equal(
            ((IObjectNode)initialByKey.Value.Node).Handle,
            ((IObjectNode)initialByIndex.Value.Node).Handle);
        Assert.Equal(
            ((IObjectNode)remappedByKey.Value.Node).Handle,
            ((IObjectNode)reorderedByIndex.Value.Node).Handle);
        Assert.NotEqual(
            ((IObjectNode)initialByIndex.Value.Node).Handle,
            ((IObjectNode)reorderedByIndex.Value.Node).Handle);
    }

    [Fact]
    public void PathsResolveFreshTerminalAndCollectionNodes()
    {
        using var host = _CreateWorldHost();

        var firstName = host.ResolvePath("/world/Name");
        var secondName = host.ResolvePath("/world/Name");
        var nations = host.ResolvePath("/world/Nations");
        var advance = host.ResolvePath(
            "/world/Nations[index=0]/AdvanceTurn");

        Assert.True(firstName.IsSuccess);
        Assert.True(secondName.IsSuccess);
        Assert.NotSame(firstName.Value.Node, secondName.Value.Node);
        Assert.True(firstName.Value.Node is IReadableValueNode and IStringNode);
        Assert.True(firstName.Value.Node.IsTerminal);
        Assert.IsAssignableFrom<ICollectionNode>(nations.Value.Node);
        Assert.IsAssignableFrom<IMethodNode>(advance.Value.Node);
        Assert.True(advance.Value.Node.IsTerminal);
    }

    [Fact]
    public async Task ActionsSupportSyncTaskStatusAndDomainFaults()
    {
        var model = new _ActionModel();
        using var host = new UIEngineHost();
        host.SetRoot("model", model);
        var resolved = host.ResolveRootNode("model");
        var actions = ((IObjectNode)resolved.Value.Node).Members
            .Where(static member => member is IMethodNode)
            .ToDictionary(static member => member.Name, static member => (IMethodNode)member);

        Assert.Null(actions["ObserveStatus"].Status);
        model.ReadInvocationStatus = () => actions["ObserveStatus"].Status;
        var observed = actions["ObserveStatus"].Invoke(new Dictionary<string, object?>());
        Assert.Equal(InvocationStatus.RUNNING, (await observed.Value.Completion).Value);
        Assert.Equal(InvocationStatus.SUCCEEDED, actions["ObserveStatus"].Status);

        var added = actions["Add"].Invoke(new Dictionary<string, object?>
        {
            ["left"] = "2",
            ["right"] = 3,
        });
        Assert.Equal(5, (await added.Value.Completion).Value);

        var worked = actions["WorkAsync"].Invoke(new Dictionary<string, object?>
        {
            ["steps"] = 3,
        });
        Assert.Equal(InvocationStatus.RUNNING, actions["WorkAsync"].Status);
        model.CompleteWork();
        Assert.Equal(3, (await worked.Value.Completion).Value);
        Assert.Equal(InvocationStatus.SUCCEEDED, actions["WorkAsync"].Status);

        var failed = actions["Fail"].Invoke(new Dictionary<string, object?>());
        var fault = await failed.Value.Completion;
        Assert.Equal(InvocationStatus.FAILED, failed.Value.Status);
        Assert.Equal(InteractionErrorCode.FAULT, fault.Error?.Code);
        Assert.IsType<InvalidOperationException>(failed.Value.Fault);
        Assert.Equal(InvocationStatus.FAILED, actions["Fail"].Status);
    }

    [Fact]
    public async Task HostDisposalCompletesRunningInvocations()
    {
        var host = new UIEngineHost();
        host.SetRoot("model", new _ActionModel());
        var resolved = host.ResolveRootNode("model");
        var wait = Assert.Single(
            ((IObjectNode)resolved.Value.Node).Members,
            action => action.Name == "WaitAsync");
        var started = ((IMethodNode)wait).Invoke(new Dictionary<string, object?>());

        host.Dispose();

        var completion = await started.Value.Completion;
        Assert.Equal(InteractionErrorCode.DISPOSED, completion.Error?.Code);
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
            "ActionDescriptor",
            "ActionParameter",
            "BindingReference",
            "ChangeKind",
            "ChangeRecord",
            "CollectionDescriptor",
            "IObjectDescriptorProvider",
            "IObjectDescriptorFactory",
            "IObservationAdapter",
            "IObjectIdentityProvider",
            "IPathSelector",
            "IInteractionDispatchPolicy",
            "IInteractionDispatcher",
            "MemberDescriptor",
            "MemberKind",
            "ObjectDescriptor",
            "ObservationSubscription",
            "ObservationValue",
            "ObjectIdentity",
            "ReferenceDescriptor",
            "ResolvedBinding",
            "BindingResolution",
            "PathResolutionState",
            "ValueDescriptor",
        };

        Assert.Equal(
            [
                "IBooleanNode",
                "ICharacterNode",
                "ICollectionNode",
                "IEnumNode",
                "IFieldNode",
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
                "IStringNode",
                "IValueRange",
                "IWritableValueNode",
            ],
            interfaces);
        foreach (var name in removed)
        {
            Assert.DoesNotContain(assembly.GetExportedTypes(), type => type.Name == name);
        }

        Assert.DoesNotContain(
            typeof(UIEngineHost).GetMethods(),
            static method => method.Name == "ObserveAsync");
        Assert.DoesNotContain(
            typeof(UIEngineHost).GetMethods(),
            static method => method.Name is "DescribeAsync" or "ResolveBindingAsync");
        Assert.DoesNotContain(
            typeof(UIEngineHost).GetMethods(),
            static method => method.Name is "ResolveRootNodeAsync" or "ResolvePathAsync");
        Assert.Equal(
            typeof(InteractionResult<object?>),
            typeof(IReadableValueNode).GetMethod(nameof(IReadableValueNode.ReadValue))!.ReturnType);
        Assert.Equal(
            typeof(InteractionResult<ActionInvocation>),
            typeof(IMethodNode).GetMethod(nameof(IMethodNode.Invoke))!.ReturnType);
        Assert.Equal(
            typeof(InvocationStatus?),
            typeof(IMethodNode).GetProperty(nameof(IMethodNode.Status))!.PropertyType);
        Assert.Null(typeof(IMethodNode).GetProperty("ProgressType"));
        Assert.Null(typeof(ActionInvocation).GetMethod("ReadProgressAsync"));
        Assert.Null(typeof(UIEngineHostOptions).GetProperty("Dispatcher"));
        Assert.Null(typeof(UIEngineHost).GetProperty("IsDisposed"));
        Assert.Null(typeof(UIEngineHostOptions).GetProperty("ObservationBufferCapacity"));
        Assert.Null(typeof(UIEngineHostOptions).GetProperty("InvocationProgressBufferCapacity"));
        Assert.DoesNotContain(
            assembly.GetExportedTypes(),
            static type => type.Name == "InvocationProgress");
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
        public object?[] Items { get; } = [null, 7, new _DomainObject("entry")];

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

    private sealed class _DomainObject(string key)
    {
        [Expose]
        public string Key { get; } = key;
    }

    private sealed class _SelectorModel(_DomainObject first, _DomainObject second)
    {
        [Children]
        public Dictionary<string, _DomainObject> ByKey { get; } = new() { ["a"] = first };

        [Children]
        public List<_DomainObject> Items { get; } = [first, second];
    }

    private sealed class _ActionModel
    {
        private int _InvocationCount;
        private readonly TaskCompletionSource _WorkCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Func<InvocationStatus?> ReadInvocationStatus { get; set; } = () => null;

        public void CompleteWork() => _WorkCompletion.TrySetResult();

        [Action]
        public InvocationStatus? ObserveStatus()
        {
            _InvocationCount++;
            return ReadInvocationStatus();
        }

        [Action]
        public int Add(int left, int right = 1)
        {
            _InvocationCount++;
            return left + right;
        }

        [Action]
        public async Task<int> WorkAsync(
            [Range(1, 5)] int steps)
        {
            _InvocationCount++;
            await _WorkCompletion.Task;

            return steps;
        }

        [Action]
        public Task WaitAsync()
        {
            _InvocationCount++;
            return new TaskCompletionSource().Task;
        }

        [Action]
        public void Fail()
        {
            _InvocationCount++;
            throw new InvalidOperationException("domain failure");
        }
    }
}
