using UIEngine.Core;
using UIEngine.Core.Reflection;
using UIEngine.Examples.CyclicDomain;
using Xunit;

namespace UIEngine.Framework.Tests;

/// <summary>Verifies that the readable sample domain covers the Framework MVP interaction surface.</summary>
public sealed class CyclicWorldAcceptanceFixtureTests
{
    [Fact]
    public async Task FixtureDemonstratesEveryBuiltInAndProgrammaticIdentitySource()
    {
        var world = CyclicWorldFactory.Create();
        using var host = _CreateHost();
        var worldHandle = host.RegisterRoot("world", world).Value;

        var worldDescriptor = (await host.DescribeAsync(worldHandle)).Value;
        var economyHandle = (await Assert.Single(
            worldDescriptor.References,
            reference => reference.Id == nameof(World.Economy)).ReadAsync()).Value!.Value;
        var economyDescriptor = (await host.DescribeAsync(economyHandle)).Value;
        var nationHandle = Assert.Single((await Assert.Single(
            worldDescriptor.Collections,
            collection => collection.Id == nameof(World.Nations))
            .ReadAsync(CollectionReadRequest.Range(0, 1))).Value.Entries).Reference!.Value;
        var nationDescriptor = (await host.DescribeAsync(nationHandle)).Value;
        var cityHandle = (await Assert.Single(
            nationDescriptor.References,
            reference => reference.Id == nameof(Nation.Capital)).ReadAsync()).Value!.Value;
        var cityDescriptor = (await host.DescribeAsync(cityHandle)).Value;

        Assert.Equal(new DomainIdentity("world/Earth"), worldDescriptor.DomainIdentity);
        Assert.Equal(new DomainIdentity("nation/N1"), nationDescriptor.DomainIdentity);
        Assert.Equal(new DomainIdentity("city/capital"), cityDescriptor.DomainIdentity);
        Assert.Equal(new DomainIdentity("economy/Earth"), economyDescriptor.DomainIdentity);
        Assert.Equal(
            "Earth: 1000 million credits",
            economyDescriptor.Summary);
        Assert.Equal(
            1_000m,
            (await Assert.Single(economyDescriptor.Values).ReadAsync()).Value);
    }

    [Fact]
    public async Task FixtureEmitsNotificationsAndReplacesObjectsWithoutChangingDomainIdentity()
    {
        var world = CyclicWorldFactory.Create();
        var nation = Assert.Single(world.Nations);
        var originalCapital = nation.Capital!;
        using var host = _CreateHost();
        var nationHandle = host.RegisterRoot("nation", nation).Value;
        var originalHandle = host.GetOrCreateHandle(originalCapital);
        var originalIdentity = (await host.GetDomainIdentityAsync(originalHandle)).Value;
        await using var populationSubscription = (await host.ObserveAsync(
            nationHandle,
            new ObservationRequest { MemberId = nameof(Nation.Population) })).Value;
        await using var citySubscription = (await host.ObserveAsync(
            nationHandle,
            new ObservationRequest { MemberId = nameof(Nation.Cities) })).Value;
        await using var populationChanges = populationSubscription.ReadAllAsync().GetAsyncEnumerator();
        await using var cityChanges = citySubscription.ReadAllAsync().GetAsyncEnumerator();

        nation.Population = 120;
        var populationChange = await _NextAsync(populationChanges);
        var replacement = nation.ReplaceCapital("New Capital");
        var cityChange = await _NextAsync(cityChanges);
        var replacementHandle = host.GetOrCreateHandle(replacement);
        var replacementIdentity = (await host.GetDomainIdentityAsync(replacementHandle)).Value;

        Assert.Equal(ChangeKind.MEMBER_CHANGED, populationChange.Kind);
        Assert.Equal(nameof(Nation.Population), populationChange.MemberId);
        Assert.Equal(ChangeKind.COLLECTION_ITEMS_REPLACED, cityChange.Kind);
        Assert.Equal(nameof(Nation.Cities), cityChange.MemberId);
        Assert.NotEqual(originalHandle, replacementHandle);
        Assert.Equal(originalIdentity, replacementIdentity);
        Assert.Same(replacement, nation.Capital);
        Assert.Same(replacement, Assert.Single(nation.Cities));
    }

    [Fact]
    public async Task FixtureProvidesBoundedScaleValidationAndAsyncProgress()
    {
        var world = CyclicWorldFactory.Create();
        var nation = Assert.Single(world.Nations);
        using var host = _CreateHost();
        var worldDescriptor = (await host.DescribeAsync(
            host.RegisterRoot("world", world).Value)).Value;
        var forecast = Assert.Single(
            worldDescriptor.Collections,
            collection => collection.Id == nameof(World.PopulationForecast));

        var tail = await forecast.ReadAsync(CollectionReadRequest.Range(9_998, 2));

        Assert.True(forecast.Capabilities.HasFlag(CollectionCapabilities.VIRTUALIZED_RANGE));
        Assert.Equal(10_000, tail.Value.TotalCount);
        Assert.Equal(
            [10_098, 10_099],
            tail.Value.Entries.Select(static entry => entry.ScalarValue));

        var nationDescriptor = (await host.DescribeAsync(host.GetOrCreateHandle(nation))).Value;
        var population = Assert.Single(
            nationDescriptor.Values,
            value => value.Id == nameof(Nation.Population));
        var rejected = await population.WriteAsync(-1);

        Assert.Equal(InteractionErrorCode.VALIDATION_FAILED, rejected.Error?.Code);
        Assert.Equal(100, nation.Population);

        var simulation = Assert.Single(
            nationDescriptor.Actions,
            action => action.Id == nameof(Nation.SimulateGrowthAsync));
        var started = await simulation.InvokeAsync(new Dictionary<string, object?>
        {
            ["years"] = 3,
            ["populationPerYear"] = 2,
        });
        var progress = new List<SimulationProgress>();
        await foreach (var update in started.Value.ReadProgressAsync())
        {
            progress.Add(Assert.IsType<SimulationProgress>(update.Value));
        }

        var completion = await started.Value.Completion;

        Assert.True(simulation.IsAsynchronous);
        Assert.True(simulation.SupportsCancellation);
        Assert.Equal(typeof(SimulationProgress), simulation.ProgressType);
        Assert.Equal([1, 2, 3], progress.Select(static update => update.Year));
        Assert.Equal([102, 104, 106], progress.Select(static update => update.Population));
        Assert.Equal(106, completion.Value);
        Assert.Equal(InvocationStatus.SUCCEEDED, started.Value.Status);
    }

    [Fact]
    public async Task FixtureRootReplacementCreatesNewRuntimeIdentityWithStableDomainIdentity()
    {
        using var host = _CreateHost();
        var originalHandle = host.RegisterRoot("world", CyclicWorldFactory.Create()).Value;
        var original = (await host.DescribeAsync(originalHandle)).Value;

        var replacementHandle = host.ReplaceRoot("world", CyclicWorldFactory.Create()).Value;
        var replacement = (await host.DescribeAsync(replacementHandle)).Value;

        Assert.NotEqual(original.Identity, replacement.Identity);
        Assert.Equal(original.DomainIdentity, replacement.DomainIdentity);
    }

    private static UIEngineHost _CreateHost() => new(new UIEngineHostOptions
    {
        Exposure = CyclicWorldFactory.CreateExposureRegistry(),
        DescriptorProviders = [new ReflectionObjectDescriptorProvider()],
    });

    private static async Task<ChangeRecord> _NextAsync(IAsyncEnumerator<ChangeRecord> changes)
    {
        Assert.True(await changes.MoveNextAsync());
        return changes.Current;
    }
}
