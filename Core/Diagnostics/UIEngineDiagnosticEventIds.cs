using Microsoft.Extensions.Logging;

namespace UIEngine.Core;

/// <summary>Stable event identifiers for structured UIEngine diagnostics.</summary>
public static class UIEngineDiagnosticEventIds
{
    public static readonly EventId HOST_CREATED = new(1000, nameof(HOST_CREATED));
    public static readonly EventId HOST_DISPOSED = new(1001, nameof(HOST_DISPOSED));

    public static readonly EventId DISCOVERY_STARTED = new(2000, nameof(DISCOVERY_STARTED));
    public static readonly EventId DISCOVERY_COMPLETED = new(2001, nameof(DISCOVERY_COMPLETED));
    public static readonly EventId DISCOVERY_FAILED = new(2002, nameof(DISCOVERY_FAILED));
    public static readonly EventId CONFIGURATION_INVALID = new(2100, nameof(CONFIGURATION_INVALID));

    public static readonly EventId BINDING_RESOLVED = new(3000, nameof(BINDING_RESOLVED));
    public static readonly EventId BINDING_BROKEN = new(3001, nameof(BINDING_BROKEN));
    public static readonly EventId VALIDATION_FAILED = new(4000, nameof(VALIDATION_FAILED));
    public static readonly EventId MUTATION_COMPLETED = new(4100, nameof(MUTATION_COMPLETED));
    public static readonly EventId MUTATION_FAILED = new(4101, nameof(MUTATION_FAILED));
    public static readonly EventId INVOCATION_STARTED = new(5000, nameof(INVOCATION_STARTED));
    public static readonly EventId INVOCATION_COMPLETED = new(5001, nameof(INVOCATION_COMPLETED));
    public static readonly EventId INVOCATION_FAILED = new(5002, nameof(INVOCATION_FAILED));
    public static readonly EventId OBSERVATION_FAILED = new(6000, nameof(OBSERVATION_FAILED));
    public static readonly EventId COLLECTION_ACCESS_FAILED = new(7000, nameof(COLLECTION_ACCESS_FAILED));
    public static readonly EventId DISPATCH_FAILED = new(8000, nameof(DISPATCH_FAILED));
    public static readonly EventId CAPABILITY_MISMATCH = new(9000, nameof(CAPABILITY_MISMATCH));
}
