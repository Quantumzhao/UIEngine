using System.Collections.ObjectModel;
using UIEngine.Core.Reflection;

namespace UIEngine.Core.Exposure;

/// <summary>Declares one named user parameter for a programmatic action.</summary>
public sealed record ProgrammaticParameter
{
    public ProgrammaticParameter(
        string id,
        Type parameterType,
        bool isRequired = true,
        bool isNullable = false,
        bool hasDefaultValue = false,
        object? defaultValue = null,
        IReadOnlyList<SelectionOption>? options = null,
        ValueRange? range = null,
        string? unit = null,
        IReadOnlyList<string>? tags = null,
        string? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(parameterType);
        if (parameterType == typeof(void))
        {
            throw new ArgumentException("An action parameter cannot have type void.", nameof(parameterType));
        }

        if (isRequired && hasDefaultValue)
        {
            throw new ArgumentException("A required parameter cannot declare a default value.");
        }

        if (!isNullable && hasDefaultValue && defaultValue is null)
        {
            throw new ArgumentException("A non-nullable parameter cannot declare a null default value.");
        }

        if (tags?.Any(string.IsNullOrWhiteSpace) == true)
        {
            throw new ArgumentException("Parameter tags cannot be empty or whitespace.", nameof(tags));
        }

        var effectiveType = Nullable.GetUnderlyingType(parameterType) ?? parameterType;
        if (options?.Any(option => option is null ||
                option.Value is not null && !effectiveType.IsInstanceOfType(option.Value)) == true)
        {
            throw new ArgumentException(
                "Every finite option must match the declared parameter type.",
                nameof(options));
        }

        if (range is not null &&
            (!effectiveType.IsInstanceOfType(range.Minimum) ||
                !effectiveType.IsInstanceOfType(range.Maximum) ||
                !_IsOrderedRange(range)))
        {
            throw new ArgumentException(
                "The parameter range must contain ordered bounds of the declared type.",
                nameof(range));
        }

        Id = id;
        DisplayName = displayName ?? id;
        ParameterType = parameterType;
        IsRequired = isRequired;
        IsNullable = isNullable;
        HasDefaultValue = hasDefaultValue;
        DefaultValue = defaultValue;
        Options = options?.ToArray() ?? [];
        Range = range;
        Unit = unit;
        Tags = tags?.ToArray() ?? [];
    }

    private static bool _IsOrderedRange(ValueRange range)
    {
        try
        {
            return ((IComparable)range.Minimum).CompareTo(range.Maximum) <= 0;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidCastException)
        {
            return false;
        }
    }

    public string Id { get; }

    public string DisplayName { get; }

    public Type ParameterType { get; }

    public bool IsRequired { get; }

    public bool IsNullable { get; }

    public bool HasDefaultValue { get; }

    public object? DefaultValue { get; }

    public IReadOnlyList<SelectionOption> Options { get; }

    public ValueRange? Range { get; }

    public string? Unit { get; }

