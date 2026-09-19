using UIEngine.Core;
using UIEngine.Core.Attributes;
using UIEngine.Core.Reflection;
using Xunit;

namespace UIEngine.Framework.Tests;

public sealed class LogicalPathTests
{
    [Fact]
    public void ParserCanonicalizesSelectorsAndEscapedDelimiters()
    {
        var parsed = LogicalPath.Parse(
            "/Wo%2Frld/Nations[index=001]/Cities[key=Mos%2F%5Bcow%5D]/Mayor[identity=Person%2F1]");

        Assert.True(parsed.IsSuccess);
        Assert.Equal("Wo/rld", parsed.Value.Segments[0].Identifier);
        Assert.Equal(CollectionSelectorKind.INDEX, parsed.Value.Segments[1].Selector?.Kind);
        Assert.Equal("1", parsed.Value.Segments[1].Selector?.Value);
        Assert.Equal("Mos/[cow]", parsed.Value.Segments[2].Selector?.Value);
        Assert.Equal(
            "/Wo%2Frld/Nations[index=1]/Cities[key=Mos%2F%5Bcow%5D]/Mayor[identity=Person%2F1]",
            parsed.Value.ToString());
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative")]
    [InlineData("//root")]
    [InlineData("/root/")]
    [InlineData("/root[index=0]")]
    [InlineData("/root/Items[unknown=x]")]
    [InlineData("/root/Items[index=-1]")]
    [InlineData("/root/Items[key=]")]
    [InlineData("/root/Bad%2")]
    [InlineData("/root/Bad%FF")]
    [InlineData("/root/Bad=value")]
    public void ParserRejectsMalformedPaths(string path)
    {
        var result = LogicalPath.Parse(path);

        Assert.Equal(InteractionErrorCode.INVALID_PATH, result.Error?.Code);
    }

    [Fact]
    public void ParseFormatRoundTripsDeterministicReservedInputs()
    {
        var identifiers = new[] { "simple", "A/B", "bracket[value]", "100%", "城市", "caseSensitive" };

        foreach (var identifier in identifiers)
        {
            var path = LogicalPath.Root
                .Append(identifier)
                .Append(identifier, new CollectionSelector(CollectionSelectorKind.KEY, identifier));
            var reparsed = LogicalPath.Parse(path.ToString());

            Assert.True(reparsed.IsSuccess);
            Assert.Equal(path, reparsed.Value);
            Assert.Equal(path.ToString(), reparsed.Value.ToString());
        }
    }

    [Fact]
    public async Task ResolverTraversesReferencesAndAllSupportedCollectionSelectors()
    {
        var first = new _Node("node/1", "First");
        var second = new _Node("node/2", "Second");
        first.Next = second;
        var root = new _Root(first, second);
        using var host = _CreateHost("wo/rld", root);

        var byIndex = await host.Paths.ResolveAsync("/wo%2Frld/Items[index=0]/Next/Name");
        var legacyIndex = await host.Paths.ResolveAsync("/wo%2Frld/Items/0");
        var byIdentity = await host.Paths.ResolveAsync("/wo%2Frld/Items[identity=node%2F2]");
        var byKey = await host.Paths.ResolveAsync("/wo%2Frld/ByKey[key=primary%2F%5Bnode%5D]");
        var collection = await host.Paths.ResolveAsync("/wo%2Frld/Items");

        Assert.Equal(DescriptorKind.VALUE, byIndex.Target?.Kind);
        Assert.Equal(nameof(_Node.Name), byIndex.Target?.MemberDescriptor?.Id);
        Assert.Equal("/wo%2Frld/Items[index=0]/Next/Name", byIndex.CanonicalPath?.ToString());
        Assert.Equal("/wo%2Frld/Items[index=0]", legacyIndex.CanonicalPath?.ToString());
        Assert.Equal(second.DomainIdentity, byIdentity.Target?.OwnerDescriptor.DomainIdentity?.Value);
        Assert.Equal(first.DomainIdentity, byKey.Target?.OwnerDescriptor.DomainIdentity?.Value);
        Assert.Equal(DescriptorKind.COLLECTION, collection.Target?.Kind);
    }

    [Fact]
    public async Task ResolverKeepsCaseSensitivityAndReturnsStructuredFailures()
    {
        var node = new _Node("node/1", "First");
        var root = new _Root(node);
        using var host = _CreateHost("root", root);

        var wrongRootCase = await host.Paths.ResolveAsync("/Root");
        var wrongMemberCase = await host.Paths.ResolveAsync("/root/items[index=0]");
        var unavailable = await host.Paths.ResolveAsync("/root/Current");
        var badTraversal = await host.Paths.ResolveAsync("/root/Title/Next");

        Assert.Equal(BindingResolutionState.TARGET_MISSING, wrongRootCase.State);
        Assert.Equal(BindingResolutionState.TARGET_MISSING, wrongMemberCase.State);
        Assert.Equal(BindingResolutionState.TEMPORARILY_UNAVAILABLE, unavailable.State);
        Assert.Equal(BindingResolutionState.INVALID_PATH, badTraversal.State);
    }

    private static UIEngineHost _CreateHost(string identifier, _Root root)
    {
        var host = new UIEngineHost([new ReflectionObjectDescriptorProvider()]);
        Assert.True(host.RegisterRoot(identifier, root).IsSuccess);
        return host;
    }

    private sealed class _Root
    {
        public _Root(params _Node[] items)
        {
            Items.AddRange(items);
            if (items.Length > 0)
            {
                ByKey.Add("primary/[node]", items[0]);
            }
        }

        [Expose]
        public string Title { get; } = "Root";

        [Expose]
        public _Node? Current { get; set; }

        [Children]
        public List<_Node> Items { get; } = [];

        [Children]
        public Dictionary<string, _Node> ByKey { get; } = [];
    }

    private sealed class _Node(string identity, string name) : IStableDomainIdentity
    {
        public string? DomainIdentity { get; } = identity;

        [Expose]
        public string Name { get; } = name;

        [Expose]
        public _Node? Next { get; set; }
    }
}
