using UIEngine.Core;
using Xunit;

namespace UIEngine.Framework.Tests;

public sealed class UIEngineHostTests
{
    [Fact]
    public void RegisterRootReturnsStructuredDuplicateIdentifierFailure()
    {
        var host = new UIEngineHost();

        var first = host.RegisterRoot("world", new _TestRoot());
        var duplicate = host.RegisterRoot("world", new _TestRoot());

        Assert.True(first.IsSuccess);
        Assert.False(duplicate.IsSuccess);
        Assert.Equal(InteractionErrorCode.DUPLICATE_ROOT_IDENTIFIER, duplicate.Error?.Code);
        Assert.Throws<InvalidOperationException>(() => duplicate.Value);
        Assert.Single(host.Roots);
    }

    [Fact]
    public void RegisterRootTreatsIdentifiersAsCaseSensitive()
    {
        var host = new UIEngineHost();

        var upperCase = host.RegisterRoot("World", new _TestRoot());
        var lowerCase = host.RegisterRoot("world", new _TestRoot());

        Assert.True(upperCase.IsSuccess);
        Assert.True(lowerCase.IsSuccess);
        Assert.Equal(2, host.Roots.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void RegisterRootRejectsInvalidIdentifiers(string identifier)
    {
        var result = new UIEngineHost().RegisterRoot(identifier, new _TestRoot());

        Assert.False(result.IsSuccess);
        Assert.Equal(InteractionErrorCode.INVALID_ROOT_IDENTIFIER, result.Error?.Code);
    }

    [Fact]
    public void RepeatedReferencesShareIdentityWithinAHost()
    {
        var host = new UIEngineHost();
        var instance = new _TestRoot();

        var first = host.GetOrCreateHandle(instance);
        var second = host.GetOrCreateHandle(instance);
        var registered = host.RegisterRoot("root", instance);

        Assert.Equal(first, second);
        Assert.Equal(first, registered.Value);
        Assert.NotEqual(Guid.Empty, first.Identity.RuntimeId);
    }

    [Fact]
    public void SeparateHostsAssignDifferentIdentityToTheSameReference()
    {
        var instance = new _TestRoot();

        var first = new UIEngineHost().GetOrCreateHandle(instance);
        var second = new UIEngineHost().GetOrCreateHandle(instance);

        Assert.NotEqual(first, second);
    }

    private sealed class _TestRoot;
}
