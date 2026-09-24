using LanguageExt;

namespace UIEngine.Core;

/// <summary>
/// Carries a canonical location beside one resolved node occurrence without putting a path on the
/// node itself.
/// </summary>
public sealed class ResolvedNode
{
    internal ResolvedNode(LogicalPath canonicalPath, BaseNode node)
    {
        CanonicalPath = canonicalPath;
        Node = node;
    }

    public LogicalPath CanonicalPath { get; }

    public BaseNode Node { get; }

    internal UIEngineHost Host => Node.Host;
}

/// <summary>
/// Describes one committed navigator mutation. Implementations publish the same instance as the
/// successful mutation result and in their change notification.
/// </summary>
public abstract record WorkspaceChange(Guid NavigatorId);

public sealed record NavigatorAddedChange(
    Guid NavigatorId,
    int Index,
    LogicalPath InitialPath,
    Either<InteractionError, Option<BaseNode>> InitialEntry)
    : WorkspaceChange(NavigatorId);

public sealed record NavigationPushedChange(
    Guid NavigatorId,
    LogicalPath Path,
    Either<InteractionError, Option<BaseNode>> Entry)
    : WorkspaceChange(NavigatorId);

public sealed record NavigationPoppedChange(
    Guid NavigatorId,
    LogicalPath RemovedPath,
    Either<InteractionError, Option<BaseNode>> RemovedEntry,
    LogicalPath CurrentPath,
    Either<InteractionError, Option<BaseNode>> CurrentEntry)
    : WorkspaceChange(NavigatorId);

public sealed record NavigatorDuplicatedChange(
    Guid SourceNavigatorId,
    Guid NavigatorId,
    int Index,
    LogicalPath InitialPath,
    Either<InteractionError, Option<BaseNode>> InitialEntry)
    : WorkspaceChange(NavigatorId);

public sealed record NavigatorReorderedChange(
    Guid NavigatorId,
    int PreviousIndex,
    int CurrentIndex)
    : WorkspaceChange(NavigatorId);

public sealed record NavigatorRemovedChange(
    Guid NavigatorId,
    int PreviousIndex,
    IReadOnlyList<LogicalPath> RemovedPaths,
    IReadOnlyList<Either<InteractionError, Option<BaseNode>>> RemovedEntries)
    : WorkspaceChange(NavigatorId);

/// <summary>Event data for a committed, frontend-neutral workspace change.</summary>
public sealed class WorkspaceChangedEventArgs(WorkspaceChange change) : EventArgs
{
    public WorkspaceChange Change { get; } = change ??
        throw new ArgumentNullException(nameof(change));
}
