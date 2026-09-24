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
        return Navigators.Any(navigator =>
            StringComparer.Ordinal.Equals(navigator.Name, name))
            ? new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                $"A navigator named '{name}' already exists.")
            : null;
    }

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

    private static Either<InteractionError, T> _Disposed<T>() =>
        Left(new InteractionError(
            InteractionErrorCode.DISPOSED,
            "The workspace has been disposed."));
}
