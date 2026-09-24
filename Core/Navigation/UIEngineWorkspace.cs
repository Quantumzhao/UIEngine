using System.Collections.ObjectModel;
using LanguageExt;
using static LanguageExt.Prelude;

namespace UIEngine.Core;

/// <summary>
/// Owns an ordered set of independent navigators over one caller-owned host.
/// </summary>
public sealed class UIEngineWorkspace : IDisposable
{
    public List<Navigator> Navigators { get; } = [];

    public UIEngineWorkspace(UIEngineHost host)
    {
        Host = host;
    }

    public event EventHandler<WorkspaceChangedEventArgs>? Changed;

    internal UIEngineHost Host { get; }

    /// <summary>Adds a navigator whose initial location is a registered root.</summary>
    public Either<InteractionError, NavigatorAddedChange> AddNavigator( string name, string rootName) => 
        AddNavigator(name, LogicalPath.Root.Append(rootName));

    /// <summary>Adds a navigator whose stack is reconstructed from an absolute path.</summary>
    public Either<InteractionError, NavigatorAddedChange> AddNavigator(
        string name,
        LogicalPath path)
    {
        var validation = _ValidateNewName(name);
        if (validation is not null)
        {
            return Left(validation);
        }

        var entries = _ResolveInitialEntries(path);
        var navigator = new Navigator(this, Guid.NewGuid(), name, entries);
        var index = Navigators.Count;
        Navigators.Add(navigator);

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
    public Either<InteractionError, NavigatorAddedChange> AddNavigator(string name, ResolvedNode resolvedNode) => 
        AddNavigator(name, resolvedNode.CanonicalPath);

    /// <summary>Duplicates a navigator at its current path with fresh entry and node instances.</summary>
    public Either<InteractionError, NavigatorDuplicatedChange> DuplicateNavigator(
        Guid sourceNavigatorId,
        string name)
    {
        var validation = _ValidateNewName(name);
        if (validation is not null)
        {
            return Left(validation);
        }

        var source = _FindNavigator(sourceNavigatorId);
        if (source is null)
        {
            return _NavigatorNotFound<NavigatorDuplicatedChange>(sourceNavigatorId);
        }

        var entries = _ResolveInitialEntries(source.CurrentPath);
        var navigator = new Navigator(this, Guid.NewGuid(), name, entries);
        var index = Navigators.IndexOf(source) + 1;
        Navigators.Insert(index, navigator);

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

        var previousIndex = Navigators.IndexOf(navigator);
        if (previousIndex == index)
        {
            return Left(new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                "The navigator is already at the requested index."));
        }

        Navigators.RemoveAt(previousIndex);
        Navigators.Insert(index, navigator);
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
        string? selectedNavigatorName)
    {
        if (Navigators.Count == 0)
        {
            return selectedNavigatorName is null
                ? Right(new WorkspaceSnapshot([], null))
                : Left(new InteractionError(
                    InteractionErrorCode.INVALID_INPUT,
                    "An empty workspace cannot have a selected navigator."));
        }

        if (string.IsNullOrWhiteSpace(selectedNavigatorName) ||
            !Navigators.Any(navigator => StringComparer.Ordinal.Equals(
                navigator.Name,
                selectedNavigatorName)))
        {
            return Left(new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                $"Selected navigator '{selectedNavigatorName}' was not found."));
        }

        var snapshots = new List<NavigatorSnapshot>(Navigators.Count);
        foreach (var navigator in Navigators)
        {
            if (navigator.CurrentPath.IsRoot)
            {
                return Left(new InteractionError(
                    InteractionErrorCode.INVALID_INPUT,
                    $"Navigator '{navigator.Name}' does not identify a registered root."));
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

            snapshots.Add(new NavigatorSnapshot(navigator.Name, segments.ToArray()));
        }

        return Right(new WorkspaceSnapshot(snapshots, selectedNavigatorName));
    }

