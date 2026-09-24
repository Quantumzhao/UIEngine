using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Runtime.CompilerServices;
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
    public void PathsPreserveCyclesSharedReferencesAndReplacementObjects()
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
        Assert.NotSame(nation.Value.Node, cycled.Value.Node);
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
    public void PathsResolveEveryNodeKindAndRetainReferenceFacets()
    {
        using var host = _CreateWorldHost();

        var root = host.ResolvePath("/world");
        var scalar = host.ResolvePath("/world/Name");
        var collection = host.ResolvePath("/world/Nations");
        var selected = host.ResolvePath("/world/Nations[index=0]");
        var reference = host.ResolvePath("/world/Nations[index=0]/Capital");
        var method = host.ResolvePath("/world/Nations[index=0]/AdvanceTurn");

        Assert.IsAssignableFrom<IObjectNode>(root.Value.Node);
        Assert.True(scalar.Value.Node is IReadableValueNode);
        Assert.IsAssignableFrom<ICollectionNode>(collection.Value.Node);
        Assert.IsAssignableFrom<IObjectNode>(selected.Value.Node);
        Assert.IsAssignableFrom<IObjectNode>(reference.Value.Node);
        Assert.True(reference.Value.Node is IReferenceNode);
        Assert.True(reference.Value.Node is IPropertyNode);
        Assert.IsAssignableFrom<IMethodNode>(method.Value.Node);
        Assert.Equal(typeof(City), reference.Value.Node.ValueType);
        Assert.Equal(typeof(City), ((IReferenceNode)reference.Value.Node).ReferenceType);
        Assert.Equal(typeof(Nation), ((IPropertyNode)reference.Value.Node).DeclaringType);
    }

    [Fact]
    public void ResolutionChainContainsEverySemanticAncestor()
    {
        using var host = _CreateWorldHost();

        var first = host.ResolvePath(
            "/world/Nations[index=0]/Capital/Name");
        var second = host.ResolvePath(
            "/world/Nations[index=0]/Capital/Name");

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(
            [
                "/world",
                "/world/Nations",
                "/world/Nations[index=0]",
                "/world/Nations[index=0]/Capital",
                "/world/Nations[index=0]/Capital/Name",
            ],
            first.Value.ResolutionChain.Select(static node => node.CanonicalPath.ToString()));
        Assert.Same(first.Value.Node, first.Value.ResolutionChain[^1].Node);
        Assert.All(
            first.Value.ResolutionChain.Zip(second.Value.ResolutionChain),
            static pair => Assert.NotSame(pair.First.Node, pair.Second.Node));
        Assert.Equal(
            ((IObjectNode)first.Value.ResolutionChain[2].Node).Handle,
            ((IObjectNode)second.Value.ResolutionChain[2].Node).Handle);
    }

    [Fact]
    public void PathsPreserveEscapingAndCaseSensitivity()
    {
        var model = new _EscapedModel();
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            Exposures =
            [
                new TypeExposure<_EscapedModel>(
                [
                    new ValueExposure<_EscapedModel, string>(
                        "value/name",
                        static value => value.Value),
                ]),
            ],
        });
        host.SetRoot("root/name", model);

        var resolved = host.ResolvePath("/root%2Fname/value%2Fname");
        var wrongRootCase = host.ResolvePath("/Root%2Fname/value%2Fname");
        var wrongMemberCase = host.ResolvePath("/root%2Fname/Value%2Fname");

        Assert.True(resolved.IsSuccess);
        Assert.Equal("/root%2Fname/value%2Fname", resolved.Value.CanonicalPath.ToString());
        Assert.Equal(InteractionErrorCode.NOT_FOUND, wrongRootCase.Error?.Code);
        Assert.Equal(InteractionErrorCode.NOT_FOUND, wrongMemberCase.Error?.Code);
    }

    [Fact]
    public void PathFailuresRemainStructured()
    {
        var model = new _PathFailureModel();
        using var host = new UIEngineHost();
        host.SetRoot("model", model);

        var nullReference = host.ResolvePath("/model/Child");
        var missingMember = host.ResolvePath("/model/Missing");
        var missingElement = host.ResolvePath("/model/Items[index=2]");
        var scalarElement = host.ResolvePath("/model/Items[index=0]");
        var ambiguousElement = host.ResolvePath("/model/Duplicates[key=duplicate]");
        var fieldReference = host.ResolvePath("/model/FieldChild");
        var keyedList = host.ResolvePath("/model/Items[key=0]");
        var indexedDictionary = host.ResolvePath("/model/Duplicates[index=0]");
        var runtimeList = host.ResolvePath("/model/ListView[index=0]");
        var runtimeDictionary = host.ResolvePath("/model/DictView[key=item]");

        Assert.Equal(InteractionErrorCode.UNAVAILABLE, nullReference.Error?.Code);
        Assert.Equal(InteractionErrorCode.NOT_FOUND, missingMember.Error?.Code);
        Assert.Equal(InteractionErrorCode.NOT_FOUND, missingElement.Error?.Code);
        Assert.Equal(InteractionErrorCode.TYPE_MISMATCH, scalarElement.Error?.Code);
        Assert.Equal(InteractionErrorCode.AMBIGUOUS, ambiguousElement.Error?.Code);
        Assert.True(fieldReference.Value.Node is IObjectNode and IReferenceNode and IFieldNode);
        Assert.Equal(InteractionErrorCode.TYPE_MISMATCH, keyedList.Error?.Code);
        Assert.Equal(InteractionErrorCode.TYPE_MISMATCH, indexedDictionary.Error?.Code);
        Assert.True(runtimeList.IsSuccess);
        Assert.True(runtimeDictionary.IsSuccess);
    }

    [Fact]
    public void ResolvedReferenceMembersReportUnavailableCollectedTargets()
    {
        var model = new _PathFailureModel();
        using var host = new UIEngineHost();
        host.SetRoot("model", model);
        var (childMember, target) = _ResolveTemporaryChild(host, model);

        for (var attempt = 0; attempt < 3 && target.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        var read = childMember.ReadValue();

        Assert.False(target.IsAlive);
        Assert.Equal(InteractionErrorCode.UNAVAILABLE, read.Error?.Code);
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
        Assert.Null(actions["ObserveStatus"].ResultTask);
        model.ReadInvocationStatus = () => actions["ObserveStatus"].Status;
        var observed = actions["ObserveStatus"].Invoke(new Dictionary<string, object?>());
        Assert.Equal(InvocationStatus.SUCCEEDED, observed.Value);
        Assert.Equal(
            InvocationStatus.RUNNING,
            (await actions["ObserveStatus"].ResultTask!).Value);
        Assert.Equal(InvocationStatus.SUCCEEDED, actions["ObserveStatus"].Status);

        var added = actions["Add"].Invoke(new Dictionary<string, object?>
        {
            ["left"] = "2",
            ["right"] = 3,
        });
        Assert.Equal(InvocationStatus.SUCCEEDED, added.Value);
        Assert.True(actions["Add"].ResultTask!.IsCompletedSuccessfully);
        Assert.Equal(5, (await actions["Add"].ResultTask!).Value);

        var previousResultTask = actions["Add"].ResultTask;
        var rejected = actions["Add"].Invoke(new Dictionary<string, object?>
        {
            ["missing"] = 1,
        });
        Assert.Equal(InteractionErrorCode.INVALID_INPUT, rejected.Error?.Code);
        Assert.Same(previousResultTask, actions["Add"].ResultTask);

        var worked = actions["WorkAsync"].Invoke(new Dictionary<string, object?>
        {
            ["steps"] = 3,
        });
        Assert.Equal(InvocationStatus.RUNNING, worked.Value);
        Assert.Equal(InvocationStatus.RUNNING, actions["WorkAsync"].Status);
        var workResultTask = actions["WorkAsync"].ResultTask!;
        Assert.False(workResultTask.IsCompleted);
        var invocationCount = model.InvocationCount;
        var overlapping = actions["WorkAsync"].Invoke(new Dictionary<string, object?>
        {
            ["steps"] = 4,
        });
        Assert.Equal(InteractionErrorCode.UNAVAILABLE, overlapping.Error?.Code);
        Assert.Equal(invocationCount, model.InvocationCount);
        model.CompleteWork();
        Assert.Equal(3, (await workResultTask).Value);
        Assert.Equal(InvocationStatus.SUCCEEDED, actions["WorkAsync"].Status);
        var completedWork = actions["WorkAsync"].ResultTask;
        var workedAgain = actions["WorkAsync"].Invoke(new Dictionary<string, object?>
        {
            ["steps"] = 2,
        });
        Assert.Equal(InvocationStatus.SUCCEEDED, workedAgain.Value);
        Assert.NotSame(completedWork, actions["WorkAsync"].ResultTask);
        Assert.Equal(3, (await completedWork!).Value);
        Assert.Equal(2, (await actions["WorkAsync"].ResultTask!).Value);

        var failed = actions["Fail"].Invoke(new Dictionary<string, object?>());
        Assert.Equal(InvocationStatus.FAILED, failed.Value);
        var fault = await actions["Fail"].ResultTask!;
        Assert.Equal(InteractionErrorCode.FAULT, fault.Error?.Code);
        Assert.Equal(InvocationStatus.FAILED, actions["Fail"].Status);

        var failedAsync = actions["FailAsync"].Invoke(new Dictionary<string, object?>());
        Assert.True(failedAsync.IsSuccess);
        var asyncFault = await actions["FailAsync"].ResultTask!;
        Assert.Equal(InvocationStatus.FAILED, actions["FailAsync"].Status);
        Assert.Equal(InteractionErrorCode.FAULT, asyncFault.Error?.Code);
    }

    [Fact]
    public async Task SeparateMethodNodeOccurrencesCanRunConcurrently()
    {
        var model = new _ActionModel();
        using var host = new UIEngineHost();
        host.SetRoot("model", model);
        var first = (IMethodNode)Assert.Single(
            ((IObjectNode)host.ResolveRootNode("model").Value.Node).Members,
            static member => member.Name == "WorkAsync");
        var second = (IMethodNode)Assert.Single(
            ((IObjectNode)host.ResolveRootNode("model").Value.Node).Members,
            static member => member.Name == "WorkAsync");

        var firstStarted = first.Invoke(new Dictionary<string, object?> { ["steps"] = 1 });
        var secondStarted = second.Invoke(new Dictionary<string, object?> { ["steps"] = 2 });

        Assert.Equal(InvocationStatus.RUNNING, firstStarted.Value);
        Assert.Equal(InvocationStatus.RUNNING, secondStarted.Value);
        Assert.NotSame(first.ResultTask, second.ResultTask);
        Assert.Equal(2, model.InvocationCount);

        model.CompleteWork();
        var results = await Task.WhenAll(first.ResultTask!, second.ResultTask!);
        Assert.Equal(1, results[0].Value);
        Assert.Equal(2, results[1].Value);
    }

    [Fact]
    public async Task HostDisposalDoesNotRewriteRunningInvocation()
    {
        var model = new _ActionModel();
        var host = new UIEngineHost();
        host.SetRoot("model", model);
        var resolved = host.ResolveRootNode("model");
        var work = Assert.Single(
            ((IObjectNode)resolved.Value.Node).Members,
            action => action.Name == "WorkAsync");
        var method = (IMethodNode)work;
        var started = method.Invoke(new Dictionary<string, object?> { ["steps"] = 5 });
        var resultTask = method.ResultTask!;

        host.Dispose();

        Assert.Equal(InvocationStatus.RUNNING, started.Value);
        Assert.False(resultTask.IsCompleted);
        Assert.Equal(InvocationStatus.RUNNING, method.Status);

        model.CompleteWork();
        var result = await resultTask;
        Assert.Equal(InvocationStatus.SUCCEEDED, method.Status);
        Assert.Equal(5, result.Value);
    }

    [Fact]
    public async Task ReleasingMethodNodeObservationDoesNotStopDomainTask()
    {
        var model = new _ActionModel();
        using var host = new UIEngineHost();
        host.SetRoot("model", model);

        _StartWorkWithoutRetainingNode(host);
        model.CompleteWork();

        await model.WorkFinished;
        Assert.Equal(1, model.CompletedWorkCount);
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
            "CollectionSelector",
            "CollectionSelectorKind",
            "CollectionDescriptor",
            "IObjectDescriptorProvider",
            "IObjectDescriptorFactory",
            "IObservationAdapter",
            "IObjectIdentityProvider",
            "IPathSelector",
            "IndexSelector",
            "IInteractionDispatchPolicy",
            "IInteractionDispatcher",
            "MemberDescriptor",
            "MemberKind",
            "ObjectDescriptor",
            "ObservationSubscription",
            "ObservationValue",
            "ObjectIdentity",
            "PathLocation",
            "ReferenceDescriptor",
            "ResolvedBinding",
            "BindingResolution",
            "PathResolutionState",
            "KeySelector",
            "ListSelector",
            "DictSelector",
            "ValueDescriptor",
        };

        Assert.Equal(
            [
                "IBooleanNode",
                "ICharacterNode",
                "ICollectionNode",
                "IEnumNode",
                "IFieldNode",
                "ILogicalPathSegment",
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
            typeof(InteractionResult<InvocationStatus>),
            typeof(IMethodNode).GetMethod(nameof(IMethodNode.Invoke))!.ReturnType);
        Assert.Equal(
            typeof(InvocationStatus?),
            typeof(IMethodNode).GetProperty(nameof(IMethodNode.Status))!.PropertyType);
        Assert.Equal(
            typeof(Task<InteractionResult<object>>),
            typeof(IMethodNode).GetProperty(nameof(IMethodNode.ResultTask))!.PropertyType);
        Assert.Null(typeof(IMethodNode).GetProperty("Completion"));
        Assert.Null(typeof(IMethodNode).GetProperty("Result"));
        Assert.Null(typeof(IMethodNode).GetProperty("ProgressType"));
        Assert.DoesNotContain(
            assembly.GetExportedTypes(),
            static type => type.Name == "ActionInvocation");
        Assert.Null(typeof(UIEngineHostOptions).GetProperty("Dispatcher"));
        Assert.Null(typeof(UIEngineHost).GetProperty("IsDisposed"));
        Assert.Null(typeof(UIEngineHostOptions).GetProperty("ObservationBufferCapacity"));
        Assert.Null(typeof(UIEngineHostOptions).GetProperty("InvocationProgressBufferCapacity"));
        Assert.DoesNotContain(
            assembly.GetExportedTypes(),
            static type => type.Name == "InvocationProgress");
        Assert.Null(typeof(ResolvedPath).GetProperty("Locations"));
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

    private static void _StartWorkWithoutRetainingNode(UIEngineHost host)
    {
        var work = (IMethodNode)Assert.Single(
            ((IObjectNode)host.ResolveRootNode("model").Value.Node).Members,
            static member => member.Name == "WorkAsync");
        var started = work.Invoke(new Dictionary<string, object?> { ["steps"] = 1 });
        Assert.Equal(InvocationStatus.RUNNING, started.Value);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (IReadableValueNode Member, WeakReference Target) _ResolveTemporaryChild(
        UIEngineHost host,
        _PathFailureModel model)
    {
        var child = new _DomainObject("temporary");
        var target = new WeakReference(child);
        model.Child = child;
        var resolved = host.ResolvePath("/model/Child");
        var member = Assert.Single(
            ((IObjectNode)resolved.Value.Node).Members,
            static candidate => candidate.Name == nameof(_DomainObject.Key));
        model.Child = null;
        return ((IReadableValueNode)member, target);
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

    private sealed class _EscapedModel
    {
        public string Value { get; } = "escaped";
    }

    private sealed class _PathFailureModel
    {
        [Expose]
        public _DomainObject? Child { get; set; }

        [Expose]
        public _DomainObject FieldChild = new("field");

        [Children]
        public object[] Items { get; } = [1];

        [Children]
        public IReadOnlyDictionary<string, _DomainObject> Duplicates { get; } =
            new _DuplicateKeyDictionary();

        [Children]
        public IEnumerable<_DomainObject> ListView { get; } =
            new List<_DomainObject> { new("list") };

        [Children]
        public IEnumerable<KeyValuePair<string, _DomainObject>> DictView { get; } =
            new Dictionary<string, _DomainObject> { ["item"] = new("dictionary") };
    }

    private sealed class _DuplicateKeyDictionary : IReadOnlyDictionary<string, _DomainObject>
    {
        private readonly KeyValuePair<string, _DomainObject>[] _Entries =
        [
            new("duplicate", new _DomainObject("first")),
            new("duplicate", new _DomainObject("second")),
        ];

        public _DomainObject this[string key] =>
            _Entries.First(entry => StringComparer.Ordinal.Equals(entry.Key, key)).Value;

        public IEnumerable<string> Keys => _Entries.Select(static entry => entry.Key);

        public IEnumerable<_DomainObject> Values => _Entries.Select(static entry => entry.Value);

        public int Count => _Entries.Length;

        public bool ContainsKey(string key) =>
            _Entries.Any(entry => StringComparer.Ordinal.Equals(entry.Key, key));

        public IEnumerator<KeyValuePair<string, _DomainObject>> GetEnumerator() =>
            ((IEnumerable<KeyValuePair<string, _DomainObject>>)_Entries).GetEnumerator();

        public bool TryGetValue(string key, out _DomainObject value)
        {
            value = _Entries
                .FirstOrDefault(entry => StringComparer.Ordinal.Equals(entry.Key, key))
                .Value;
            return value is not null;
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class _ActionModel
    {
        private int _InvocationCount;
        private readonly TaskCompletionSource _WorkCompletion = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _WorkFinished = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        private int _CompletedWorkCount;

        public Func<InvocationStatus?> ReadInvocationStatus { get; set; } = () => null;

        public int InvocationCount => _InvocationCount;

        public int CompletedWorkCount => _CompletedWorkCount;

        public Task WorkFinished => _WorkFinished.Task;

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
            Interlocked.Increment(ref _CompletedWorkCount);
            _WorkFinished.TrySetResult();

            return steps;
        }

        [Action]
        public Task WaitAsync()
        {
            _InvocationCount++;
            return new TaskCompletionSource().Task;
        }

        [Action]
        public async Task FailAsync()
        {
            _InvocationCount++;
            await Task.Yield();
            throw new InvalidOperationException("async domain failure");
        }

        [Action]
        public void Fail()
        {
            _InvocationCount++;
            throw new InvalidOperationException("domain failure");
        }
    }
}
