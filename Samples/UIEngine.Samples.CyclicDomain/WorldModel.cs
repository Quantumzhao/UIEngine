using UIEngine.Attributes;

namespace UIEngine.Samples.CyclicDomain;

/// <summary>Creates the deterministic cyclic graph used by the CLI and integration tests.</summary>
public static class CyclicWorldFactory
{
    public static World Create()
    {
        var world = new World("Earth");
        var nation = new Nation("N1") { Population = 100 };
        var capital = new City("Capital City", nation);
        nation.Capital = capital;
        nation.Cities.Add(capital);
        world.Nations.Add(nation);
        return world;
    }
}

/// <summary>Provides a deterministic root for the cyclic sample domain.</summary>
public sealed class World
{
    public World(string name)
    {
        Name = name;
    }

    [Expose]
    public string Name { get; }

    [Children]
    public IList<Nation> Nations { get; } = new List<Nation>();

    [Summary]
    public string Summary => $"{Name}: {Nations.Count} nation(s)";
}

/// <summary>Represents a nation that owns cities and refers to its capital.</summary>
public sealed class Nation
{
    private int _Turn;

    public Nation(string code)
    {
        Code = code;
    }

    [Expose]
    public string Code { get; }

    [Expose]
    public int Population { get; set; }

    [Expose]
    public string Motto { get; set; } = "Forward";

    [Expose(ReadOnly = true)]
    public int Turn => _Turn;

    [Expose]
    public City? Capital { get; set; }

    [Children]
    public IList<City> Cities { get; } = new List<City>();

    [Summary]
    public string Summary => $"{Code}: {Population} resident(s), turn {Turn}";

    [Action]
    public int AdvanceTurn(int populationDelta)
    {
        if (populationDelta < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(populationDelta),
                "Population delta cannot be negative.");
        }

        Population += populationDelta;
        _Turn++;
        return Population;
    }

    public string HiddenState { get; } = "hidden";
}

/// <summary>Represents a city whose owner reference closes the sample cycle.</summary>
public sealed class City
{
    public City(string name, Nation ownerNation)
    {
        Name = name;
        OwnerNation = ownerNation;
    }

    [Expose]
    public string Name { get; }

    [Expose]
    public Nation OwnerNation { get; }

    [Summary]
    public string Summary => Name;
}
