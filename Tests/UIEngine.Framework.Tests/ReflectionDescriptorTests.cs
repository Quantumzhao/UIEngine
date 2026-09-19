using UIEngine.Core;
using UIEngine.Core.Attributes;
using UIEngine.Core.Reflection;
using UIEngine.Examples.CyclicDomain;
using Xunit;

namespace UIEngine.Framework.Tests;

/// <summary>Verifies cached reflection discovery and lazy graph traversal.</summary>
public sealed class ReflectionDescriptorTests
{
    private static readonly string[] _EXPECTED_OVERLOADED_ACTION_IDENTIFIERS = ["Run", "Run#2"];

    [Fact]
    public void ReflectionMetadataIsCachedPerType()
    {
        var first = ReflectionTypeMetadataCache.GetOrCreate(typeof(Nation));
        var second = ReflectionTypeMetadataCache.GetOrCreate(typeof(Nation));

        Assert.Same(first, second);
    }

    [Fact]
    public async Task DescriptorMembersAreClassifiedWithStableUniqueIdentifiers()
    {
        var nation = new Nation("N1");
        var capital = new City("Capital", nation);
        nation.Capital = capital;
        nation.Cities.Add(capital);
        var host = new UIEngineHost([new ReflectionObjectDescriptorProvider()]);
        var handle = host.RegisterRoot("nation", nation).Value;

        var first = (await host.DescribeAsync(handle)).Value;
        var second = (await host.DescribeAsync(handle)).Value;

        var value = Assert.Single(first.Values, descriptor => descriptor.Id == nameof(Nation.Code));
        var reference = Assert.Single(first.References);
        var collection = Assert.Single(first.Collections);

        Assert.Equal(nameof(Nation.Code), value.Id);
        Assert.Equal(typeof(string), value.ValueType);
        Assert.Equal(nameof(Nation.Capital), reference.Id);
        Assert.Equal(typeof(City), reference.ReferenceType);
        Assert.Equal(nameof(Nation.Cities), collection.Id);
        Assert.Equal(typeof(City), collection.ElementType);
        Assert.Equal(nameof(Nation.AdvanceTurn), Assert.Single(first.Actions).Id);
        Assert.Equal("N1: 0 resident(s), turn 0", first.Summary);

        var firstIdentifiers = _GetIdentifiers(first);
        Assert.Equal(firstIdentifiers.Length, firstIdentifiers.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(firstIdentifiers, _GetIdentifiers(second));
        Assert.DoesNotContain(nameof(Nation.HiddenState), firstIdentifiers);
    }

    [Fact]
    public async Task ReferenceTraversalReturnsToOriginalIdentityWithoutRecursiveExpansion()
    {
        var nation = new Nation("N1");
        var capital = new City("Capital", nation);
        nation.Capital = capital;
        var host = new UIEngineHost([new ReflectionObjectDescriptorProvider()]);
        var nationHandle = host.RegisterRoot("nation", nation).Value;

        var nationDescriptor = (await host.DescribeAsync(nationHandle)).Value;
        var capitalHandle = (await Assert.Single(nationDescriptor.References).ReadAsync()).Value!.Value;
        var capitalDescriptor = (await host.DescribeAsync(capitalHandle)).Value;
        var ownerHandle = (await Assert.Single(capitalDescriptor.References).ReadAsync()).Value!.Value;

        Assert.Equal(nationHandle, ownerHandle);
        Assert.Equal(nationHandle.Identity, nationDescriptor.Identity);
        Assert.Equal(capitalHandle.Identity, capitalDescriptor.Identity);
    }

    [Fact]
    public async Task OverloadedActionsReceiveDeterministicUniqueIdentifiers()
    {
        var host = new UIEngineHost([new ReflectionObjectDescriptorProvider()]);
        var handle = host.RegisterRoot("actions", new _OverloadedActions()).Value;

        var first = (await host.DescribeAsync(handle)).Value.Actions.Select(static action => action.Id).ToArray();
        var second = (await host.DescribeAsync(handle)).Value.Actions.Select(static action => action.Id).ToArray();

        Assert.Equal(_EXPECTED_OVERLOADED_ACTION_IDENTIFIERS, first);
        Assert.Equal(first, second);
    }

    private static string[] _GetIdentifiers(IObjectDescriptor descriptor) =>
        descriptor.Values.Cast<IMemberDescriptor>()
            .Concat(descriptor.References)
            .Concat(descriptor.Collections)
            .Concat(descriptor.Actions)
            .Select(static member => member.Id)
            .ToArray();

    /// <summary>Provides overloaded reflected actions for identifier tests.</summary>
    private sealed class _OverloadedActions
    {
        private int _ExecutionCount;

        [Action]
        public void Run() => _ExecutionCount++;

        [Action]
        public void Run(int value) => _ExecutionCount += value;
    }
}
