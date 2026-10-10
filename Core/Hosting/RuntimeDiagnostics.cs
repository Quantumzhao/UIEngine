using Microsoft.Extensions.Logging;

namespace UIEngine.Core;

internal static class RuntimeDiagnostics
{
    private static readonly Action<ILogger, string, Exception?> _UNEXPECTED_FAILURE =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2000, "UNEXPECTED_FAILURE"),
            "UIEngine operation {Operation} failed unexpectedly.");
    private static readonly Action<ILogger, string?, string, Exception?> _ROOT_SKIPPED =
        LoggerMessage.Define<string?, string>(
            LogLevel.Warning,
            new EventId(2001, "ROOT_SKIPPED"),
            "Skipping unsupported root marker on {DeclaringType}.{MemberName}.");
    private static readonly Action<ILogger, string, Exception?> _ROOT_DISCOVERY_FAILURE =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2002, "ROOT_DISCOVERY_FAILURE"),
            "Skipping roots that could not be discovered from {Source}.");

    public static void UnexpectedFailure(
        ILogger logger,
        string operation,
        Exception exception,
        bool includeSensitiveData) =>
        _UNEXPECTED_FAILURE(logger, operation, includeSensitiveData ? exception : null);

    public static void RootSkipped(ILogger logger, string? declaringType, string memberName) =>
        _ROOT_SKIPPED(logger, declaringType, memberName, null);

    public static void RootDiscoveryFailure(
        ILogger logger,
        string source,
        Exception exception,
        bool includeSensitiveData) =>
        _ROOT_DISCOVERY_FAILURE(logger, source, includeSensitiveData ? exception : null);
}
