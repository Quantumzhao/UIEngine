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
        var handle = host.SetRoot("model", model).RightValue();

        var first = host.ResolveRootNode("model");
        var second = host.ResolveRootNode("model");

        Assert.True(first.IsRight);
        Assert.True(second.IsRight);
        Assert.Equal("/model", first.RightValue().CanonicalPath.ToString());
        Assert.NotSame(first.RightValue().Node, second.RightValue().Node);
        var firstObject = Assert.IsAssignableFrom<IObjectNode>(first.RightValue().Node);
        var secondObject = Assert.IsAssignableFrom<IObjectNode>(second.RightValue().Node);
        Assert.Equal(handle, firstObject.Handle);
        Assert.Equal("model summary", firstObject.Summary);
        Assert.Equal(typeof(_Model), first.RightValue().Node.ValueType);
        Assert.DoesNotContain(firstObject.Members, static node => node.Name == nameof(_Model.Hidden));
        Assert.Equal(
            firstObject.Members.Select(static node => node.Name),
            secondObject.Members.Select(static node => node.Name));
        Assert.All(firstObject.Members.Zip(secondObject.Members),
            static pair => Assert.NotSame(pair.Item1, pair.Item2));

        var members = firstObject.Members.ToDictionary(static node => node.Name);
        var child = members[nameof(_Model.Child)];
        Assert.True(child is IReferenceNode);
        Assert.True(child is IPropertyNode);
        Assert.False(child.IsTerminal);
        Assert.True(((IReferenceNode)child).ReadReference().RightValue().IsNone);

        model.Child = new _Child("one");
        var childRead = ((IReferenceNode)child).ReadReference();
        Assert.True(childRead.IsRight);
        Assert.True(childRead.RightValue().IsSome);

        var items = members[nameof(_Model.Items)];
        Assert.True(items is ICollectionNode);
        Assert.True(items is IPropertyNode);
        var slice = ((ICollectionNode)items).ReadEntries(0, 2);
        var tooLarge = ((ICollectionNode)items).ReadEntries(0, 3);
        Assert.IsType<NullCollectionEntry>(slice.RightValue().Entries[0]);
        Assert.Equal(7, Assert.IsType<ScalarCollectionEntry>(slice.RightValue().Entries[1]).Value);
        Assert.True(slice.RightValue().HasMore);
        Assert.Equal(InteractionErrorCode.INVALID_INPUT, tooLarge.LeftValue().Code);

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
        Assert.Equal(InvocationStatus.SUCCEEDED, invocation.RightValue());
        Assert.Equal(3, (await method.ResultTask!).RightValue());
        Assert.Equal(1, model.InvocationCount);
    }

    [Fact]
    public void RootResolutionReturnsStructuredFailures()
    {
        using var host = new UIEngineHost();

        var missing = host.ResolveRootNode("missing");

        Assert.Equal(InteractionErrorCode.NOT_FOUND, missing.LeftValue().Code);
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

    private sealed class _Child(string name)
    {
        [Summary]
        public string Summary => name;
    }
}
