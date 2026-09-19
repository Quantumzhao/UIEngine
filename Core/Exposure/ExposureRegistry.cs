using System.Collections.ObjectModel;

namespace UIEngine.Core.Exposure;

/// <summary>Collects programmatic exposure definitions that a host snapshots at construction.</summary>
public sealed class ExposureRegistry
{
    private readonly Dictionary<Type, IExposureTypeBuilder> _Builders = [];

    public ExposureTypeBuilder<T> For<T>()
        where T : class
    {
        if (_Builders.TryGetValue(typeof(T), out var existing))
        {
            return (ExposureTypeBuilder<T>)existing;
        }

        var builder = new ExposureTypeBuilder<T>();
        _Builders.Add(typeof(T), builder);
        return builder;
    }

    internal ExposureRegistrySnapshot CreateSnapshot()
    {
        var registrations = _Builders.Values
            .Select(static builder => builder.CreateRegistration())
            .ToDictionary(static registration => registration.ObjectType);
        return new ExposureRegistrySnapshot(registrations);
    }
}

/// <summary>Builds programmatic exposure for one exact runtime type.</summary>
public sealed class ExposureTypeBuilder<T> : IExposureTypeBuilder
    where T : class
{
    private readonly List<ValueExposureDefinition> _Values = [];
    private Func<object, string?>? _Summary;

    /// <summary>Adds a live scalar value whose display name may depend on the current instance.</summary>
    public ExposureTypeBuilder<T> Value<TValue>(
        string identifier,
        Func<T, TValue> getter,
        Action<T, TValue>? setter = null,
        Func<T, string>? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentNullException.ThrowIfNull(getter);

        _Values.Add(new ValueExposureDefinition(
            identifier,
            typeof(TValue),
            instance => displayName?.Invoke((T)instance) ?? identifier,
            instance => getter((T)instance),
            setter is null
                ? null
                : (instance, value) => setter((T)instance, (TValue)value!)));
        return this;
    }

    /// <summary>Sets the summary evaluated for each described instance.</summary>
    public ExposureTypeBuilder<T> Summary(Func<T, string?> summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        if (_Summary is not null)
        {
            throw new InvalidOperationException($"A summary is already registered for '{typeof(T).FullName}'.");
        }

        _Summary = instance => summary((T)instance);
        return this;
    }

    ExposureTypeRegistration IExposureTypeBuilder.CreateRegistration()
    {
        var duplicateIdentifier = _Values
            .GroupBy(static definition => definition.Id, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1)
            ?.Key;
        if (duplicateIdentifier is not null)
        {
            throw new ArgumentException(
                $"Programmatic exposure for '{typeof(T).FullName}' contains duplicate member identifier '{duplicateIdentifier}'.");
        }

        return new ExposureTypeRegistration(
            typeof(T),
            new ReadOnlyCollection<ValueExposureDefinition>(_Values.ToArray()),
            _Summary);
    }
}

internal interface IExposureTypeBuilder
{
    ExposureTypeRegistration CreateRegistration();
}

internal sealed record ValueExposureDefinition(
    string Id,
    Type ValueType,
    Func<object, string> GetDisplayName,
    Func<object, object?> Read,
    Action<object, object?>? Write);

internal sealed record ExposureTypeRegistration(
    Type ObjectType,
    IReadOnlyList<ValueExposureDefinition> Values,
    Func<object, string?>? GetSummary);

internal sealed class ExposureRegistrySnapshot
{
    private readonly ReadOnlyDictionary<Type, ExposureTypeRegistration> _Registrations;

    public ExposureRegistrySnapshot(IDictionary<Type, ExposureTypeRegistration> registrations)
    {
        _Registrations = new ReadOnlyDictionary<Type, ExposureTypeRegistration>(registrations);
        RegisteredTypes = new ReadOnlyCollection<Type>(
            registrations.Keys.OrderBy(static type => type.FullName, StringComparer.Ordinal).ToArray());
    }

    public IReadOnlyList<Type> RegisteredTypes { get; }

    public bool TryGet(Type objectType, out ExposureTypeRegistration? registration) =>
        _Registrations.TryGetValue(objectType, out registration);
}
