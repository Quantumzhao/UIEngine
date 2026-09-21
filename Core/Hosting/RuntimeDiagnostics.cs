using Microsoft.Extensions.Logging;

namespace UIEngine.Core;

internal static class RuntimeDiagnostics
{
    private static readonly Action<ILogger, string, Exception?> _UNEXPECTED_FAILURE =
        LoggerMessage.Define<string>(
            LogLevel.Warning,
            new EventId(2000, "UNEXPECTED_FAILURE"),
            "UIEngine operation {Operation} failed unexpectedly.");

    public static void UnexpectedFailure(
        ILogger logger,
        string operation,
        Exception exception,
        bool includeSensitiveData) =>
        _UNEXPECTED_FAILURE(logger, operation, includeSensitiveData ? exception : null);
}
