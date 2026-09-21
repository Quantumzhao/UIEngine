using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace UIEngine.Core;

public sealed record UIEngineHostOptions
{
    public IEnumerable<TypeExposure> Exposures { get; init; } = [];

    public IInteractionDispatcher Dispatcher { get; init; } = InlineInteractionDispatcher.Instance;

    public int MaxCollectionItems { get; init; } = 1_000;

    public int ObservationBufferCapacity { get; init; } = 256;

    public int InvocationProgressBufferCapacity { get; init; } = 256;

    public ILoggerFactory LoggerFactory { get; init; } = NullLoggerFactory.Instance;

    public bool IncludeSensitiveDiagnosticData { get; init; }
}

internal sealed class HostSettings
{
    public HostSettings(UIEngineHostOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxCollectionItems);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.ObservationBufferCapacity);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.InvocationProgressBufferCapacity);

        var exposures = options.Exposures.ToArray();
        var duplicate = exposures
            .GroupBy(static exposure => exposure.ObjectType)
            .FirstOrDefault(static group => group.Count() > 1)?.Key;
        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"Type '{duplicate.FullName}' has more than one programmatic exposure.",
                nameof(options));
        }

        Exposures = exposures.ToDictionary(static exposure => exposure.ObjectType);
        Dispatcher = options.Dispatcher;
        MaxCollectionItems = options.MaxCollectionItems;
        ObservationBufferCapacity = options.ObservationBufferCapacity;
        InvocationProgressBufferCapacity = options.InvocationProgressBufferCapacity;
        LoggerFactory = options.LoggerFactory;
        IncludeSensitiveDiagnosticData = options.IncludeSensitiveDiagnosticData;
    }

    public IReadOnlyDictionary<Type, TypeExposure> Exposures { get; }

    public IInteractionDispatcher Dispatcher { get; }

    public int MaxCollectionItems { get; }

    public int ObservationBufferCapacity { get; }

    public int InvocationProgressBufferCapacity { get; }

    public ILoggerFactory LoggerFactory { get; }

    public bool IncludeSensitiveDiagnosticData { get; }
}
