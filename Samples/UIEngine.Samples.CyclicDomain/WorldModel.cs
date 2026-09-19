using UIEngine.Attributes;

namespace UIEngine.Samples.CyclicDomain;

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
    public City? Capital { get; set; }

    [Children]
    public IList<City> Cities { get; } = new List<City>();

    [Summary]
    public string Summary => Code;

    [Action]
    public void AdvanceTurn() => _Turn++;

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
