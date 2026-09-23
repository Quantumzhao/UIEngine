using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Runtime.CompilerServices;
using UIEngine.Core;
using UIEngine.Core.Attributes;

namespace UIEngine.Examples.CyclicDomain;

/// <summary>Creates the deterministic cyclic graph used by the CLI and integration tests.</summary>
public static class CyclicWorldFactory
{
    /// <summary>Creates the immutable exposure for the unannotated sample type.</summary>
    public static IReadOnlyList<TypeExposure> CreateExposures() =>
    [
        new TypeExposure<EconomicProfile>(
            values:
            [
                new ValueExposure<EconomicProfile, decimal>(
                    nameof(EconomicProfile.GrossDomesticProduct),
                    static profile => profile.GrossDomesticProduct,
                    static (profile, value) => profile.GrossDomesticProduct = value,
                    new ValueRange<decimal>(0m, 10_000_000m)),
            ],
            summary: static profile =>
                $"{profile.RegionCode}: " +
                $"{profile.GrossDomesticProduct.ToString(CultureInfo.InvariantCulture)} million credits"),
    ];

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
        Economy = new EconomicProfile(name, 1_000m);
    }

    [Expose]
    public string Name { get; }

    [Children]
    public ObservableCollection<Nation> Nations { get; } = [];

    [Expose]
    public EconomicProfile Economy { get; }

    /// <summary>A large computed list that supports bounded range access without enumeration.</summary>
    [Children]
    public IReadOnlyList<int> PopulationForecast { get; } =
        new PopulationProjectionSeries(startingPopulation: 100, count: 10_000);

    [Summary]
    public string Summary => $"{Name}: {Nations.Count} nation(s)";
}

/// <summary>Represents a nation that owns cities and refers to its capital.</summary>
public sealed class Nation : INotifyPropertyChanged
{
    private City? _Capital;
    private string _Motto = "Forward";
    private string? _OptionalNote;
    private int _Population;
    private int _Turn;

    public Nation(string code)
    {
        Code = code;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    [Expose]
    public string Code { get; }

    [Expose]
    [Range(0, 1_000_000)]
    public int Population
    {
        get => _Population;
        set => _SetField(ref _Population, value);
    }

    [Expose]
    [Required]
    public string Motto
    {
        get => _Motto;
        set => _SetField(ref _Motto, value);
    }

    [Expose]
    public string? OptionalNote
    {
        get => _OptionalNote;
        set => _SetField(ref _OptionalNote, value);
    }

    [Expose(ReadOnly = true)]
    public int Turn => _Turn;

    [Expose]
    public City? Capital
    {
        get => _Capital;
        set => _SetField(ref _Capital, value);
    }

    [Children]
    public ObservableCollection<City> Cities { get; } = [];

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
        _OnPropertyChanged(nameof(Turn));
        return Population;
    }

    /// <summary>Replaces the capital instance.</summary>
    [Action]
    public City ReplaceCapital(string name)
    {
        var replacement = new City(name, this);
        var existingIndex = Capital is null ? -1 : Cities.IndexOf(Capital);
        Capital = replacement;
        if (existingIndex < 0)
        {
            Cities.Add(replacement);
        }
        else
        {
            Cities[existingIndex] = replacement;
        }

        return replacement;
    }

    /// <summary>Runs deterministic asynchronous turns.</summary>
    [Action]
    public async Task<int> SimulateGrowthAsync(
        [Range(1, 100)] int years,
        [Range(0, 10_000)] int populationPerYear)
    {
        for (var year = 1; year <= years; year++)
        {
            await Task.Yield();
            Population += populationPerYear;
            _Turn++;
            _OnPropertyChanged(nameof(Turn));
        }

        return Population;
    }

    public string HiddenState { get; } = "hidden";

    private void _OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void _SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        _OnPropertyChanged(propertyName);
    }
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

/// <summary>Models an unannotated third-party type configured through programmatic exposure.</summary>
public sealed class EconomicProfile(string regionCode, decimal grossDomesticProduct)
{
    public string RegionCode { get; } = regionCode;

    public decimal GrossDomesticProduct { get; set; } = grossDomesticProduct;
}

/// <summary>Computes a large indexed series without storing or enumerating all values.</summary>
public sealed class PopulationProjectionSeries : IReadOnlyList<int>
{
    private readonly int _StartingPopulation;

    public PopulationProjectionSeries(int startingPopulation, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        _StartingPopulation = startingPopulation;
        Count = count;
    }

    public int Count { get; }

    public int this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Count);

            return _StartingPopulation + index;
        }
    }

    public IEnumerator<int> GetEnumerator()
    {
        for (var index = 0; index < Count; index++)
        {
            yield return this[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
