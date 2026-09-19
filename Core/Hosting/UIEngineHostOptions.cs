using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace UIEngine.Core;

/// <summary>Configures the host-scoped services and safety limits used by one runtime.</summary>
public sealed class UIEngineHostOptions
{
    public IEnumerable<IObjectDescriptorProvider> DescriptorProviders { get; init; } = [];

    public IInteractionDispatcher Dispatcher { get; init; } = InlineInteractionDispatcher.Instance;

    public IEnumerable<IDomainIdentityProvider> DomainIdentityProviders { get; init; } = [];

    public IEnumerable<IObservationAdapter> ObservationAdapters { get; init; } = [];

    public CollectionAccessLimits CollectionLimits { get; init; } = new();

    public ILoggerFactory LoggerFactory { get; init; } = NullLoggerFactory.Instance;

    public bool IncludeSensitiveDiagnosticData { get; init; }
}

/// <summary>An immutable snapshot of the options supplied when a host is created.</summary>
public sealed class UIEngineHostConfiguration
{
    internal UIEngineHostConfiguration(UIEngineHostOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.DescriptorProviders);
        ArgumentNullException.ThrowIfNull(options.Dispatcher);
        ArgumentNullException.ThrowIfNull(options.DomainIdentityProviders);
        ArgumentNullException.ThrowIfNull(options.ObservationAdapters);
        ArgumentNullException.ThrowIfNull(options.CollectionLimits);
        ArgumentNullException.ThrowIfNull(options.LoggerFactory);

        options.CollectionLimits.Validate();
        DescriptorProviders = _Snapshot(options.DescriptorProviders, nameof(options.DescriptorProviders));
        Dispatcher = options.Dispatcher;
        DomainIdentityProviders = _Snapshot(
            options.DomainIdentityProviders,
            nameof(options.DomainIdentityProviders));
        ObservationAdapters = _Snapshot(
            options.ObservationAdapters,
            nameof(options.ObservationAdapters));
        CollectionLimits = options.CollectionLimits with { };
        LoggerFactory = options.LoggerFactory;
        IncludeSensitiveDiagnosticData = options.IncludeSensitiveDiagnosticData;
    }

    /// <summary>
    /// Gets descriptor providers in descending precedence order. The first provider that supports
    /// a target is selected.
    /// </summary>
    public IReadOnlyList<IObjectDescriptorProvider> DescriptorProviders { get; }

    public IInteractionDispatcher Dispatcher { get; }

    /// <summary>Gets domain identity providers in descending precedence order.</summary>
    public IReadOnlyList<IDomainIdentityProvider> DomainIdentityProviders { get; }

    /// <summary>Gets observation adapters in descending precedence order.</summary>
    public IReadOnlyList<IObservationAdapter> ObservationAdapters { get; }

    public CollectionAccessLimits CollectionLimits { get; }

    public ILoggerFactory LoggerFactory { get; }

    public bool IncludeSensitiveDiagnosticData { get; }

    private static ReadOnlyCollection<T> _Snapshot<T>(IEnumerable<T> source, string parameterName)
        where T : class
    {
        var items = source.ToArray();
        if (items.Any(static item => item is null))
        {
            throw new ArgumentException("Configured service collections cannot contain null entries.", parameterName);
        }

        return new ReadOnlyCollection<T>(items);
    }
}

/// <summary>Bounds collection work and observation buffering performed by a host.</summary>
public sealed record CollectionAccessLimits
{
    public int MaxPageSize { get; init; } = 100;

    public int MaxSnapshotSize { get; init; } = 1_000;

    public int ObservationBufferCapacity { get; init; } = 256;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxPageSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxSnapshotSize);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ObservationBufferCapacity);
    }
}
