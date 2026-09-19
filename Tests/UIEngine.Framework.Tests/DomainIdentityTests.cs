using System.Collections;
using System.Runtime.CompilerServices;
using UIEngine.Core;
using UIEngine.Core.Attributes;
using UIEngine.Core.Exposure;
using UIEngine.Core.Reflection;
using Xunit;

namespace UIEngine.Framework.Tests;

public sealed class DomainIdentityTests
{
    [Fact]
    public async Task InterfaceAndAttributeSourcesAreNormalizedAndIndexed()
    {
        using var host = _CreateReflectionHost();
        var interfaceHandle = host.RegisterRoot("interface", new _InterfaceIdentity("interface/1")).Value;
        var attributeHandle = host.RegisterRoot("attribute", new _AttributeIdentity("attribute/1")).Value;

        var interfaceDescriptor = (await host.DescribeAsync(interfaceHandle)).Value;
        var attributeDescriptor = (await host.DescribeAsync(attributeHandle)).Value;

        Assert.Equal(new DomainIdentity("interface/1"), interfaceDescriptor.DomainIdentity);
        Assert.Equal(new DomainIdentity("attribute/1"), attributeDescriptor.DomainIdentity);
        Assert.Equal(interfaceHandle, host.ResolveDomainIdentity(new DomainIdentity("interface/1")).Value);
        Assert.Equal(attributeHandle, host.ResolveDomainIdentity(new DomainIdentity("attribute/1")).Value);
    }

