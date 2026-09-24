using System.Collections.ObjectModel;

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
    public InteractionResult<NavigatorAddedChange> AddNavigator(
        string name,
        string rootName)
    {
        if (_IsDisposed)
        {
            return _Disposed<NavigatorAddedChange>();
        }

        if (string.IsNullOrEmpty(rootName))
        {
            return InteractionResult.Failure<NavigatorAddedChange>(
                InteractionErrorCode.INVALID_INPUT,
                "A root name cannot be empty.");
        }

        return AddNavigator(name, LogicalPath.Root.Append(rootName));
    }

    /// <summary>Adds a navigator whose stack is reconstructed from an absolute path.</summary>
    public InteractionResult<NavigatorAddedChange> AddNavigator(
        string name,
        LogicalPath path)
    {
        var validation = _ValidateNewName<NavigatorAddedChange>(name);
        if (validation is not null)
        {
            return validation;
        }

        var entries = _ResolveInitialEntries(path);
        var navigator = new Navigator(this, Guid.NewGuid(), name, entries);
        var index = _Navigators.Count;
        _Navigators.Add(navigator);

        var change = new NavigatorAddedChange(navigator.Id, index, navigator.CurrentEntry);
        Publish(change);
        return InteractionResult.Success(change);
    }

    /// <summary>
    /// Captures a supplied occurrence's location and creates a fresh navigator-owned stack there.
    /// </summary>
    public InteractionResult<NavigatorAddedChange> AddNavigator(
        string name,
        ResolvedNode resolvedNode)
    {
        if (_IsDisposed)
        {
            return _Disposed<NavigatorAddedChange>();
        }

        if (!ReferenceEquals(resolvedNode.Host, Host))
        {
            return InteractionResult.Failure<NavigatorAddedChange>(
                InteractionErrorCode.INVALID_INPUT,
                "The resolved node belongs to another host.");
        }

        return AddNavigator(name, resolvedNode.CanonicalPath);
    }

    /// <summary>Duplicates a navigator at its current path with fresh entry and node instances.</summary>
    public InteractionResult<NavigatorDuplicatedChange> DuplicateNavigator(
        Guid sourceNavigatorId,
        string name)
    {
        var validation = _ValidateNewName<NavigatorDuplicatedChange>(name);
        if (validation is not null)
        {
            return validation;
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
        return InteractionResult.Success(change);
    }

    /// <summary>Moves a navigator to a zero-based position in the workspace.</summary>
    public InteractionResult<NavigatorReorderedChange> ReorderNavigator(
        Guid navigatorId,
        int index)
    {
        if (_IsDisposed)
        {
            return _Disposed<NavigatorReorderedChange>();
        }

        if (index < 0 || index >= _Navigators.Count)
        {
            return InteractionResult.Failure<NavigatorReorderedChange>(
                InteractionErrorCode.INVALID_INPUT,
                $"Navigator index {index} is outside the workspace.");
        }

        var navigator = _FindNavigator(navigatorId);
        if (navigator is null)
        {
            return _NavigatorNotFound<NavigatorReorderedChange>(navigatorId);
        }

        var previousIndex = _Navigators.IndexOf(navigator);
        if (previousIndex == index)
        {
            return InteractionResult.Failure<NavigatorReorderedChange>(
                InteractionErrorCode.INVALID_INPUT,
                "The navigator is already at the requested index.");
        }

        _Navigators.RemoveAt(previousIndex);
        _Navigators.Insert(index, navigator);
        var change = new NavigatorReorderedChange(navigator.Id, previousIndex, index);
        Publish(change);
        return InteractionResult.Success(change);
    }

    /// <summary>Removes a navigator and all of its entries from this workspace.</summary>
    public InteractionResult<NavigatorRemovedChange> RemoveNavigator(Guid navigatorId)
    {
        if (_IsDisposed)
        {
            return _Disposed<NavigatorRemovedChange>();
        }

        var navigator = _FindNavigator(navigatorId);
        return navigator is null
            ? _NavigatorNotFound<NavigatorRemovedChange>(navigatorId)
            : InteractionResult.Success(_RemoveNavigator(navigator));
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
        if (resolved.IsSuccess)
        {
            return resolved.Value.ResolutionChain
                .Select(static occurrence =>
                    new NavigationEntry(occurrence.CanonicalPath, occurrence.Node))
                .ToArray();
        }

        if (path.IsRoot)
        {
            return [new NavigationEntry(path, resolved.Error!)];
        }

        var entries = new List<NavigationEntry>();
        var prefix = LogicalPath.Root;
        foreach (var segment in path.Segments)
        {
            prefix = prefix.Append(segment);
            var prefixResolution = Host.ResolvePath(prefix);
            entries.Add(prefixResolution.IsSuccess
                ? new NavigationEntry(
                    prefixResolution.Value.CanonicalPath,
                    prefixResolution.Value.Node)
                : new NavigationEntry(prefix, prefixResolution.Error!));
        }

        return [.. entries];
    }

    private InteractionResult<T>? _ValidateNewName<T>(string name)
    {
        if (_IsDisposed)
        {
            return _Disposed<T>();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            return InteractionResult.Failure<T>(
                InteractionErrorCode.INVALID_INPUT,
                "A navigator name cannot be empty or whitespace.");
        }

        return _Navigators.Any(navigator =>
            StringComparer.Ordinal.Equals(navigator.Name, name))
            ? InteractionResult.Failure<T>(
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

    private static InteractionResult<T> _NavigatorNotFound<T>(Guid navigatorId) =>
        InteractionResult.Failure<T>(
            InteractionErrorCode.NOT_FOUND,
            $"Navigator '{navigatorId}' was not found.");

    private static InteractionResult<T> _Disposed<T>() => InteractionResult.Failure<T>(
        InteractionErrorCode.DISPOSED,
        "The workspace has been disposed.");
}
