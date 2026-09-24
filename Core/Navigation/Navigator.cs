using System.Collections.ObjectModel;
using LanguageExt;
using static LanguageExt.Prelude;

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
    public Either<InteractionError, NavigationPushedChange> Navigate(
        ILogicalPathSegment segment)
    {
        if (_IsRemoved)
        {
            return _Disposed<NavigationPushedChange>();
        }

        var current = _Entries[^1];
        if (!current.IsResolved)
        {
            return Left(new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                "A broken navigation entry has no child locations."));
        }

        if (current.Node!.IsTerminal)
        {
            return Left(new InteractionError(
                InteractionErrorCode.UNSUPPORTED,
                $"Node '{current.Node.Name}' is terminal and cannot be navigated further."));
        }

        LogicalPath target;
        try
        {
            target = current.Path.Append(segment);
        }
        catch (ArgumentException exception)
        {
            return Left(new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                exception.Message));
        }

        var resolved = _Workspace.Host.ResolvePath(target);
        var entry = resolved.IsRight
            ? new NavigationEntry(
                ((ResolvedPath)resolved).CanonicalPath,
                ((ResolvedPath)resolved).Node)
            : new NavigationEntry(target, (InteractionError)resolved);
        _Entries.Add(entry);

        var change = new NavigationPushedChange(Id, entry);
        _Workspace.Publish(change);
        return Right(change);
    }

    /// <summary>Permanently removes the current entry and reveals its parent entry.</summary>
    public Either<InteractionError, NavigationPoppedChange> GoBack()
    {
        if (_IsRemoved)
        {
            return _Disposed<NavigationPoppedChange>();
        }

        if (_Entries.Count == 1)
        {
            return Left(new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                "The navigator is already at its root entry."));
        }

        var removed = _Entries[^1];
        _Entries.RemoveAt(_Entries.Count - 1);
        var change = new NavigationPoppedChange(Id, removed, _Entries[^1]);
        _Workspace.Publish(change);
        return Right(change);
    }

    internal IReadOnlyList<NavigationEntry> Remove()
    {
        var entries = _Entries.ToArray();
        _Entries.Clear();
        _IsRemoved = true;
        return entries;
    }

    private static Either<InteractionError, T> _Disposed<T>() =>
        Left(new InteractionError(
            InteractionErrorCode.DISPOSED,
            "The navigator has been removed from its workspace."));
}