    /// <summary>
    /// Replaces this workspace with every usable navigator in <paramref name="snapshot"/>.
    /// Malformed records are skipped and reported without preventing other records from restoring.
    /// </summary>
    public WorkspaceRestoreResult RestoreSnapshot(WorkspaceSnapshot snapshot)
    {
        var issues = new List<SnapshotRestoreIssue>();
        var candidates = new List<(string Name, LogicalPath Path)>();
        var names = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
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
                if (record is null || string.IsNullOrWhiteSpace(record.Name))
                {
                    issues.Add(_RestoreIssue(
                        SnapshotRestoreIssueCode.INVALID_NAVIGATOR_RECORD,
                        index,
                        record?.Name,
                        "A navigator record and its name must be present."));
                    continue;
                }

                if (!names.Add(record.Name))
                {
                    issues.Add(_RestoreIssue(
                        SnapshotRestoreIssueCode.DUPLICATE_NAVIGATOR_NAME,
                        index,
                        record.Name,
                        $"Navigator name '{record.Name}' has already appeared in the snapshot."));
                    continue;
                }

                var path = _RestorePath(record.CurrentPath);
                if (!path.IsRight)
                {
                    issues.Add(new SnapshotRestoreIssue(
                        SnapshotRestoreIssueCode.INVALID_NAVIGATOR_PATH,
                        index,
                        record.Name,
                        (InteractionError)path));
                    continue;
                }

                candidates.Add((record.Name, (LogicalPath)path));
            }
        }

        while (Navigators.Count > 0)
        {
            _RemoveNavigator(Navigators[^1]);
        }

        foreach (var candidate in candidates)
        {
            AddNavigator(candidate.Name, candidate.Path);
        }

        var selectedNavigatorName = Navigators.Any(navigator => StringComparer.Ordinal.Equals(
            navigator.Name,
            snapshot.SelectedNavigatorName))
            ? snapshot.SelectedNavigatorName
            : Navigators.FirstOrDefault()?.Name;
        if (!StringComparer.Ordinal.Equals(selectedNavigatorName, snapshot.SelectedNavigatorName))
        {
            issues.Add(_RestoreIssue(
                SnapshotRestoreIssueCode.SELECTION_ADJUSTED,
                null,
                snapshot.SelectedNavigatorName,
                selectedNavigatorName is null
                    ? "The requested selection could not be restored because the workspace is empty."
                    : $"The requested selection could not be restored; '{selectedNavigatorName}' was selected instead."));
        }

        return new WorkspaceRestoreResult(selectedNavigatorName, issues.ToArray());
    }

    public void Dispose()
    {
        while (Navigators.Count > 0)
        {
            _RemoveNavigator(Navigators[^1]);
        }
    }

    internal void Publish(WorkspaceChange change) =>
        Changed?.Invoke(this, new WorkspaceChangedEventArgs(change));

    private (
        LogicalPath Path,
        Either<InteractionError, Option<BaseNode>> Entry)[] _ResolveInitialEntries(
            LogicalPath path)
    {
        var resolved = Host.ResolvePath(path);
        if (resolved.IsRight)
        {
            return ((ResolvedPath)resolved).ResolutionChain
                .Select(static occurrence =>
                    (occurrence.CanonicalPath, _ResolvedEntry(occurrence.Node)))
                .ToArray();
        }

        if (path.IsRoot)
        {
            return [(path, _BrokenEntry((InteractionError)resolved))];
        }

        var entries = new List<(
            LogicalPath Path,
            Either<InteractionError, Option<BaseNode>> Entry)>();
        var prefix = LogicalPath.Root;
        foreach (var segment in path.Segments)
        {
            prefix = prefix.Append(segment);
            var prefixResolution = Host.ResolvePath(prefix);
            if (prefixResolution.IsRight)
            {
                var resolvedPrefix = (ResolvedPath)prefixResolution;
                entries.Add((
                    resolvedPrefix.CanonicalPath,
                    _ResolvedEntry(resolvedPrefix.Node)));
            }
            else
            {
                entries.Add((prefix, _BrokenEntry((InteractionError)prefixResolution)));
            }
        }

        return [.. entries];
    }

    private static Either<InteractionError, Option<BaseNode>> _ResolvedEntry(BaseNode node) =>
        Right(Some(node));

    private static Either<InteractionError, Option<BaseNode>> _BrokenEntry(
        InteractionError error) =>
        Left(error);

    private InteractionError? _ValidateNewName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                "A navigator name cannot be empty or whitespace.");
        }

        return Navigators.Any(navigator =>
            StringComparer.Ordinal.Equals(navigator.Name, name))
            ? new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                $"A navigator named '{name}' already exists.")
            : null;
    }

    private static IPathSegmentSnapshot? _CreateSegmentSnapshot(
        ILogicalPathSegment segment) => segment switch
        {
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

        var path = LogicalPath.Root;
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
        string? navigatorName,
        string message) => new(
            code,
            navigatorIndex,
            navigatorName,
            new InteractionError(InteractionErrorCode.INVALID_INPUT, message));

    private Navigator? _FindNavigator(Guid navigatorId) =>
        Navigators.FirstOrDefault(navigator => navigator.Id == navigatorId);

    private NavigatorRemovedChange _RemoveNavigator(Navigator navigator)
    {
        var previousIndex = Navigators.IndexOf(navigator);
        Navigators.RemoveAt(previousIndex);
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
