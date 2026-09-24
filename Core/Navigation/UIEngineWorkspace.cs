using System.Collections.ObjectModel;
using LanguageExt;
using static LanguageExt.Prelude;

namespace UIEngine.Core;

/// <summary>
/// Owns an ordered set of independent navigators over one caller-owned host.
/// </summary>
public sealed class UIEngineWorkspace : IDisposable
{
    private readonly List<Navigator> _Navigators = [];
    private readonly ReadOnlyCollection<Navigator> _NavigatorView;
    private bool _IsDisposed;

    public UIEngineWorkspace(UIEngineHost host)
    {
        Host = host;
        _NavigatorView = _Navigators.AsReadOnly();
    }

    public IReadOnlyList<Navigator> Navigators => _NavigatorView;

    public event EventHandler<WorkspaceChangedEventArgs>? Changed;

    internal UIEngineHost Host { get; }

    /// <summary>Adds a navigator whose initial location is a registered root.</summary>
    public Either<InteractionError, NavigatorAddedChange> AddNavigator(
        string name,
        string rootName)
    {
        if (_IsDisposed)
        {
            return _Disposed<NavigatorAddedChange>();
        }

        if (string.IsNullOrEmpty(rootName))
        {
            return Left(new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                "A root name cannot be empty."));
        }

        return AddNavigator(name, LogicalPath.Root.Append(rootName));
    }

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
        var index = _Navigators.Count;
        _Navigators.Add(navigator);

        var change = new NavigatorAddedChange(navigator.Id, index, navigator.CurrentEntry);
        Publish(change);
        return Right(change);
    }

    /// <summary>
    /// Captures a supplied occurrence's location and creates a fresh navigator-owned stack there.
    /// </summary>
    public Either<InteractionError, NavigatorAddedChange> AddNavigator(
        string name,
        ResolvedNode resolvedNode)
    {
        if (_IsDisposed)
        {
            return _Disposed<NavigatorAddedChange>();
        }

        if (!ReferenceEquals(resolvedNode.Host, Host))
        {
            return Left(new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                "The resolved node belongs to another host."));
        }

        return AddNavigator(name, resolvedNode.CanonicalPath);
    }

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

        var entries = _ResolveInitialEntries(source.CurrentEntry.Path);
        var navigator = new Navigator(this, Guid.NewGuid(), name, entries);
        var index = _Navigators.IndexOf(source) + 1;
        _Navigators.Insert(index, navigator);

        var change = new NavigatorDuplicatedChange(
            source.Id,
            navigator.Id,
            index,
            navigator.CurrentEntry);
        Publish(change);
        return Right(change);
    }

    /// <summary>Moves a navigator to a zero-based position in the workspace.</summary>
    public Either<InteractionError, NavigatorReorderedChange> ReorderNavigator(
        Guid navigatorId,
        int index)
    {
        if (_IsDisposed)
        {
            return _Disposed<NavigatorReorderedChange>();
        }

        if (index < 0 || index >= _Navigators.Count)
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
        if (_IsDisposed)
        {
            return _Disposed<NavigatorRemovedChange>();
        }

        var navigator = _FindNavigator(navigatorId);
        return navigator is null
            ? _NavigatorNotFound<NavigatorRemovedChange>(navigatorId)
            : Right(_RemoveNavigator(navigator));
    }

    public void Dispose()
    {
        if (_IsDisposed)
        {
            return;
        }

        _IsDisposed = true;
        while (_Navigators.Count > 0)
        {
            _RemoveNavigator(_Navigators[^1]);
        }
    }

    internal void Publish(WorkspaceChange change) =>
        Changed?.Invoke(this, new WorkspaceChangedEventArgs(change));

    private NavigationEntry[] _ResolveInitialEntries(LogicalPath path)
    {
        var resolved = Host.ResolvePath(path);
        if (resolved.IsRight)
        {
            return ((ResolvedPath)resolved).ResolutionChain
                .Select(static occurrence =>
                    new NavigationEntry(occurrence.CanonicalPath, occurrence.Node))
                .ToArray();
        }

        if (path.IsRoot)
        {
            return [new NavigationEntry(path, (InteractionError)resolved)];
        }

        var entries = new List<NavigationEntry>();
        var prefix = LogicalPath.Root;
        foreach (var segment in path.Segments)
        {
            prefix = prefix.Append(segment);
            var prefixResolution = Host.ResolvePath(prefix);
            entries.Add(prefixResolution.IsRight
                ? new NavigationEntry(
                    ((ResolvedPath)prefixResolution).CanonicalPath,
                    ((ResolvedPath)prefixResolution).Node)
                : new NavigationEntry(prefix, (InteractionError)prefixResolution));
        }

        return [.. entries];
    }

    private InteractionError? _ValidateNewName(string name)
    {
        if (_IsDisposed)
        {
            return new InteractionError(
                InteractionErrorCode.DISPOSED,
                "The workspace has been disposed.");
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                "A navigator name cannot be empty or whitespace.");
        }

        return _Navigators.Any(navigator =>
            StringComparer.Ordinal.Equals(navigator.Name, name))
            ? new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                $"A navigator named '{name}' already exists.")
            : null;
    }

    private Navigator? _FindNavigator(Guid navigatorId) =>
        _Navigators.FirstOrDefault(navigator => navigator.Id == navigatorId);

    private NavigatorRemovedChange _RemoveNavigator(Navigator navigator)
    {
        var previousIndex = _Navigators.IndexOf(navigator);
        _Navigators.RemoveAt(previousIndex);
        var removedEntries = navigator.Remove();
        var change = new NavigatorRemovedChange(
            navigator.Id,
            previousIndex,
            removedEntries);
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
