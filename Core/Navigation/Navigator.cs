using System.Collections.ObjectModel;
using LanguageExt;
using static LanguageExt.Prelude;

namespace UIEngine.Core;

/// <summary>One independent, destructive navigation stack within a workspace.</summary>
public sealed class Navigator
{
    private readonly UIEngineWorkspace _Workspace;

    internal Navigator(
        UIEngineWorkspace workspace,
        Guid id,
        string name,
        IReadOnlyList<(
            LogicalPath Path,
            Either<InteractionError, Option<BaseNode>> Entry)> entries)
    {
        _Workspace = workspace;
        Id = id;
        Name = name;
        Paths = entries.Select(static item => item.Path).ToList();
        Entries = entries.Select(static item => item.Entry).ToList();
    }

    public Guid Id { get; }

    public string Name { get; }

    public IReadOnlyList<LogicalPath> Paths { get; }

    public IReadOnlyList<Either<InteractionError, Option<BaseNode>>> Entries { get; }

    public LogicalPath CurrentPath => Paths[^1];

    public Either<InteractionError, Option<BaseNode>> CurrentEntry => Entries[^1];

    /// <summary>Resolves one child location and makes it the current entry.</summary>
    /// <remarks>
    /// A resolution failure is committed as a broken entry so that <see cref="GoBack"/> can
    /// return to the previous location.
    /// </remarks>
    public Either<InteractionError, NavigationPushedChange> Navigate(
        ILogicalPathSegment segment)
    {
        var current = Entries[^1];
        if (!current.IsRight)
        {
            return Left(new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                "A broken navigation entry has no child locations."));
        }

        var currentNode = ((Option<BaseNode>)current)
            .IfNoneUnsafe((BaseNode?)null);
        if (currentNode is null)
        {
            return Left(new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                "An empty navigation entry has no child locations."));
        }

        if (currentNode.IsTerminal)
        {
            return Left(new InteractionError(
                InteractionErrorCode.UNSUPPORTED,
                $"Node '{currentNode.Name}' is terminal and cannot be navigated further."));
        }

        LogicalPath target;
        try
        {
            target = Paths[^1].Append(segment);
        }
        catch (ArgumentException exception)
        {
            return Left(new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                exception.Message));
        }

        var resolved = _Workspace.Host.ResolvePath(target);
        LogicalPath path;
        Either<InteractionError, Option<BaseNode>> entry;
        if (resolved.IsRight)
        {
            var resolvedPath = (ResolvedPath)resolved;
            path = resolvedPath.CanonicalPath;
            entry = Right(Some(resolvedPath.Node));
        }
        else
        {
            path = target;
            entry = Left((InteractionError)resolved);
        }

        ((List<LogicalPath>)Paths).Add(path);
        ((List<Either<InteractionError, Option<BaseNode>>>)Entries).Add(entry);

        var change = new NavigationPushedChange(Id, path, entry);
        _Workspace.Publish(change);
        return Right(change);
    }

    /// <summary>Permanently removes the current entry and reveals its parent entry.</summary>
    public Either<InteractionError, NavigationPoppedChange> GoBack()
    {
        if (Entries.Count == 1)
        {
            return Left(new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                "The navigator is already at its root entry."));
        }

        var removedPath = Paths[^1];
        var removedEntry = Entries[^1];
        ((List<LogicalPath>)Paths).RemoveAt(Paths.Count - 1);
        ((List<Either<InteractionError, Option<BaseNode>>>)Entries).RemoveAt(Entries.Count - 1);
        var change = new NavigationPoppedChange(
            Id,
            removedPath,
            removedEntry,
            Paths[^1],
            Entries[^1]);
        _Workspace.Publish(change);
        return Right(change);
    }

    internal (
        IReadOnlyList<LogicalPath> Paths,
        IReadOnlyList<Either<InteractionError, Option<BaseNode>>> Entries) Remove()
    {
        var paths = Paths.ToArray();
        var entries = Entries.ToArray();
        ((List<LogicalPath>)Paths).Clear();
        ((List<Either<InteractionError, Option<BaseNode>>>)Entries).Clear();
        return (paths, entries);
    }
}
