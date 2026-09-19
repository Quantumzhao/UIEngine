using System.Text.Json;
using UIEngine.Core;
using UIEngine.Core.Attributes;
using UIEngine.Core.Reflection;
using Xunit;

namespace UIEngine.Framework.Tests;

public sealed class BindingResolutionTests
{
    [Fact]
    public void BindingReferenceIsDataOnlyAndJsonRoundTrips()
    {
        var binding = _Binding("node/1", "/root/Current");

        var json = JsonSerializer.Serialize(binding);
        var roundTrip = JsonSerializer.Deserialize<BindingReference>(json);

        Assert.Equal(binding, roundTrip);
        Assert.DoesNotContain(nameof(ObjectHandle), json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResolverRepresentsEveryBindingState()
    {
        var first = new _Node("duplicate", "First");
        var second = new _Node("duplicate", "Second");
        var root = new _Root { Current = null };
        using var host = _CreateHost(root);
        var firstHandle = host.RegisterRoot("first", first).Value;
        var secondHandle = host.RegisterRoot("second", second).Value;
        Assert.True((await host.DescribeAsync(firstHandle)).IsSuccess);
        Assert.True((await host.DescribeAsync(secondHandle)).IsSuccess);

        var resolvedNode = new _Node("node/1", "Resolved");
        root.Current = resolvedNode;
        var resolved = await host.Bindings.ResolveAsync(_Binding("node/1", "/root/Current"));
        var mismatch = await host.Bindings.ResolveAsync(
            _Binding("node/1", "/root/Current") with { ExpectedDescriptorKind = DescriptorKind.ACTION });
        var missing = await host.Bindings.ResolveAsync(
            _Binding("node/1", "/root/Current") with { MemberId = "Missing" });
        var ambiguous = await host.Bindings.ResolveAsync(new BindingReference(
            "duplicate",
            null,
            nameof(_Node.Name),
            DescriptorKind.VALUE,
            typeof(string).FullName,
            BindingFallbackPolicy.DOMAIN_IDENTITY_ONLY));
        root.Current = null;
        var unavailable = await host.Bindings.ResolveAsync(_Binding("node/1", "/root/Current"));
        var invalid = await host.Bindings.ResolveAsync(
            _Binding("node/1", "relative"));

        using var deniedHost = new UIEngineHost([new _DeniedProvider()]);
        deniedHost.RegisterRoot("root", new _DeniedRoot());
        var denied = await deniedHost.Bindings.ResolveAsync(new BindingReference(
            null,
            "/root",
            "Value",
            DescriptorKind.VALUE,
            null,
            BindingFallbackPolicy.PATH_ONLY));

        Assert.Equal(BindingResolutionState.RESOLVED, resolved.State);
        Assert.Equal(BindingResolutionState.TYPE_MISMATCH, mismatch.State);
        Assert.Equal(BindingResolutionState.TARGET_MISSING, missing.State);
        Assert.Equal(BindingResolutionState.AMBIGUOUS, ambiguous.State);
        Assert.Equal(BindingResolutionState.TEMPORARILY_UNAVAILABLE, unavailable.State);
        Assert.Equal(BindingResolutionState.INVALID_PATH, invalid.State);
        Assert.Equal(BindingResolutionState.PERMISSION_DENIED, denied.State);
    }

    [Fact]
    public async Task BindingRecoversCompatibleRootReferenceAndCollectionReplacements()
    {
        var originalRoot = new _Node("node/root", "Old root");
        using var rootHost = new UIEngineHost([new ReflectionObjectDescriptorProvider()]);
        rootHost.RegisterRoot("root", originalRoot);
        Assert.True((await rootHost.Bindings.ResolveAsync(_Binding("node/root", "/root"))).IsResolved);
        var replacementRoot = new _Node("node/root", "New root");
        rootHost.ReplaceRoot("root", replacementRoot);

        var rootResolution = await rootHost.Bindings.ResolveAsync(_Binding("node/root", "/root"));

        var originalReference = new _Node("node/reference", "Old reference");
        var originalElement = new _Node("node/element", "Old element");
        var holder = new _Root { Current = originalReference };
        holder.Items.Add(originalElement);
        using var host = _CreateHost(holder);
        Assert.True((await host.Bindings.ResolveAsync(
            _Binding("node/reference", "/root/Current"))).IsResolved);
        Assert.True((await host.Bindings.ResolveAsync(
            _Binding("node/element", "/root/Items[identity=node%2Felement]"))).IsResolved);
        holder.Current = new _Node("node/reference", "New reference");
        holder.Items[0] = new _Node("node/element", "New element");

        var referenceResolution = await host.Bindings.ResolveAsync(
            _Binding("node/reference", "/root/Current"));
        var elementResolution = await host.Bindings.ResolveAsync(
            _Binding("node/element", "/root/Items[identity=node%2Felement]"));

        Assert.Equal("New root", await _ReadNameAsync(rootResolution));
        Assert.Equal("New reference", await _ReadNameAsync(referenceResolution));
        Assert.Equal("New element", await _ReadNameAsync(elementResolution));
    }

    [Fact]
    public async Task BindingRefusesStalePathToDifferentIdentityAndSuggestsMovedPath()
    {
        var intended = new _Node("node/1", "Intended");
        var other = new _Node("node/2", "Other");
        var root = new _Root { Current = intended };
        using var host = _CreateHost(root);
        var binding = _Binding("node/1", "/root/Current");
        Assert.True((await host.Bindings.ResolveAsync(binding)).IsResolved);

        root.Alternate = intended;
        root.Current = other;
        Assert.True((await host.Paths.ResolveAsync("/root/Alternate")).IsResolved);

        var moved = await host.Bindings.ResolveAsync(binding);

        Assert.True(moved.IsResolved);
        Assert.Equal("/root/Alternate", moved.CanonicalPath?.ToString());
        Assert.Equal("Intended", await _ReadNameAsync(moved));

        root.Alternate = null;
        using var isolatedHost = _CreateHost(new _Root { Current = intended });
        Assert.True((await isolatedHost.Bindings.ResolveAsync(binding)).IsResolved);
        _ResolveRoot(isolatedHost).Current = other;

        var refused = await isolatedHost.Bindings.ResolveAsync(binding);

        Assert.Equal(BindingResolutionState.TARGET_MISSING, refused.State);
    }

    [Fact]
    public async Task KindAndTypeGuardsAreBothEnforced()
    {
        var root = new _Root { Current = new _Node("node/1", "Name") };
        using var host = _CreateHost(root);

        var kind = await host.Bindings.ResolveAsync(
            _Binding("node/1", "/root/Current") with { ExpectedDescriptorKind = DescriptorKind.REFERENCE });
        var type = await host.Bindings.ResolveAsync(
            _Binding("node/1", "/root/Current") with { ExpectedTypeName = typeof(int).FullName });

        Assert.Equal(BindingResolutionState.TYPE_MISMATCH, kind.State);
        Assert.Equal(BindingResolutionState.TYPE_MISMATCH, type.State);
    }

    private static BindingReference _Binding(string identity, string path) => new(
        identity,
        path,
        nameof(_Node.Name),
        DescriptorKind.VALUE,
        typeof(string).FullName,
        BindingFallbackPolicy.DOMAIN_IDENTITY_THEN_PATH);

    private static UIEngineHost _CreateHost(_Root root)
    {
        var host = new UIEngineHost([new ReflectionObjectDescriptorProvider()]);
        host.RegisterRoot("root", root);
        return host;
    }

    private static _Root _ResolveRoot(UIEngineHost host)
    {
        var root = Assert.Single(host.Roots);
        Assert.True(host.TryResolve(root.Handle, out var instance));
        return Assert.IsType<_Root>(instance);
    }

    private static async Task<string?> _ReadNameAsync(BindingResolution resolution)
    {
        Assert.True(resolution.IsResolved);
        var value = Assert.IsAssignableFrom<IValueDescriptor>(resolution.Target?.MemberDescriptor);
        return Assert.IsType<string>((await value.ReadAsync()).Value);
    }

    private sealed class _Root
    {
        [Expose]
        public _Node? Current { get; set; }

        [Expose]
        public _Node? Alternate { get; set; }

        [Children]
        public List<_Node> Items { get; } = [];
    }

    private sealed class _Node(string identity, string name) : IStableDomainIdentity
    {
        public string? DomainIdentity { get; } = identity;

        [Expose]
        public string Name { get; } = name;
    }

    private sealed class _DeniedRoot;

    private sealed class _DeniedProvider : IObjectDescriptorProvider
    {
        public bool CanDescribe(Type objectType) => objectType == typeof(_DeniedRoot);

        public ValueTask<InteractionResult<IObjectDescriptor>> DescribeAsync(
            UIEngineHost host,
            object instance,
            ObjectHandle handle,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InteractionResult.Failure<IObjectDescriptor>(
                InteractionErrorCode.PERMISSION_DENIED,
                "Descriptor access was denied."));
    }
}