    public IReadOnlyList<string> Tags { get; }
}

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
    private readonly List<ReferenceExposureDefinition> _References = [];
    private readonly List<CollectionExposureDefinition> _Collections = [];
    private readonly List<ActionExposureDefinition> _Actions = [];
    private Func<UIEngineHost, object, ObjectHandle, CancellationToken, ValueTask<InteractionResult<IObjectDescriptor>>>?
        _DescriptorFactory;
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

    /// <summary>Adds a live object reference, including a successful empty reference.</summary>
    public ExposureTypeBuilder<T> Reference<TReference>(
        string identifier,
        Func<T, TReference?> getter,
        Func<T, string>? displayName = null)
        where TReference : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentNullException.ThrowIfNull(getter);
        _References.Add(new ReferenceExposureDefinition(
            identifier,
            typeof(TReference),
            instance => displayName?.Invoke((T)instance) ?? identifier,
            instance => getter((T)instance)));
        return this;
    }

    /// <summary>
    /// Adds a finite indexed collection. Supplying a key selector also enables stable key paths.
    /// </summary>
    public ExposureTypeBuilder<T> Collection<TElement>(
        string identifier,
        Func<T, IReadOnlyList<TElement>?> getter,
        Func<TElement, string?>? keySelector = null,
        Func<T, string>? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentNullException.ThrowIfNull(getter);
        _Collections.Add(new CollectionExposureDefinition(
            identifier,
            typeof(TElement),
            keySelector is null ? null : typeof(string),
            CollectionCapabilities.FINITE_SNAPSHOT |
                CollectionCapabilities.VIRTUALIZED_RANGE |
                CollectionCapabilities.INDEXED |
                (keySelector is null ? CollectionCapabilities.NONE : CollectionCapabilities.KEYED),
            instance => displayName?.Invoke((T)instance) ?? identifier,
            instance => getter((T)instance),
            collection => ((IReadOnlyList<TElement>)collection).Count,
            (collection, index) => ((IReadOnlyList<TElement>)collection)[index],
            keySelector is null ? null : element => keySelector((TElement)element!)));
        return this;
    }

    /// <summary>Adds a synchronous programmatic action with explicit frontend parameter metadata.</summary>
    public ExposureTypeBuilder<T> Action(
        string identifier,
        IReadOnlyList<ProgrammaticParameter> parameters,
        Action<T, IReadOnlyDictionary<string, object?>> action,
        ActionRisk risk = ActionRisk.MUTATING,
        bool requiresConfirmation = false,
        Func<T, string>? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(action);
        _Actions.Add(new ActionExposureDefinition(
            identifier,
            instance => displayName?.Invoke((T)instance) ?? identifier,
            parameters.ToArray(),
            ResultType: null,
            IsAsynchronous: false,
            SupportsCancellation: false,
            ProgressType: null,
            risk,
            requiresConfirmation,
            (instance, arguments, _, _) =>
            {
                action((T)instance, arguments);
                return ValueTask.FromResult<object?>(null);
            }));
        return this;
    }

    /// <summary>Adds a synchronous programmatic action with an exposed result.</summary>
    public ExposureTypeBuilder<T> Action<TResult>(
        string identifier,
        IReadOnlyList<ProgrammaticParameter> parameters,
        Func<T, IReadOnlyDictionary<string, object?>, TResult> action,
        ActionRisk risk = ActionRisk.MUTATING,
        bool requiresConfirmation = false,
        Func<T, string>? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(action);
        _Actions.Add(new ActionExposureDefinition(
            identifier,
            instance => displayName?.Invoke((T)instance) ?? identifier,
            parameters.ToArray(),
            typeof(TResult),
            IsAsynchronous: false,
            SupportsCancellation: false,
            ProgressType: null,
            risk,
            requiresConfirmation,
            (instance, arguments, _, _) => ValueTask.FromResult<object?>(
                action((T)instance, arguments))));
        return this;
    }

    /// <summary>Adds an asynchronous cancellable programmatic action.</summary>
    public ExposureTypeBuilder<T> ActionAsync(
        string identifier,
        IReadOnlyList<ProgrammaticParameter> parameters,
        Func<T, IReadOnlyDictionary<string, object?>, CancellationToken, ValueTask> action,
        ActionRisk risk = ActionRisk.MUTATING,
        bool requiresConfirmation = false,
        Func<T, string>? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(action);
        _Actions.Add(new ActionExposureDefinition(
            identifier,
            instance => displayName?.Invoke((T)instance) ?? identifier,
            parameters.ToArray(),
            ResultType: null,
            IsAsynchronous: true,
            SupportsCancellation: true,
            ProgressType: null,
            risk,
            requiresConfirmation,
            async (instance, arguments, cancellationToken, _) =>
            {
                await action((T)instance, arguments, cancellationToken).ConfigureAwait(false);
                return null;
            }));
        return this;
    }

    /// <summary>Adds an asynchronous cancellable programmatic action with a result.</summary>
    public ExposureTypeBuilder<T> ActionAsync<TResult>(
        string identifier,
        IReadOnlyList<ProgrammaticParameter> parameters,
        Func<T, IReadOnlyDictionary<string, object?>, CancellationToken, ValueTask<TResult>> action,
        ActionRisk risk = ActionRisk.MUTATING,
        bool requiresConfirmation = false,
        Func<T, string>? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(action);
        _Actions.Add(new ActionExposureDefinition(
            identifier,
            instance => displayName?.Invoke((T)instance) ?? identifier,
            parameters.ToArray(),
            typeof(TResult),
            IsAsynchronous: true,
            SupportsCancellation: true,
            ProgressType: null,
            risk,
            requiresConfirmation,
            async (instance, arguments, cancellationToken, _) =>
                await action((T)instance, arguments, cancellationToken).ConfigureAwait(false)));
        return this;
    }

    /// <summary>Adds an asynchronous, cancellable, progress-reporting programmatic action.</summary>
    public ExposureTypeBuilder<T> ActionAsync<TProgress>(
        string identifier,
        IReadOnlyList<ProgrammaticParameter> parameters,
        Func<T, IReadOnlyDictionary<string, object?>, CancellationToken, IProgress<TProgress>, ValueTask> action,
        ActionRisk risk = ActionRisk.MUTATING,
        bool requiresConfirmation = false,
        Func<T, string>? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(action);
        _Actions.Add(new ActionExposureDefinition(
            identifier,
            instance => displayName?.Invoke((T)instance) ?? identifier,
            parameters.ToArray(),
            ResultType: null,
            IsAsynchronous: true,
            SupportsCancellation: true,
            typeof(TProgress),
            risk,
            requiresConfirmation,
            async (instance, arguments, cancellationToken, progress) =>
            {
                await action(
                    (T)instance,
                    arguments,
                    cancellationToken,
                    (IProgress<TProgress>)progress!).ConfigureAwait(false);
                return null;
            }));
        return this;
    }

    /// <summary>Adds an asynchronous, cancellable, progress-reporting action with a result.</summary>
    public ExposureTypeBuilder<T> ActionAsync<TResult, TProgress>(
        string identifier,
        IReadOnlyList<ProgrammaticParameter> parameters,
        Func<T, IReadOnlyDictionary<string, object?>, CancellationToken, IProgress<TProgress>, ValueTask<TResult>> action,
        ActionRisk risk = ActionRisk.MUTATING,
        bool requiresConfirmation = false,
        Func<T, string>? displayName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentNullException.ThrowIfNull(parameters);
        ArgumentNullException.ThrowIfNull(action);
        _Actions.Add(new ActionExposureDefinition(
            identifier,
            instance => displayName?.Invoke((T)instance) ?? identifier,
            parameters.ToArray(),
            typeof(TResult),
            IsAsynchronous: true,
            SupportsCancellation: true,
            typeof(TProgress),
            risk,
            requiresConfirmation,
            async (instance, arguments, cancellationToken, progress) =>
                await action(
                    (T)instance,
                    arguments,
                    cancellationToken,
                    (IProgress<TProgress>)progress!).ConfigureAwait(false)));
        return this;
    }

    /// <summary>
    /// Supplies a descriptor factory that can expose specialized roles; explicit registrations
    /// still override members with the same identifier.
    /// </summary>
    public ExposureTypeBuilder<T> DescriptorFactory(
        Func<UIEngineHost, T, ObjectHandle, CancellationToken, ValueTask<InteractionResult<IObjectDescriptor>>> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        if (_DescriptorFactory is not null)
        {
            throw new InvalidOperationException(
                $"A descriptor factory is already registered for '{typeof(T).FullName}'.");
        }

        _DescriptorFactory = (host, instance, handle, cancellationToken) =>
            factory(host, (T)instance, handle, cancellationToken);
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
        var duplicateIdentifier = _Values.Select(static definition => definition.Id)
            .Concat(_References.Select(static definition => definition.Id))
            .Concat(_Collections.Select(static definition => definition.Id))
            .Concat(_Actions.Select(static definition => definition.Id))
            .GroupBy(static identifier => identifier, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1)
            ?.Key;
        if (duplicateIdentifier is not null)
        {
            throw new ArgumentException(
                $"Programmatic exposure for '{typeof(T).FullName}' contains duplicate member identifier '{duplicateIdentifier}'.");
        }

        foreach (var action in _Actions)
        {
            if (action.ResultType?.ContainsGenericParameters == true ||
                action.ProgressType?.ContainsGenericParameters == true)
            {
                throw new ArgumentException(
                    $"Programmatic action '{action.Id}' has an unsupported open generic result or progress type.");
            }

            var invalidParameter = action.Parameters.FirstOrDefault(static parameter =>
                parameter.ParameterType.IsByRef || parameter.ParameterType.ContainsGenericParameters ||
                parameter.ParameterType == typeof(void));
            if (invalidParameter is not null)
            {
                throw new ArgumentException(
                    $"Programmatic action '{action.Id}' has unsupported parameter '{invalidParameter.Id}'.");
            }

            var duplicateParameter = action.Parameters
                .GroupBy(static parameter => parameter.Id, StringComparer.Ordinal)
                .FirstOrDefault(static group => group.Count() > 1)?.Key;
            if (duplicateParameter is not null)
            {
                throw new ArgumentException(
                    $"Programmatic action '{action.Id}' contains duplicate parameter '{duplicateParameter}'.");
            }

            foreach (var parameter in action.Parameters)
            {
                _ValidateParameter(action.Id, parameter);
            }
        }

        foreach (var value in _Values)
        {
            if (!value.IsNullable && value.Options.Any(static option => option.Value is null))
            {
                throw new ArgumentException(
                    $"Programmatic value '{value.Id}' is non-nullable but declares a null option.");
            }

            if (value.Range is not null && value.Options.Any(option =>
                    option.Value is not null && !_IsInRange(option.Value, value.Range)))
            {
                throw new ArgumentException(
                    $"Programmatic value '{value.Id}' declares a finite option outside its range.");
            }
        }

        return new ExposureTypeRegistration(
            typeof(T),
            new ReadOnlyCollection<ValueExposureDefinition>(_Values.ToArray()),
            new ReadOnlyCollection<ReferenceExposureDefinition>(_References.ToArray()),
            new ReadOnlyCollection<CollectionExposureDefinition>(_Collections.ToArray()),
            new ReadOnlyCollection<ActionExposureDefinition>(_Actions.ToArray()),
            _Summary,
            _DomainIdentity,
            _DescriptorFactory);
    }

    private static void _ValidateParameter(string actionId, ProgrammaticParameter parameter)
    {
        if (!parameter.IsNullable && parameter.Options.Any(static option => option.Value is null))
        {
            throw new ArgumentException(
                $"Programmatic action '{actionId}' parameter '{parameter.Id}' is non-nullable but declares a null option.");
        }

        if (parameter.HasDefaultValue)
        {
            var convertedDefault = ReflectionValueConverter.Convert(
                parameter.DefaultValue,
                parameter.ParameterType);
            if (!convertedDefault.IsSuccess)
            {
                throw new ArgumentException(
                    $"Programmatic action '{actionId}' parameter '{parameter.Id}' has an invalid default value.");
            }

            var defaultIssues = ValueValidation.Validate(
                convertedDefault.Value,
                parameter.IsNullable,
                parameter.Options,
                parameter.Range,
                [],
                null,
                InteractionIssueTarget.PARAMETER,
                parameter.Id);
            if (defaultIssues.Count > 0)
            {
                throw new ArgumentException(
                    $"Programmatic action '{actionId}' parameter '{parameter.Id}' has a default value that contradicts its metadata.");
            }
        }

        if (parameter.Range is not null && parameter.Options.Any(option =>
                option.Value is not null && !_IsInRange(option.Value, parameter.Range)))
        {
            throw new ArgumentException(
                $"Programmatic action '{actionId}' parameter '{parameter.Id}' declares a finite option outside its range.");
        }
    }

    private static bool _IsInRange(object value, ValueRange range)
    {
        try
        {
            return ((IComparable)value).CompareTo(range.Minimum) >= 0 &&
                ((IComparable)value).CompareTo(range.Maximum) <= 0;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidCastException)
        {
            return false;
        }
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

internal sealed record ReferenceExposureDefinition(
    string Id,
    Type ReferenceType,
    Func<object, string> GetDisplayName,
    Func<object, object?> Read);

internal sealed record CollectionExposureDefinition(
    string Id,
    Type ElementType,
    Type? KeyType,
    CollectionCapabilities Capabilities,
    Func<object, string> GetDisplayName,
    Func<object, object?> Read,
    Func<object, int> GetCount,
    Func<object, int, object?> GetElement,
    Func<object?, string?>? GetKey);

internal sealed record ActionExposureDefinition(
    string Id,
    Func<object, string> GetDisplayName,
    IReadOnlyList<ProgrammaticParameter> Parameters,
    Type? ResultType,
    bool IsAsynchronous,
    bool SupportsCancellation,
    Type? ProgressType,
    ActionRisk Risk,
    bool RequiresConfirmation,
    Func<object, IReadOnlyDictionary<string, object?>, CancellationToken, object?, ValueTask<object?>> Invoke);

internal sealed record ExposureTypeRegistration(
    Type ObjectType,
    IReadOnlyList<ValueExposureDefinition> Values,
    IReadOnlyList<ReferenceExposureDefinition> References,
    IReadOnlyList<CollectionExposureDefinition> Collections,
    IReadOnlyList<ActionExposureDefinition> Actions,
    Func<object, string?>? GetSummary,
    Func<object, string?>? GetDomainIdentity,
    Func<UIEngineHost, object, ObjectHandle, CancellationToken, ValueTask<InteractionResult<IObjectDescriptor>>>?
        DescriptorFactory)
{
    public bool HasDescriptorExposure => Values.Count > 0 || References.Count > 0 || Collections.Count > 0 ||
        Actions.Count > 0 || GetSummary is not null || DescriptorFactory is not null;
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
