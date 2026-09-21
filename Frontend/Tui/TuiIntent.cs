using UIEngine.Core;

namespace UIEngine.Frontend.Tui;

internal abstract record TuiIntent;

internal sealed record ReadPathIntent(
    LogicalPath Path,
    Action<InteractionResult<ResolvedPath>> Present) : TuiIntent;

internal readonly record struct QueuedTuiIntent(
    TuiIntent Intent,
    long Generation,
    CancellationToken CancellationToken);
