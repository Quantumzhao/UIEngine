using UIEngine.Core;
using Xunit;

namespace UIEngine.Framework.Tests;

public sealed class NodeAndNavigationContractTests
{
    [Fact]
    public void ReflectedEnumPropertyCombinesOverlappingFacets()
    {
        using var host = new UIEngineHost();
        var node = new _EnumPropertyNode(host);

        Assert.IsAssignableFrom<IPropertyNode>(node);
        Assert.IsAssignableFrom<IReadableValueNode>(node);
        Assert.IsAssignableFrom<IWritableValueNode>(node);
        Assert.IsAssignableFrom<INullableValueNode>(node);
        Assert.IsAssignableFrom<IEnumNode>(node);
        Assert.False((BaseNode)node is IFieldNode);
        Assert.False((BaseNode)node is IProgrammaticValueNode);
        Assert.True(node.IsTerminal);
    }

    [Fact]
    public void ProgrammaticScalarDoesNotClaimReflectedMembership()
    {
        using var host = new UIEngineHost();
        var node = new _ProgrammaticStringNode(host);

        Assert.IsAssignableFrom<IProgrammaticValueNode>(node);
        Assert.IsAssignableFrom<IStringNode>(node);
        Assert.IsAssignableFrom<IReadableValueNode>(node);
        Assert.False((BaseNode)node is IMemberNode);
        Assert.True(node.IsTerminal);
    }

    [Fact]
    public void ScalarAndMethodNodesAreTerminalWhileObjectAndCollectionNodesAreNavigable()
    {
        using var host = new UIEngineHost();

        Assert.True(new _ProgrammaticStringNode(host).IsTerminal);
        Assert.True(new _MethodNode(host).IsTerminal);
        Assert.False(new _ObjectNode(host).IsTerminal);
        Assert.False(new _CollectionNode(host).IsTerminal);
    }

    [Fact]
    public void ResolvedOccurrencesKeepPathsOffNodesAndRemainFresh()
    {
        using var host = new UIEngineHost();
        var path = LogicalPath.Parse("/model/State").Value;
        var first = new ResolvedNode(path, new _EnumPropertyNode(host));
        var second = new ResolvedNode(path, new _EnumPropertyNode(host));

        Assert.Equal(first.CanonicalPath, second.CanonicalPath);
        Assert.NotSame(first.Node, second.Node);
        Assert.Same(host, first.Host);
        Assert.DoesNotContain(
            typeof(BaseNode).GetProperties(),
            static property => property.PropertyType == typeof(LogicalPath));
    }

    [Fact]
    public void BrokenEntryCarriesFailureInsteadOfFakeNode()
    {
        var path = LogicalPath.Parse("/missing").Value;
        var error = new InteractionError(InteractionErrorCode.NOT_FOUND, "Missing node.");
        var entry = new NavigationEntry(path, error);

        Assert.False(entry.IsResolved);
        Assert.Null(entry.Node);
        Assert.Same(error, entry.Error);
        Assert.Equal(path, entry.Path);
    }

    [Fact]
    public void ChangesIdentifyExactEntriesForFrontendRetirement()
    {
        using var host = new UIEngineHost();
        var navigatorId = Guid.NewGuid();
        var oldEntry = new NavigationEntry(
            LogicalPath.Parse("/model/Name").Value,
            new _ProgrammaticStringNode(host));
        var currentEntry = new NavigationEntry(
            LogicalPath.Parse("/model").Value,
            new _ObjectNode(host));
        var change = new NavigationPoppedChange(navigatorId, oldEntry, currentEntry);
        var notification = new WorkspaceChangedEventArgs(change);

        Assert.Same(change, notification.Change);
        Assert.Same(oldEntry, change.RemovedEntry);
        Assert.Same(currentEntry, change.CurrentEntry);
    }

    private sealed class _EnumPropertyNode(UIEngineHost host)
        : BaseNode(host, "State", typeof(_State)),
            IPropertyNode,
            IReadableValueNode,
            IWritableValueNode,
            INullableValueNode,
            IEnumNode
    {
        public Type DeclaringType => typeof(_Model);

        public IReadOnlyList<SelectionOption> Options { get; } =
            [new SelectionOption(_State.READY, nameof(_State.READY))];

        public IValueRange? Range => null;

        public InteractionResult<object?> ReadValue() =>
            InteractionResult.Success<object?>(_State.READY);

        public InteractionResult<object?> WriteValue(object? value) =>
            InteractionResult.Success(value);
    }

    private sealed class _ProgrammaticStringNode(UIEngineHost host)
        : BaseNode(host, "Name", typeof(string)),
            IProgrammaticValueNode,
            IStringNode,
            IReadableValueNode
    {
        public InteractionResult<object?> ReadValue() =>
            InteractionResult.Success<object?>("name");
    }

    private sealed class _ObjectNode(UIEngineHost host)
        : BaseNode(host, "model", typeof(_Model)), IObjectNode
    {
        public Guid Handle { get; } = Guid.NewGuid();

        public string? Summary => null;

        public IReadOnlyList<BaseNode> Members => [];
    }

    private sealed class _CollectionNode(UIEngineHost host)
        : BaseNode(host, "Items", typeof(IReadOnlyList<string>)), ICollectionNode
    {
        public Type ElementType => typeof(string);

        public Type? KeyType => null;

        public InteractionResult<CollectionSlice> ReadEntries(
            long offset,
            int limit) => InteractionResult.Success(new CollectionSlice(offset, [], 0, false));
    }

    private sealed class _MethodNode(UIEngineHost host)
        : BaseNode(host, "Run", typeof(void)), IMethodNode
    {
        public Type DeclaringType => typeof(_Model);

        public IReadOnlyList<MethodParameter> Parameters => [];

        public Type? ResultType => null;

        public bool IsAsynchronous => false;

        public InvocationStatus? Status => null;

        public InteractionResult<ActionInvocation> Invoke(
            IReadOnlyDictionary<string, object?> arguments) => throw new NotSupportedException();
    }

    private sealed class _Model;

    private enum _State
    {
        READY,
    }
}