    [Fact]
    public async Task ProgrammaticCallbackOverridesConfiguredInterfaceAndAttributeSources()
    {
        var callbackCount = 0;
        var registry = new ExposureRegistry();
        registry.For<_ConflictingIdentity>()
            .Identity(model =>
            {
                callbackCount++;
                return $"callback/{model.Key}";
            });
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            Exposure = registry,
            DescriptorProviders = [new ReflectionObjectDescriptorProvider()],
            DomainIdentityProviders = [new _ConstantIdentityProvider("provider")],
        });
        var handle = host.RegisterRoot("model", new _ConflictingIdentity("1")).Value;

        var first = (await host.DescribeAsync(handle)).Value;
        var second = (await host.DescribeAsync(handle)).Value;

        Assert.Equal(new DomainIdentity("callback/1"), first.DomainIdentity);
        Assert.Equal(first.DomainIdentity, second.DomainIdentity);
        Assert.Equal(nameof(_ConflictingIdentity.Key), Assert.Single(first.Values).Id);
        Assert.Equal(1, callbackCount);
    }

    [Fact]
    public async Task ConfiguredProviderOverridesBuiltInSources()
    {
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            DescriptorProviders = [new ReflectionObjectDescriptorProvider()],
            DomainIdentityProviders = [new _ConstantIdentityProvider("provider/1")],
        });
        var handle = host.RegisterRoot("model", new _ConflictingIdentity("1")).Value;

        var descriptor = (await host.DescribeAsync(handle)).Value;

        Assert.Equal(new DomainIdentity("provider/1"), descriptor.DomainIdentity);
    }

    [Fact]
    public async Task ConflictingAndInvalidBuiltInSourcesFailStructurally()
    {
        using var host = _CreateReflectionHost();
        var conflicting = host.RegisterRoot("conflicting", new _ConflictingIdentity("1")).Value;
        var invalid = host.RegisterRoot("invalid", new _InterfaceIdentity(" ")).Value;
        var invalidMember = host.RegisterRoot("invalid-member", new _InvalidAttributeIdentity()).Value;

        var conflictingResult = await host.DescribeAsync(conflicting);
        var invalidResult = await host.DescribeAsync(invalid);
        var invalidMemberResult = await host.DescribeAsync(invalidMember);

        Assert.Equal(InteractionErrorCode.CONFLICTING_DOMAIN_IDENTITY, conflictingResult.Error?.Code);
        Assert.Equal(InteractionErrorCode.INVALID_DOMAIN_IDENTITY, invalidResult.Error?.Code);
        Assert.Equal(InteractionErrorCode.INVALID_DOMAIN_IDENTITY, invalidMemberResult.Error?.Code);
    }

    [Fact]
    public async Task DuplicateLiveDomainIdentitiesResolveAsAmbiguous()
    {
        using var host = _CreateReflectionHost();
        var first = host.RegisterRoot("first", new _InterfaceIdentity("shared")).Value;
        var second = host.RegisterRoot("second", new _InterfaceIdentity("shared")).Value;

        Assert.True((await host.DescribeAsync(first)).IsSuccess);
        Assert.True((await host.DescribeAsync(second)).IsSuccess);

        var resolved = host.ResolveDomainIdentity(new DomainIdentity("shared"));

        Assert.Equal(InteractionErrorCode.AMBIGUOUS_DOMAIN_IDENTITY, resolved.Error?.Code);
        Assert.NotEqual(first.Identity, second.Identity);
    }

    [Fact]
    public async Task IdentityIndexDoesNotWalkUnrequestedCollections()
    {
        var child = new _InterfaceIdentity("child/1");
        var children = new _TrackingEnumerable(child);
        using var host = _CreateReflectionHost();
        var root = host.RegisterRoot("root", new _IdentityRoot(children)).Value;

        var rootDescriptor = (await host.DescribeAsync(root)).Value;

        Assert.Equal(0, children.EnumerationCount);
        Assert.Equal(
            InteractionErrorCode.DOMAIN_IDENTITY_NOT_FOUND,
            host.ResolveDomainIdentity(new DomainIdentity("child/1")).Error?.Code);

        var childHandle = Assert.Single((await Assert.Single(rootDescriptor.Collections).SnapshotAsync()).Value);

        Assert.Equal(1, children.EnumerationCount);
        Assert.Equal(childHandle, host.ResolveDomainIdentity(new DomainIdentity("child/1")).Value);
    }

    [Fact]
    public async Task RootReplacementUsesNewRuntimeIdentityAndStableDomainIdentity()
    {
        using var host = _CreateReflectionHost();
        var original = new _InterfaceIdentity("root/1");
        var originalHandle = host.RegisterRoot("root", original).Value;
        var originalDescriptor = (await host.DescribeAsync(originalHandle)).Value;
        var replacement = new _InterfaceIdentity("root/1");

        var replacementHandle = host.ReplaceRoot("root", replacement).Value;
        var replacementDescriptor = (await host.DescribeAsync(replacementHandle)).Value;

        Assert.NotEqual(originalHandle.Identity, replacementHandle.Identity);
        Assert.Equal(originalDescriptor.DomainIdentity, replacementDescriptor.DomainIdentity);
        Assert.Equal(replacementHandle, Assert.Single(host.Roots).Handle);
        Assert.Same(replacement, _Resolve(host, replacementHandle));
    }

    [Fact]
    public void ReplacementAndUnregistrationReleaseRootReferences()
    {
        using var host = _CreateReflectionHost();

        var replacedReference = _RegisterAndReplace(host);
        var unregisteredReference = _RegisterAndUnregister(host);

        _Collect();

        Assert.False(replacedReference.IsAlive);
        Assert.False(unregisteredReference.IsAlive);
        Assert.Single(host.Roots);
    }

    [Fact]
    public void MissingRootReplacementAndUnregistrationReturnStructuredFailures()
    {
        using var host = new UIEngineHost();

        var replacement = host.ReplaceRoot("missing", new object());
        var unregistration = host.UnregisterRoot("missing");

        Assert.Equal(InteractionErrorCode.ROOT_NOT_FOUND, replacement.Error?.Code);
        Assert.Equal(InteractionErrorCode.ROOT_NOT_FOUND, unregistration.Error?.Code);
    }

    private static UIEngineHost _CreateReflectionHost() =>
        new([new ReflectionObjectDescriptorProvider()]);

    private static object? _Resolve(UIEngineHost host, ObjectHandle handle)
    {
        host.TryResolve(handle, out var instance);
        return instance;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference _RegisterAndReplace(UIEngineHost host)
    {
        var original = new _InterfaceIdentity("replaced");
        var reference = new WeakReference(original);
        host.RegisterRoot("replace", original);
        host.ReplaceRoot("replace", new _InterfaceIdentity("replacement"));
        return reference;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference _RegisterAndUnregister(UIEngineHost host)
    {
        var original = new _InterfaceIdentity("unregistered");
        var reference = new WeakReference(original);
        host.RegisterRoot("unregister", original);
        host.UnregisterRoot("unregister");
        return reference;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void _Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private sealed class _InterfaceIdentity(string identity) : IStableDomainIdentity
    {
        public string? DomainIdentity { get; } = identity;
    }

    private sealed class _AttributeIdentity(string identity)
    {
        [DomainIdentitySource]
        public string Identity { get; } = identity;
    }

    private sealed class _ConflictingIdentity(string key) : IStableDomainIdentity
    {
        [Expose]
        public string Key { get; } = key;

        public string? DomainIdentity => $"interface/{Key}";

        [DomainIdentitySource]
        public string Identity => $"attribute/{Key}";
    }

    private sealed class _InvalidAttributeIdentity
    {
        public _InvalidAttributeIdentity()
        {
            Identity = 1;
        }

        [DomainIdentitySource]
        public int Identity { get; }
    }

    private sealed class _IdentityRoot(IEnumerable<_InterfaceIdentity> children)
    {
        [Children]
        public IEnumerable<_InterfaceIdentity> Children { get; } = children;
    }

    private sealed class _TrackingEnumerable(params _InterfaceIdentity[] items) : IEnumerable<_InterfaceIdentity>
    {
        public int EnumerationCount { get; private set; }

        public IEnumerator<_InterfaceIdentity> GetEnumerator()
        {
            EnumerationCount++;
            return ((IEnumerable<_InterfaceIdentity>)items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class _ConstantIdentityProvider(string identity) : IDomainIdentityProvider
    {
        public bool CanProvideIdentity(Type objectType) => true;

        public ValueTask<InteractionResult<string?>> GetIdentityAsync(
            object instance,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InteractionResult.Success<string?>(identity));
    }
}
