using System.Collections.ObjectModel;
using LanguageExt;
using static LanguageExt.Prelude;

namespace UIEngine.Core;

/// <summary>
/// Owns an ordered set of independent navigators over the active host.
/// </summary>
public sealed class UIEngineWorkspace : IDisposable
{
    private readonly List<Navigator> _Navigators = [];
    private readonly ReadOnlyCollection<Navigator> _NavigatorView;

    public UIEngineWorkspace()
    {
        _NavigatorView = _Navigators.AsReadOnly();
        UIEngineHost.Instance.RegisterWorkspace(this);
    }

    public IReadOnlyList<Navigator> Navigators => _NavigatorView;

    public event EventHandler<WorkspaceChangedEventArgs>? Changed;

    /// <summary>Adds a navigator whose initial location is a registered root.</summary>
    public Either<InteractionError, NavigatorAddedChange> AddNavigator(Guid rootId) =>
        AddNavigator(LogicalPath.Empty.Append(new RootLogicalPathSegment(rootId)));

    /// <summary>Adds a navigator whose stack is reconstructed from an absolute path.</summary>
    public Either<InteractionError, NavigatorAddedChange> AddNavigator(LogicalPath path) =>
        _AddNavigator(Guid.NewGuid(), path);

    private Either<InteractionError, NavigatorAddedChange> _AddNavigator(
        Guid id,
        LogicalPath path)
    {
        var entries = _ResolveInitialEntries(path);
        var navigator = new Navigator(id, entries);
        _Subscribe(navigator);
        var index = Navigators.Count;
        _Navigators.Add(navigator);

        var change = new NavigatorAddedChange(
            navigator.Id,
            index,
            navigator.CurrentPath,
            navigator.CurrentEntry);
        Publish(change);
        return Right(change);
    }

    /// <summary>
    /// Captures a supplied occurrence's location and creates a fresh navigator-owned stack there.
    /// </summary>
    public Either<InteractionError, NavigatorAddedChange> AddNavigator(ResolvedNode resolvedNode) =>
        AddNavigator(resolvedNode.CanonicalPath);

    /// <summary>Duplicates a navigator at its current path with fresh entry and node instances.</summary>
    public Either<InteractionError, NavigatorDuplicatedChange> DuplicateNavigator(
        Guid sourceNavigatorId)
    {
        var source = _FindNavigator(sourceNavigatorId);
        if (source is null)
        {
            return _NavigatorNotFound<NavigatorDuplicatedChange>(sourceNavigatorId);
        }

        var entries = _ResolveInitialEntries(source.CurrentPath);
        var navigator = new Navigator(Guid.NewGuid(), entries);
        _Subscribe(navigator);
        var index = _Navigators.IndexOf(source) + 1;
        _Navigators.Insert(index, navigator);

        var change = new NavigatorDuplicatedChange(
            source.Id,
            navigator.Id,
            index,
            navigator.CurrentPath,
            navigator.CurrentEntry);
        Publish(change);
        return Right(change);
    }

    /// <summary>Moves a navigator to a zero-based position in the workspace.</summary>
    public Either<InteractionError, NavigatorReorderedChange> ReorderNavigator(
        Guid navigatorId,
        int index)
    {
        if (index < 0 || index >= Navigators.Count)
        {
            return Left(new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                $"Navigator index {index} is outside the workspace."));
        }

        var navigator = _FindNavigator(navigatorId);
        if (navigator is null)
        {
            return _NavigatorNotFound<NavigatorReorderedChange>(navigatorId);
        }

