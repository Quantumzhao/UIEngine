using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace UIEngine.Core;

public sealed record UIEngineHostOptions
{
    public IEnumerable<TypeExposure> Exposures { get; init; } = [];

    public int MaxCollectionItems { get; init; } = 1_000;

    public ILoggerFactory LoggerFactory { get; init; } = NullLoggerFactory.Instance;

    public bool IncludeSensitiveDiagnosticData { get; init; }
}

internal sealed class HostSettings
{
    public HostSettings(UIEngineHostOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxCollectionItems);
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
        MaxCollectionItems = options.MaxCollectionItems;
        LoggerFactory = options.LoggerFactory;
        IncludeSensitiveDiagnosticData = options.IncludeSensitiveDiagnosticData;
    }

    public IReadOnlyDictionary<Type, TypeExposure> Exposures { get; }

    public int MaxCollectionItems { get; }

    public ILoggerFactory LoggerFactory { get; }

    public bool IncludeSensitiveDiagnosticData { get; }
}
