using System.Collections.ObjectModel;
using System.ComponentModel;

namespace UIEngine.Core;

/// <summary>Immutable programmatic exposure for one exact runtime type.</summary>
public abstract class TypeExposure
{
    private protected TypeExposure(Type objectType)
    {
        ObjectType = objectType;
    }

    public Type ObjectType { get; }

    internal abstract string? GetSummary(object instance);

    internal abstract IReadOnlyList<ValueExposure> Values { get; }
}

public sealed class TypeExposure<T> : TypeExposure
    where T : class
{
    private readonly Func<T, string?>? _GetSummary;

    public TypeExposure(
        IReadOnlyList<ValueExposure>? values = null,
        Func<T, string?>? summary = null)
        : base(typeof(T))
    {
        var snapshot = values?.ToArray() ?? [];
        if (snapshot.Any(value => value.ObjectType != typeof(T)))
        {
            throw new ArgumentException(
                $"Every value exposure must target '{typeof(T).FullName}'.",
                nameof(values));
        }

        var duplicate = snapshot
            .GroupBy(static value => value.Name, StringComparer.Ordinal)
            .FirstOrDefault(static group => group.Count() > 1)?.Key;
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Programmatic exposure contains duplicate value name '{duplicate}'.",
                nameof(values));
        }

        Values = new ReadOnlyCollection<ValueExposure>(snapshot);
        _GetSummary = summary;
    }

    internal override IReadOnlyList<ValueExposure> Values { get; }

    internal override string? GetSummary(object instance) => _GetSummary?.Invoke((T)instance);
}

/// <summary>Immutable live scalar exposure shared by all instances of one type.</summary>
public abstract class ValueExposure
{
    private protected ValueExposure(
        Type objectType,
        string name,
        Type valueType,
        bool canWrite,
        bool isNullable,
        IValueRange? range,
        string? refreshPropertyName)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("A value name cannot be empty or whitespace.", nameof(name));
        }

        ObjectType = objectType;
        Name = name;
        ValueType = valueType;
        CanWrite = canWrite;
        IsNullable = isNullable;
        Range = range;
        RefreshPropertyName = refreshPropertyName;
    }

    public string Name { get; }

    public Type ValueType { get; }

    public bool CanWrite { get; }

    public bool IsNullable { get; }

    public IValueRange? Range { get; }

    internal Type ObjectType { get; }

    internal string? RefreshPropertyName { get; }

    internal abstract object? Read(object instance);

    internal abstract void Write(object instance, object? value);

    internal abstract INotifyPropertyChanged? GetRefreshSource(object instance);
}

public sealed class ValueExposure<T, TValue> : ValueExposure
    where T : class
{
    private readonly Func<T, TValue> _Getter;
    private readonly Action<T, TValue>? _Setter;
    private readonly Func<T, INotifyPropertyChanged?>? _GetRefreshSource;

    public ValueExposure(
        string name,
        Func<T, TValue> getter,
        Action<T, TValue>? setter = null,
        ValueRange<TValue>? range = null,
        bool? isNullable = null,
        Func<T, INotifyPropertyChanged?>? refreshSource = null,
        string? refreshPropertyName = null)
        : base(
            typeof(T),
            name,
            typeof(TValue),
            setter is not null,
            isNullable ?? _InferNullability(),
            range,
            refreshPropertyName)
    {
        if (refreshPropertyName is not null &&
            string.IsNullOrWhiteSpace(refreshPropertyName))
        {
            throw new ArgumentException(
                "A refresh property name cannot be empty or whitespace.",
                nameof(refreshPropertyName));
        }

        if (refreshPropertyName is not null && refreshSource is null)
        {
            throw new ArgumentException(
                "A refresh property name requires a refresh source.",
                nameof(refreshPropertyName));
        }

        if (range is not null && !_IsOrdered(range))
        {
            throw new ArgumentException(
                "Range bounds must be ordered values of the exposed type.",
                nameof(range));
        }

        _Getter = getter;
        _Setter = setter;
        _GetRefreshSource = refreshSource;
        Range = range;
    }

    public new ValueRange<TValue>? Range { get; }

    internal override object? Read(object instance) => _Getter((T)instance);

    internal override void Write(object instance, object? value) =>
        _Setter!((T)instance, (TValue)value!);

    internal override INotifyPropertyChanged? GetRefreshSource(object instance) =>
        _GetRefreshSource?.Invoke((T)instance);

    private static bool _InferNullability() =>
        !typeof(TValue).IsValueType || Nullable.GetUnderlyingType(typeof(TValue)) is not null;

    private static bool _IsOrdered(ValueRange<TValue> range)
    {
        try
        {
            return range.Minimum is IComparable minimum && minimum.CompareTo(range.Maximum) <= 0;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidCastException)
        {
            return false;
        }
    }
}
