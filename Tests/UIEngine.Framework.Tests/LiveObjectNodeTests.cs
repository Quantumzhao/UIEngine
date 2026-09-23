using UIEngine.Core;
using UIEngine.Core.Attributes;
using Xunit;

namespace UIEngine.Framework.Tests;

public sealed class LiveObjectNodeTests
{
    [Fact]
    public async Task RootResolutionCreatesFreshCompleteNodeOccurrences()
    {
        var model = new _Model();
        using var host = new UIEngineHost(new UIEngineHostOptions { MaxCollectionItems = 2 });
        var handle = host.SetRoot("model", model).Value;

        var first = host.ResolveRootNode("model");
        var second = host.ResolveRootNode("model");

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal("/model", first.Value.CanonicalPath.ToString());
        Assert.NotSame(first.Value.Node, second.Value.Node);
        var firstObject = Assert.IsAssignableFrom<IObjectNode>(first.Value.Node);
        var secondObject = Assert.IsAssignableFrom<IObjectNode>(second.Value.Node);
        Assert.Equal(handle, firstObject.Handle);
        Assert.Equal("model summary", firstObject.Summary);
        Assert.Equal(typeof(_Model), first.Value.Node.ValueType);
        Assert.DoesNotContain(firstObject.Members, static node => node.Id == nameof(_Model.Hidden));
        Assert.Equal(
            firstObject.Members.Select(static node => node.Id),
            secondObject.Members.Select(static node => node.Id));
        Assert.All(firstObject.Members.Zip(secondObject.Members),
            static pair => Assert.NotSame(pair.First, pair.Second));

        var members = firstObject.Members.ToDictionary(static node => node.Id);
        var child = members[nameof(_Model.Child)];
        Assert.True(child is IReferenceNode);
        Assert.True(child is IPropertyNode);
        Assert.False(child.IsTerminal);
        Assert.Null(((IReferenceNode)child).ReadReference().Value);

        model.Child = new _Child("one");
        var childRead = ((IReferenceNode)child).ReadReference();
        Assert.True(childRead.IsSuccess);
        Assert.NotNull(childRead.Value);

        var items = members[nameof(_Model.Items)];
        Assert.True(items is ICollectionNode);
        Assert.True(items is IPropertyNode);
        var slice = ((ICollectionNode)items).ReadEntries(0, 2);
        var tooLarge = ((ICollectionNode)items).ReadEntries(0, 3);
        Assert.IsType<NullCollectionEntry>(slice.Value.Entries[0]);
        Assert.Equal(7, Assert.IsType<ScalarCollectionEntry>(slice.Value.Entries[1]).Value);
        Assert.True(slice.Value.HasMore);
        Assert.Equal(InteractionErrorCode.INVALID_INPUT, tooLarge.Error?.Code);

        var add = members[nameof(_Model.Add)];
        var method = Assert.IsAssignableFrom<IMethodNode>(add);
        Assert.True(add.IsTerminal);
        Assert.False(add is IPropertyNode or IFieldNode);
        Assert.Equal(typeof(_Model), method.DeclaringType);
        Assert.Equal(typeof(int), method.ResultType);
        Assert.False(method.IsAsynchronous);
        Assert.Equal(2, method.Parameters.Count);
        Assert.True(method.Parameters[1].HasDefaultValue);
        Assert.Equal(1, method.Parameters[1].DefaultValue);

        var invocation = method.Invoke(new Dictionary<string, object?>
        {
            ["left"] = "2",
        });
        Assert.Equal(3, (await invocation.Value.Completion).Value);
        Assert.Equal(1, model.InvocationCount);
    }

    [Fact]
    public void RootResolutionReturnsStructuredFailures()
    {
        using var host = new UIEngineHost();

        var invalid = host.ResolveRootNode(" ");
        var missing = host.ResolveRootNode("missing");

        Assert.Equal(InteractionErrorCode.INVALID_INPUT, invalid.Error?.Code);
        Assert.Equal(InteractionErrorCode.NOT_FOUND, missing.Error?.Code);
    }

    private sealed class _Model
    {
        private int _InvocationCount;
        private string _Name = "initial";

        [Expose]
        public string Name
        {
            get => _Name;
            set => _Name = value;
        }

        [Expose]
        public _Child? Child { get; set; }

        [Children]
        public object?[] Items { get; } = [null, 7, new _Child("entry")];

        [Summary]
        public string Summary => Name == "initial" ? "model summary" : $"{Name} summary";

        [Action]
        public int Add(int left, int right = 1)
        {
            _InvocationCount++;
            return left + right;
        }

        public string Hidden { get; } = "hidden";

        public int InvocationCount => _InvocationCount;
    }

    private sealed class _Child(string identity)
    {
        [DomainIdentitySource]
        public string DomainIdentity => $"child/{identity}";
    }
}
