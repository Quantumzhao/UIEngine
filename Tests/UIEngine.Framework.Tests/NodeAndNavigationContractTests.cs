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
        Assert.False((ObjectNode)node is IFieldNode);
        Assert.False((ObjectNode)node is IProgrammaticValueNode);
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
        Assert.False((ObjectNode)node is IMemberNode);
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
            typeof(ObjectNode).GetProperties(),
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
        : ObjectNode(host, "State", typeof(_State)),
            IPropertyNode,
            IReadableValueNode,
            IWritableValueNode,
            INullableValueNode,
            IEnumNode
    {
        public Type DeclaringType => typeof(_Model);

        public IReadOnlyList<SelectionOption> Options { get; } =
            [new SelectionOption(_State.READY, nameof(_State.READY))];

        public ValueRange? Range => null;

        public Task<InteractionResult<object?>> ReadValueAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InteractionResult.Success<object?>(_State.READY));

        public Task<InteractionResult<object?>> WriteValueAsync(
            object? value,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InteractionResult.Success(value));
    }

    private sealed class _ProgrammaticStringNode(UIEngineHost host)
        : ObjectNode(host, "Name", typeof(string)),
            IProgrammaticValueNode,
            IStringNode,
            IReadableValueNode
    {
        public Task<InteractionResult<object?>> ReadValueAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(InteractionResult.Success<object?>("name"));
    }

    private sealed class _ObjectNode(UIEngineHost host)
        : ObjectNode(host, "model", typeof(_Model)), IObjectNode
    {
        public ObjectHandle Handle { get; } = new(Guid.NewGuid());

        public DomainIdentity? DomainIdentity => null;

        public string? Summary => null;

        public IReadOnlyList<ObjectNode> Members => [];
    }

    private sealed class _CollectionNode(UIEngineHost host)
        : ObjectNode(host, "Items", typeof(IReadOnlyList<string>)), ICollectionNode
    {
        public Type ElementType => typeof(string);

        public Type? KeyType => null;

        public Task<InteractionResult<CollectionSlice>> ReadEntriesAsync(
            long offset,
            int limit,
            CancellationToken cancellationToken = default) => Task.FromResult(
                InteractionResult.Success(new CollectionSlice(offset, [], 0, false)));
    }

    private sealed class _MethodNode(UIEngineHost host)
        : ObjectNode(host, "Run", typeof(void)), IMethodNode
    {
        public Type DeclaringType => typeof(_Model);

        public IReadOnlyList<MethodParameter> Parameters => [];

        public Type? ResultType => null;

        public bool IsAsynchronous => false;

        public Type? ProgressType => null;

        public bool SupportsCancellation => false;

        public Task<InteractionResult<ActionInvocation>> InvokeAsync(
            IReadOnlyDictionary<string, object?> arguments,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class _Model;

    private enum _State
    {
        READY,
    }
}
