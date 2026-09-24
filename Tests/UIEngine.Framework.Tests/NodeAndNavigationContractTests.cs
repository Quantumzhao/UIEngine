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
        var path = _Path(_Member("model"), _Member("State"));
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
        var path = _Path(_Member("missing"));
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
            _Path(_Member("model"), _Member("Name")),
            new _ProgrammaticStringNode(host));
        var currentEntry = new NavigationEntry(
            _Path(_Member("model")),
            new _ObjectNode(host));
        var change = new NavigationPoppedChange(navigatorId, oldEntry, currentEntry);
        var notification = new WorkspaceChangedEventArgs(change);

        Assert.Same(change, notification.Change);
        Assert.Same(oldEntry, change.RemovedEntry);
        Assert.Same(currentEntry, change.CurrentEntry);
    }

    [Fact]
    public void LogicalPathsExposeLinkedSemanticParents()
    {
        var world = _Path(_Member("world"));
        var name = world.Append("Name");
        var nations = world.Append("Nations");
        var selectedNation = nations.Append(new ListLogicalPathSegment(0));
        var selectedName = selectedNation.Append("Name");

        Assert.Null(world.Parent);
        Assert.Same(world, name.Parent);
        Assert.Same(world, nations.Parent);
        Assert.Same(nations, selectedNation.Parent);
        Assert.Same(selectedNation, selectedName.Parent);
    }

    [Fact]
    public void LogicalPathSegmentsRepresentMemberListAndDictionarySemantics()
    {
        var memberPath = _Path(_Member("world"), _Member("Nations"));
        var listPath = memberPath.Append(new ListLogicalPathSegment(2));
        var dictionaryPath = _Path(
            _Member("catalog"),
            _Member("Items"),
            new DictLogicalPathSegment("SKU/42"));

        Assert.Equal("Nations", Assert.IsType<MemberLogicalPathSegment>(
            memberPath.Segments[^1]).Name);
        var list = Assert.IsType<ListLogicalPathSegment>(listPath.Segments[^1]);
        Assert.Equal(2, list.Index);
        var dictionary = Assert.IsType<DictLogicalPathSegment>(dictionaryPath.Segments[^1]);
        Assert.Equal("SKU/42", dictionary.Key);
        Assert.Equal("/catalog/Items[key=SKU/42]", dictionaryPath.ToString());

        var constructed = LogicalPath.Root
            .Append("world")
            .Append("Nations")
            .Append(new ListLogicalPathSegment(2));
        Assert.Equal(listPath, constructed);
        Assert.Throws<ArgumentException>(() => LogicalPath.Root.Append(
            new ListLogicalPathSegment(0)));
    }

    private static LogicalPath _Path(params ILogicalPathSegment[] segments)
    {
        var path = LogicalPath.Root;
        foreach (var segment in segments)
        {
            path = path.Append(segment);
        }

        return path;
    }

    private static MemberLogicalPathSegment _Member(string name) => new(name);

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

        public Either<InteractionError, Option<object>> ReadValue() =>
            Right(Some<object>(_State.READY));

        public Either<InteractionError, Option<object>> WriteValue(object? value) =>
            Right(Optional(value));
    }

    private sealed class _ProgrammaticStringNode(UIEngineHost host)
        : BaseNode(host, "Name", typeof(string)),
            IProgrammaticValueNode,
            IStringNode,
            IReadableValueNode
    {
        public Either<InteractionError, Option<object>> ReadValue() =>
            Right(Some<object>("name"));
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

        public Either<InteractionError, CollectionSlice> ReadEntries(
            long offset,
            int limit) => Right(new CollectionSlice(offset, [], 0, false));
    }

    private sealed class _MethodNode(UIEngineHost host)
        : BaseNode(host, "Run", typeof(void)), IMethodNode
    {
        public Type DeclaringType => typeof(_Model);

        public IReadOnlyList<MethodParameter> Parameters => [];

        public Type? ResultType => null;

        public bool IsAsynchronous => false;

        public InvocationStatus? Status => null;

        public Task<Either<InteractionError, Option<object>>>? ResultTask => null;

        public Either<InteractionError, InvocationStatus> Invoke(
            IReadOnlyDictionary<string, object?> arguments) => throw new NotSupportedException();
    }

    private sealed class _Model;

    private enum _State
    {
        READY,
    }
}
