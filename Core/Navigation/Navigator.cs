using System.Collections.ObjectModel;

namespace UIEngine.Core;

/// <summary>One independent, destructive navigation stack within a workspace.</summary>
public sealed class Navigator
{
    private readonly UIEngineWorkspace _Workspace;
    private readonly List<NavigationEntry> _Entries;
    private readonly ReadOnlyCollection<NavigationEntry> _EntryView;
    private bool _IsRemoved;

    internal Navigator(
        UIEngineWorkspace workspace,
        Guid id,
        string name,
        IReadOnlyList<NavigationEntry> entries)
    {
        _Workspace = workspace;
        Id = id;
        Name = name;
        _Entries = [.. entries];
        _EntryView = _Entries.AsReadOnly();
    }

    public Guid Id { get; }

    public string Name { get; }

    public IReadOnlyList<NavigationEntry> Entries => _EntryView;

    public NavigationEntry CurrentEntry
    {
        get
        {
            ObjectDisposedException.ThrowIf(_IsRemoved, this);
            return _Entries[^1];
        }
    }

    /// <summary>Resolves one child location and makes it the current entry.</summary>
    /// <remarks>
    /// A resolution failure is committed as a broken entry so that <see cref="GoBack"/> can
    /// return to the previous location.
    /// </remarks>
    public InteractionResult<NavigationPushedChange> Navigate(
        ILogicalPathSegment segment)
    {
        if (_IsRemoved)
        {
            return _Disposed<NavigationPushedChange>();
        }

        var current = _Entries[^1];
        if (!current.IsResolved)
        {
            return InteractionResult.Failure<NavigationPushedChange>(
                InteractionErrorCode.INVALID_INPUT,
                "A broken navigation entry has no child locations.");
        }

        if (current.Node!.IsTerminal)
        {
            return InteractionResult.Failure<NavigationPushedChange>(
                InteractionErrorCode.UNSUPPORTED,
                $"Node '{current.Node.Name}' is terminal and cannot be navigated further.");
        }

        LogicalPath target;
        try
        {
            target = current.Path.Append(segment);
        }
        catch (ArgumentException exception)
        {
            return InteractionResult.Failure<NavigationPushedChange>(
                InteractionErrorCode.INVALID_INPUT,
                exception.Message);
        }

        var resolved = _Workspace.Host.ResolvePath(target);
        var entry = resolved.IsSuccess
            ? new NavigationEntry(resolved.Value.CanonicalPath, resolved.Value.Node)
            : new NavigationEntry(target, resolved.Error!);
        _Entries.Add(entry);

        var change = new NavigationPushedChange(Id, entry);
        _Workspace.Publish(change);
        return InteractionResult.Success(change);
    }

    /// <summary>Permanently removes the current entry and reveals its parent entry.</summary>
    public InteractionResult<NavigationPoppedChange> GoBack()
    {
        if (_IsRemoved)
        {
            return _Disposed<NavigationPoppedChange>();
        }

        if (_Entries.Count == 1)
        {
            return InteractionResult.Failure<NavigationPoppedChange>(
                InteractionErrorCode.INVALID_INPUT,
                "The navigator is already at its root entry.");
        }

        var removed = _Entries[^1];
        _Entries.RemoveAt(_Entries.Count - 1);
        var change = new NavigationPoppedChange(Id, removed, _Entries[^1]);
        _Workspace.Publish(change);
        return InteractionResult.Success(change);
    }

    internal IReadOnlyList<NavigationEntry> Remove()
    {
        var entries = _Entries.ToArray();
        _Entries.Clear();
        _IsRemoved = true;
        return entries;
    }

    private static InteractionResult<T> _Disposed<T>() => InteractionResult.Failure<T>(
        InteractionErrorCode.DISPOSED,
        "The navigator has been removed from its workspace.");
}