        var previousIndex = _Navigators.IndexOf(navigator);
        if (previousIndex == index)
        {
            return Left(new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                "The navigator is already at the requested index."));
        }

        _Navigators.RemoveAt(previousIndex);
        _Navigators.Insert(index, navigator);
        var change = new NavigatorReorderedChange(navigator.Id, previousIndex, index);
        Publish(change);
        return Right(change);
    }

    /// <summary>Removes a navigator and all of its entries from this workspace.</summary>
    public Either<InteractionError, NavigatorRemovedChange> RemoveNavigator(Guid navigatorId)
    {
        var navigator = _FindNavigator(navigatorId);
        return navigator is null
            ? _NavigatorNotFound<NavigatorRemovedChange>(navigatorId)
            : Right(_RemoveNavigator(navigator));
    }

    /// <summary>Creates a serializable snapshot of stable navigation state.</summary>
    public Either<InteractionError, WorkspaceSnapshot> CreateSnapshot(
        Guid? selectedNavigatorId)
    {
        if (Navigators.Count == 0)
        {
            return selectedNavigatorId is null
                ? Right(new WorkspaceSnapshot([], null))
                : Left(new InteractionError(
                    InteractionErrorCode.INVALID_INPUT,
                    "An empty workspace cannot have a selected navigator."));
        }

        if (selectedNavigatorId is null ||
            !Navigators.Any(navigator => navigator.Id == selectedNavigatorId))
        {
            return Left(new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                $"Selected navigator '{selectedNavigatorId}' was not found."));
        }

        var snapshots = new List<NavigatorSnapshot>(Navigators.Count);
        foreach (var navigator in Navigators)
        {
            if (navigator.CurrentPath.IsEmpty)
            {
                return Left(new InteractionError(
                    InteractionErrorCode.INVALID_INPUT,
                    $"Navigator '{navigator.Id}' does not identify a registered root."));
            }

            var segments = new List<IPathSegmentSnapshot>(navigator.CurrentPath.Count);
            foreach (var segment in navigator.CurrentPath.Segments)
            {
                var serialized = _CreateSegmentSnapshot(segment);
                if (serialized is null)
                {
                    return Left(new InteractionError(
                        InteractionErrorCode.UNSUPPORTED,
                        $"Logical path segment type '{segment.GetType().FullName}' cannot be serialized."));
                }

                segments.Add(serialized);
            }

            snapshots.Add(new NavigatorSnapshot(navigator.Id, segments.ToArray()));
        }

        return Right(new WorkspaceSnapshot(snapshots, selectedNavigatorId));
    }

    /// <summary>
    /// Replaces this workspace with every usable navigator in <paramref name="snapshot"/>.
    /// Malformed records are skipped and reported without preventing other records from restoring.
    /// </summary>
    public WorkspaceRestoreResult RestoreSnapshot(WorkspaceSnapshot snapshot)
    {
        var issues = new List<SnapshotRestoreIssue>();
        var candidates = new List<(Guid Id, LogicalPath Path)>();
        var ids = new System.Collections.Generic.HashSet<Guid>();
        var records = snapshot.Navigators;

        if (records is null)
        {
            issues.Add(_RestoreIssue(
                SnapshotRestoreIssueCode.INVALID_NAVIGATOR_COLLECTION,
                null,
                null,
                "The snapshot navigator collection is missing."));
        }
        else
        {
            for (var index = 0; index < records.Count; index++)
            {
                var record = records[index];
                if (record is null || record.Id == Guid.Empty)
                {
                    issues.Add(_RestoreIssue(
                        SnapshotRestoreIssueCode.INVALID_NAVIGATOR_RECORD,
                        index,
                        record?.Id,
                        "A navigator record and its non-empty ID must be present."));
                    continue;
                }

                if (!ids.Add(record.Id))
                {
                    issues.Add(_RestoreIssue(
                        SnapshotRestoreIssueCode.DUPLICATE_NAVIGATOR_ID,
                        index,
                        record.Id,
                        $"Navigator ID '{record.Id}' has already appeared in the snapshot."));
                    continue;
                }

                var path = _RestorePath(record.CurrentPath);
                if (!path.IsRight)
                {
                    issues.Add(new SnapshotRestoreIssue(
                        SnapshotRestoreIssueCode.INVALID_NAVIGATOR_PATH,
                        index,
                        record.Id,
                        (InteractionError)path));
                    continue;
                }

                candidates.Add((record.Id, (LogicalPath)path));
            }
        }

        while (Navigators.Count > 0)
        {
            _RemoveNavigator(Navigators[^1]);
        }

        foreach (var candidate in candidates)
        {
            _AddNavigator(candidate.Id, candidate.Path);
        }

        var selectedNavigatorId = Navigators.Any(navigator =>
            navigator.Id == snapshot.SelectedNavigatorId)
            ? snapshot.SelectedNavigatorId
            : Navigators.Count > 0 ? Navigators[0].Id : null;
        if (selectedNavigatorId != snapshot.SelectedNavigatorId)
        {
            issues.Add(_RestoreIssue(
                SnapshotRestoreIssueCode.SELECTION_ADJUSTED,
                null,
                snapshot.SelectedNavigatorId,
                selectedNavigatorId is null
                    ? "The requested selection could not be restored because the workspace is empty."
                    : $"The requested selection could not be restored; '{selectedNavigatorId}' was selected instead."));
        }

        return new WorkspaceRestoreResult(selectedNavigatorId, issues.ToArray());
    }

    public void Dispose()
    {
        while (Navigators.Count > 0)
        {
            _RemoveNavigator(Navigators[^1]);
        }

        if (UIEngineHost.TryGetInstance(out var host))
        {
            host.UnregisterWorkspace(this);
        }
    }

    private void _OnNavigatorChanged(object? sender, WorkspaceChangedEventArgs eventArgs) =>
        Publish(eventArgs.Change);

    private Either<InteractionError, ResolvedPath> _ResolveNavigatorPath(
        Navigator navigator,
        LogicalPath path) => PathResolution.Resolve(path);

    private void _Subscribe(Navigator navigator)
    {
        navigator.ResolvePathRequested += _ResolveNavigatorPath;
        navigator.Changed += _OnNavigatorChanged;
    }

    private void _Unsubscribe(Navigator navigator)
    {
        navigator.Changed -= _OnNavigatorChanged;
        navigator.ResolvePathRequested -= _ResolveNavigatorPath;
    }

    private void Publish(WorkspaceChange change) =>
        Changed?.Invoke(this, new WorkspaceChangedEventArgs(change));

    private static (
        LogicalPath Path,
        Either<InteractionError, Option<BaseNode>> Entry)[] _ResolveInitialEntries(
            LogicalPath path)
    {
        var resolved = PathResolution.Resolve(path);
        if (resolved.IsRight)
        {
            return ((ResolvedPath)resolved).ResolutionChain
                .Select(static occurrence =>
                    (occurrence.CanonicalPath, _ResolvedEntry(occurrence.Node)))
                .ToArray();
        }

        if (path.IsEmpty)
        {
            return [(path, _InvalidEntry((InteractionError)resolved))];
        }

        var entries = new List<(
            LogicalPath Path,
            Either<InteractionError, Option<BaseNode>> Entry)>();
        var prefix = LogicalPath.Empty;
        foreach (var segment in path.Segments)
        {
            prefix = prefix.Append(segment);
            var prefixResolution = PathResolution.Resolve(prefix);
            if (prefixResolution.IsRight)
            {
                var resolvedPrefix = (ResolvedPath)prefixResolution;
                entries.Add((
                    resolvedPrefix.CanonicalPath,
                    _ResolvedEntry(resolvedPrefix.Node)));
            }
            else
            {
                entries.Add((prefix, _InvalidEntry((InteractionError)prefixResolution)));
            }
        }

        return [.. entries];
    }

    private static Either<InteractionError, Option<BaseNode>> _ResolvedEntry(BaseNode node) =>
        Right(Some(node));

    private static Either<InteractionError, Option<BaseNode>> _InvalidEntry(
        InteractionError error) =>
        Left(error);

    private static IPathSegmentSnapshot? _CreateSegmentSnapshot(
        ILogicalPathSegment segment) => segment switch
        {
            RootLogicalPathSegment root => new RootPathSegmentSnapshot(root.RootId),
            MemberLogicalPathSegment member => new MemberPathSegmentSnapshot(member.Name),
            ListLogicalPathSegment list => new ListPathSegmentSnapshot(list.Index),
            DictLogicalPathSegment dictionary =>
                new DictionaryPathSegmentSnapshot(dictionary.Key),
            _ => null,
        };

    private static Either<InteractionError, LogicalPath> _RestorePath(
        IReadOnlyList<IPathSegmentSnapshot> segments)
    {
        if (segments is null || segments.Count == 0)
        {
            return Left(new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                "A navigator path must identify a registered root."));
        }

        var path = LogicalPath.Empty;
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            if (segment is null)
            {
                return _InvalidPathSegment(index);
            }

            ILogicalPathSegment? restored;
            try
            {
                restored = segment switch
                {
                    RootPathSegmentSnapshot root =>
                        new RootLogicalPathSegment(root.RootId),
                    MemberPathSegmentSnapshot member =>
                        new MemberLogicalPathSegment(member.Name),
                    ListPathSegmentSnapshot list =>
                        new ListLogicalPathSegment(list.Index),
                    DictionaryPathSegmentSnapshot dictionary =>
                        new DictLogicalPathSegment(dictionary.Key),
                    _ => null,
                };
                if (restored is null)
                {
                    return _InvalidPathSegment(index);
                }

                path = path.Append(restored);
            }
            catch (ArgumentException)
            {
                return _InvalidPathSegment(index);
            }
        }

        return Right(path);
    }

    private static Either<InteractionError, LogicalPath> _InvalidPathSegment(int index) =>
        Left(new InteractionError(
            InteractionErrorCode.INVALID_INPUT,
            $"Path segment {index} is malformed or invalid at that location."));

    private static SnapshotRestoreIssue _RestoreIssue(
        SnapshotRestoreIssueCode code,
        int? navigatorIndex,
        Guid? navigatorId,
        string message) => new(
            code,
            navigatorIndex,
            navigatorId,
            new InteractionError(InteractionErrorCode.INVALID_INPUT, message));

    private Navigator? _FindNavigator(Guid navigatorId) =>
        _Navigators.Find(navigator => navigator.Id == navigatorId);

    private NavigatorRemovedChange _RemoveNavigator(Navigator navigator)
    {
        var previousIndex = _Navigators.IndexOf(navigator);
        _Navigators.RemoveAt(previousIndex);
        _Unsubscribe(navigator);
        var removed = navigator.Remove();
        var change = new NavigatorRemovedChange(
            navigator.Id,
            previousIndex,
            removed.Paths,
            removed.Entries);
        Publish(change);
        return change;
    }

    private static Either<InteractionError, T> _NavigatorNotFound<T>(Guid navigatorId) =>
        Left(new InteractionError(
            InteractionErrorCode.NOT_FOUND,
            $"Navigator '{navigatorId}' was not found."));
}
