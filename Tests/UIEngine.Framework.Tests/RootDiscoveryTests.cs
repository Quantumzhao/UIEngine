using UIEngine.Core;
using UIEngine.Core.Attributes;
using Xunit;

namespace UIEngine.Framework.Tests;

public sealed class RootDiscoveryTests
{
    [Fact]
    public async Task DiscoveryCreatesEverySupportedRootNodeKindInCanonicalOrder()
    {
        using var host = new UIEngineHost();
        var names = host.RootIds.Select(_Name).ToArray();
        var testRootNames = names.Where(static name => name is
            "catalog" or "economy" or "items" or "model" or "run" or "scalar" or "world")
            .ToArray();

        Assert.Equal(
            ["catalog", "economy", "items", "model", "run", "scalar", "world"],
            testRootNames);
        var collection = Assert.IsAssignableFrom<ICollectionNode>(_Resolve("items"));
        var scalarNode = _Resolve("scalar");
        Assert.True(scalarNode is IReadableValueNode);
        var scalar = (IReadableValueNode)scalarNode;
        var action = Assert.IsAssignableFrom<IMethodNode>(_Resolve("run"));
        Assert.IsAssignableFrom<IObjectNode>(_Resolve("model"));
        Assert.Equal(3, collection.ReadEntries(0, 10).RightValue().Entries.Count);
        Assert.Equal(7, scalar.ReadValue().RightValue().SomeValue());
        Assert.Equal(
            InvocationStatus.SUCCEEDED,
            action.Invoke(new Dictionary<string, object?> { ["value"] = 4 }).RightValue());
        Assert.Equal(5, (await action.ResultTask!).RightValue().SomeValue());
        Assert.DoesNotContain(nameof(InvalidInstanceRoot), names);
        Assert.DoesNotContain(nameof(InvalidMethodRoot), names);
    }

    [Fact]
    public void RootIdsBelongToOneHostLifetimeAndOnlyOneHostCanBeActive()
    {
        var firstHost = new UIEngineHost();
        var firstId = TestRoots.Id("model");

        Assert.Throws<InvalidOperationException>(() => new UIEngineHost());

        firstHost.Dispose();
        using var secondHost = new UIEngineHost();
        var secondId = TestRoots.Id("model");

        Assert.NotEqual(firstId, secondId);
        Assert.True(secondHost.ResolveRootNode(firstId).IsLeft);
    }

    [Fact]
    public void HostDisposalDisposesItsRegisteredWorkspaces()
    {
        var host = new UIEngineHost();
        var workspace = new UIEngineWorkspace();
        workspace.AddNavigator(TestRoots.Id("model"));

        host.Dispose();

        Assert.Empty(workspace.Navigators);
    }

    [Root]
    public object InvalidInstanceRoot { get; } = new();

    [Root]
    public static object InvalidMethodRoot() => new();

    private static string _Name(Guid id)
    {
        var path = LogicalPath.Empty.Append(new RootLogicalPathSegment(id));
        return path.ResolveNames()
            .IfLeft(static error => throw new InvalidOperationException(error.Message))[0];
    }

    private static BaseNode _Resolve(string name) => UIEngineHost.Instance
        .ResolveRootNode(TestRoots.Id(name))
        .IfLeft(static error => throw new InvalidOperationException(error.Message))
        .Node;
}
