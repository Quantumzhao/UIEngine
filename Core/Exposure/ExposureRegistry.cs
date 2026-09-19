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
    private Func<object, string?>? _DomainIdentity;
    private Func<object, string?>? _Summary;

    /// <summary>Sets the stable domain identity cached when an instance is encountered.</summary>
    public ExposureTypeBuilder<T> Identity(Func<T, string?> identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        if (_DomainIdentity is not null)
        {
            throw new InvalidOperationException(
                $"A domain identity is already registered for '{typeof(T).FullName}'.");
        }

        _DomainIdentity = instance => identity((T)instance);
        return this;
    }

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
                : (instance, value) => setter((T)instance, (TValue)value!),
            !typeof(TValue).IsValueType || System.Nullable.GetUnderlyingType(typeof(TValue)) is not null,
            [],
            null,
            [],
            [],
            null,
            []));
        return this;
    }

    /// <summary>Overrides whether a registered value accepts null.</summary>
    public ExposureTypeBuilder<T> Nullable(string identifier, bool isNullable)
    {
        _ReplaceValue(identifier, definition => definition with { IsNullable = isNullable });
        return this;
    }

    /// <summary>Declares the complete finite set of accepted values.</summary>
    public ExposureTypeBuilder<T> Selection<TValue>(
        string identifier,
        params TValue[] options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var definition = _GetValue(identifier);
        if (definition.ValueType != typeof(TValue))
        {
            throw new ArgumentException(
                $"Selection type '{typeof(TValue).FullName}' does not match value '{identifier}' type '{definition.ValueType.FullName}'.",
                nameof(options));
        }

        var selectionOptions = options
            .Select(static option => new SelectionOption(option, option?.ToString() ?? "null"))
            .ToArray();
        _ReplaceValue(identifier, current => current with
        {
            Options = selectionOptions,
            ValidationRules = current.ValidationRules
                .Append(new ValidationRuleDescriptor(ValidationRuleKind.FINITE_SELECTION))
                .ToArray(),
        });
        return this;
    }

    /// <summary>Declares inclusive comparable bounds for a registered value.</summary>
    public ExposureTypeBuilder<T> Range<TValue>(
        string identifier,
        TValue minimum,
        TValue maximum)
        where TValue : IComparable<TValue>
    {
        var definition = _GetValue(identifier);
        if (definition.ValueType != typeof(TValue))
        {
            throw new ArgumentException(
                $"Range type '{typeof(TValue).FullName}' does not match value '{identifier}' type '{definition.ValueType.FullName}'.",
                nameof(minimum));
        }

        if (minimum.CompareTo(maximum) > 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimum), "The range minimum cannot exceed its maximum.");
        }

        _ReplaceValue(identifier, current => current with
        {
            Range = new ValueRange(minimum, maximum),
            ValidationRules = current.ValidationRules
                .Append(new ValidationRuleDescriptor(ValidationRuleKind.RANGE))
                .ToArray(),
        });
        return this;
    }

    /// <summary>Adds one instance-aware domain validation rule evaluated after conversion.</summary>
    public ExposureTypeBuilder<T> Validate<TValue>(
        string identifier,
        Func<T, TValue, string?> validation)
    {
        ArgumentNullException.ThrowIfNull(validation);
        var definition = _GetValue(identifier);
        if (definition.ValueType != typeof(TValue))
        {
            throw new ArgumentException(
                $"Validation type '{typeof(TValue).FullName}' does not match value '{identifier}' type '{definition.ValueType.FullName}'.",
                nameof(validation));
        }

        _ReplaceValue(identifier, current => current with
        {
            Validators = current.Validators
                .Append((instance, value, _) => ValueTask.FromResult(
                    validation((T)instance, (TValue)value!)))
                .ToArray(),
            ValidationRules = current.ValidationRules
                .Append(new ValidationRuleDescriptor(ValidationRuleKind.PROGRAMMATIC))
                .ToArray(),
        });
        return this;
    }

    /// <summary>Adds one asynchronous instance-aware rule evaluated after conversion.</summary>
    public ExposureTypeBuilder<T> ValidateAsync<TValue>(
        string identifier,
        Func<T, TValue, CancellationToken, ValueTask<string?>> validation)
    {
        ArgumentNullException.ThrowIfNull(validation);
        var definition = _GetValue(identifier);
        if (definition.ValueType != typeof(TValue))
        {
            throw new ArgumentException(
                $"Validation type '{typeof(TValue).FullName}' does not match value '{identifier}' type '{definition.ValueType.FullName}'.",
                nameof(validation));
        }

        _ReplaceValue(identifier, current => current with
        {
            Validators = current.Validators
                .Append((instance, value, cancellationToken) =>
                    validation((T)instance, (TValue)value!, cancellationToken))
                .ToArray(),
            ValidationRules = current.ValidationRules
                .Append(new ValidationRuleDescriptor(ValidationRuleKind.PROGRAMMATIC))
                .ToArray(),
        });
        return this;
    }

    /// <summary>Adds optional semantic display metadata to a registered value.</summary>
    public ExposureTypeBuilder<T> Metadata(
        string identifier,
        string? unit = null,
        params string[] tags)
    {
        ArgumentNullException.ThrowIfNull(tags);
        if (tags.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Metadata tags cannot be empty or whitespace.", nameof(tags));
        }

        _ReplaceValue(identifier, current => current with
        {
            Unit = unit,
            Tags = tags.ToArray(),
        });
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
            _Summary,
            _DomainIdentity);
    }

    private ValueExposureDefinition _GetValue(string identifier)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        return _Values.LastOrDefault(definition =>
                string.Equals(definition.Id, identifier, StringComparison.Ordinal))
            ?? throw new ArgumentException(
                $"No programmatic value with identifier '{identifier}' has been registered.",
                nameof(identifier));
    }

    private void _ReplaceValue(
        string identifier,
        Func<ValueExposureDefinition, ValueExposureDefinition> update)
    {
        var definition = _GetValue(identifier);
        var index = _Values.LastIndexOf(definition);
        _Values[index] = update(definition);
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
    Action<object, object?>? Write,
    bool IsNullable,
    IReadOnlyList<SelectionOption> Options,
    ValueRange? Range,
    IReadOnlyList<ValidationRuleDescriptor> ValidationRules,
    IReadOnlyList<Func<object, object?, CancellationToken, ValueTask<string?>>> Validators,
    string? Unit,
    IReadOnlyList<string> Tags);

internal sealed record ExposureTypeRegistration(
    Type ObjectType,
    IReadOnlyList<ValueExposureDefinition> Values,
    Func<object, string?>? GetSummary,
    Func<object, string?>? GetDomainIdentity)
{
    public bool HasDescriptorExposure => Values.Count > 0 || GetSummary is not null;
}

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
