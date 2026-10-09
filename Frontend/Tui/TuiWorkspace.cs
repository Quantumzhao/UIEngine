using LanguageExt;
using UIEngine.Core;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;

namespace UIEngine.Frontend.Tui;

/// <summary>A disposable TUI presentation that can be embedded in a XenoAtom visual tree.</summary>
public sealed class TuiWorkspace : IDisposable
{
    private readonly VStack _NavigatorVisuals;
    private readonly Dictionary<string, NavigatorPresentation> _PresentationsByName = [];
    private readonly OrderedDictionary<Guid, NavigatorPresentation> _PresentationsById = [];

    internal TuiWorkspace(
        UIEngineWorkspace workspace,
        TuiFrontendOptions options)
    {
        Workspace = workspace;
        Options = options;
        _NavigatorVisuals = new VStack();
        Visual = new VStack(
            new TextBlock(options.ApplicationTitle),
            _NavigatorVisuals);

        foreach (var navigator in workspace.Navigators)
        {
            _AddPresentation(
                navigator,
                _PresentationsById.Count,
                NavigatorPresentationConfiguration.Default(_PresentationsById.Count),
                navigator.CurrentEntry);
        }

        _SetSelectedNavigator(
            workspace.Navigators.Count > 0 ? workspace.Navigators[0].Name : null);
        workspace.Changed += _OnWorkspaceChanged;
        try
        {
            _ApplyStartup(options.Startup);
        }
        catch
        {
            workspace.Changed -= _OnWorkspaceChanged;
            _DisposePresentations();
            throw;
        }
    }

    /// <summary>Gets the immutable configuration snapshot used by this presentation.</summary>
    public TuiFrontendOptions Options { get; }

    /// <summary>Gets the Core workspace presented by this instance.</summary>
    public UIEngineWorkspace Workspace { get; }

    /// <summary>Gets the root visual to compose into a XenoAtom application.</summary>
    public Visual Visual { get; }

    /// <summary>Gets the navigator currently selected by the TUI presentation.</summary>
    public string? SelectedNavigatorName { get; private set; }

    internal IReadOnlyCollection<NavigatorPresentation> Presentations =>
        _PresentationsById.Values;

    /// <summary>Creates a serializable snapshot of Core and presentation state.</summary>
    public Either<InteractionError, TuiLayoutSnapshot> CreateLayoutSnapshot()
    {
        var selectedName = Workspace.Navigators.Any(navigator =>
            StringComparer.Ordinal.Equals(navigator.Name, SelectedNavigatorName))
            ? SelectedNavigatorName
            : Workspace.Navigators.Count > 0 ? Workspace.Navigators[0].Name : null;
        var coreSnapshot = Workspace.CreateSnapshot(selectedName);
        if (!coreSnapshot.IsRight)
        {
            return LanguageExt.Prelude.Left<InteractionError, TuiLayoutSnapshot>(
                (InteractionError)coreSnapshot);
        }

        var configurations = _PresentationsById.Values.ToDictionary(
            static presentation => presentation.Navigator.Name,
            static presentation => presentation.Configuration,
            StringComparer.Ordinal);
        return LanguageExt.Prelude.Right<InteractionError, TuiLayoutSnapshot>(new(
            (WorkspaceSnapshot)coreSnapshot,
            configurations));
    }

    /// <summary>Replaces the Core workspace and applies saved presentation configuration.</summary>
    public WorkspaceRestoreResult RestoreLayout(TuiLayoutSnapshot layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var copiedLayout = layout.Copy();
        var result = Workspace.RestoreSnapshot(copiedLayout.Workspace);
        for (var index = 0; index < _PresentationsById.Count; index++)
        {
            var presentation = _PresentationsById.GetAt(index).Value;
            var configuration = _GetConfiguration(
                copiedLayout.Navigators,
                presentation.Navigator.Name,
                index);
            presentation.ApplyConfiguration(configuration);
        }

        _SetSelectedNavigator(result.SelectedNavigatorName);
        return result;
    }

    /// <summary>Disposes frontend resources and the Core workspace.</summary>
    public void Dispose()
    {
        Workspace.Changed -= _OnWorkspaceChanged;
        _DisposePresentations();
        Workspace.Dispose();
    }

    internal void SetPresentationConfiguration(
        string navigatorName,
        NavigatorPresentationConfiguration configuration)
    {
        if (!configuration.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(configuration));
        }

        if (!_PresentationsByName.TryGetValue(navigatorName, out var presentation))
        {
            throw new ArgumentException(
                $"Navigator '{navigatorName}' was not found.",
                nameof(navigatorName));
        }

        presentation.ApplyConfiguration(configuration);
    }

    private void _ApplyStartup(TuiStartupConfiguration startup)
    {
        switch (startup)
        {
            case PresentWorkspace:
                return;
            case AddNavigator add:
                var added = Workspace.AddNavigator(add.Name, add.Path);
                if (!added.IsRight)
                {
                    throw new ArgumentException(
                        ((InteractionError)added).Message,
                        nameof(startup));
                }
                return;
            case RestoreLayout restore:
                RestoreLayout(restore.Layout);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(startup));
        }
    }

    private void _OnWorkspaceChanged(object? sender, WorkspaceChangedEventArgs eventArgs)
    {
        Navigator? addedNavigator = eventArgs.Change is NavigatorAddedChange or NavigatorDuplicatedChange
            ? Workspace.Navigators.FirstOrDefault(navigator =>
                navigator.Id == eventArgs.Change.NavigatorId)
            : null;
        _ApplyWorkspaceChange(eventArgs.Change, addedNavigator);
    }

    private void _ApplyWorkspaceChange(WorkspaceChange change, Navigator? addedNavigator)
    {
        switch (change)
        {
            case NavigatorAddedChange added:
                _AddPresentation(
                    addedNavigator ?? throw new InvalidOperationException(
                        $"Navigator '{added.NavigatorId}' was not available for its add notification."),
                    added.Index,
                    NavigatorPresentationConfiguration.Default(added.Index),
                    added.InitialEntry);
                break;
            case NavigatorDuplicatedChange duplicated:
                _AddPresentation(
                    addedNavigator ?? throw new InvalidOperationException(
                        $"Navigator '{duplicated.NavigatorId}' was not available for its duplicate notification."),
                    duplicated.Index,
                    NavigatorPresentationConfiguration.Default(duplicated.Index),
                    duplicated.InitialEntry);
                break;
            case NavigationPushedChange pushed:
                _GetActivePresentation(pushed.NavigatorId).ReplaceControl(pushed.Entry);
                break;
            case NavigationPoppedChange popped:
                _GetActivePresentation(popped.NavigatorId).ReplaceControl(popped.CurrentEntry);
                break;
            case NavigatorReorderedChange reordered:
                _ReorderPresentation(reordered);
                break;
            case NavigatorRemovedChange removed:
                _RemovePresentation(removed.NavigatorId, removed.PreviousIndex);
                break;
            default:
                throw new NotSupportedException(
                    $"Workspace change '{change.GetType().FullName}' is not supported.");
        }

        _AssertIndexes();
    }

    private void _AddPresentation(
        Navigator navigator,
        int index,
        NavigatorPresentationConfiguration configuration,
        Either<InteractionError, Option<BaseNode>> entry)
    {
        if (_PresentationsById.ContainsKey(navigator.Id) ||
            _PresentationsByName.ContainsKey(navigator.Name))
        {
            throw new InvalidOperationException(
                $"Navigator '{navigator.Name}' already has a presentation.");
        }

        var presentation = new NavigatorPresentation(
            navigator,
            configuration,
            entry);
        _PresentationsById.Insert(index, navigator.Id, presentation);
        _PresentationsByName.Add(navigator.Name, presentation);
        _NavigatorVisuals.Children.Insert(index, presentation.Container);
        if (SelectedNavigatorName is null)
        {
            _SetSelectedNavigator(navigator.Name);
        }
    }

    private NavigatorPresentation _GetActivePresentation(Guid navigatorId)
    {
        if (_PresentationsById.TryGetValue(navigatorId, out var presentation))
        {
            return presentation;
        }

        throw new InvalidOperationException(
            $"Navigator '{navigatorId}' has no presentation.");
    }

    private void _ReorderPresentation(NavigatorReorderedChange change)
    {
        var presentation = _GetActivePresentation(change.NavigatorId);
        if (!ReferenceEquals(
            _PresentationsById.GetAt(change.PreviousIndex).Value,
            presentation))
        {
            throw new InvalidOperationException("The presentation order does not match Core.");
        }

        _PresentationsById.RemoveAt(change.PreviousIndex);
        _PresentationsById.Insert(change.CurrentIndex, change.NavigatorId, presentation);
        _NavigatorVisuals.Children.RemoveAt(change.PreviousIndex);
        _NavigatorVisuals.Children.Insert(change.CurrentIndex, presentation.Container);
    }

    private void _RemovePresentation(Guid navigatorId, int previousIndex)
    {
        var presentation = _GetActivePresentation(navigatorId);
        if (!ReferenceEquals(_PresentationsById.GetAt(previousIndex).Value, presentation))
        {
            throw new InvalidOperationException("The presentation order does not match Core.");
        }

        _PresentationsById.RemoveAt(previousIndex);
        _PresentationsByName.Remove(presentation.Navigator.Name);
        _NavigatorVisuals.Children.RemoveAt(previousIndex);
        presentation.Dispose();
        if (StringComparer.Ordinal.Equals(
            SelectedNavigatorName,
            presentation.Navigator.Name))
        {
            _SetSelectedNavigator(
                _PresentationsById.Count > 0
                    ? _PresentationsById.GetAt(0).Value.Navigator.Name
                    : null);
        }
    }

    private void _SetSelectedNavigator(string? navigatorName)
    {
        SelectedNavigatorName = navigatorName;
        foreach (var presentation in _PresentationsById.Values)
        {
            presentation.IsSelected = StringComparer.Ordinal.Equals(
                presentation.Navigator.Name,
                navigatorName);
        }
    }

    private void _DisposePresentations()
    {
        foreach (var presentation in _PresentationsById.Values)
        {
            presentation.Dispose();
        }

        _NavigatorVisuals.Children.Clear();
        _PresentationsById.Clear();
        _PresentationsByName.Clear();
        SelectedNavigatorName = null;
    }

    private void _AssertIndexes()
    {
        if (_PresentationsById.Count != _PresentationsByName.Count ||
            _PresentationsById.Values.Any(presentation =>
                !_PresentationsById.TryGetValue(presentation.Navigator.Id, out var byId) ||
                !ReferenceEquals(presentation, byId) ||
                !_PresentationsByName.TryGetValue(presentation.Navigator.Name, out var byName) ||
                !ReferenceEquals(presentation, byName)))
        {
            throw new InvalidOperationException("Navigator presentation indexes are inconsistent.");
        }
    }

    private static NavigatorPresentationConfiguration _GetConfiguration(
        IReadOnlyDictionary<string, NavigatorPresentationConfiguration>? configurations,
        string navigatorName,
        int index) =>
        configurations is not null &&
        configurations.TryGetValue(navigatorName, out var configuration) &&
        configuration is not null &&
        configuration.IsValid
            ? configuration
            : NavigatorPresentationConfiguration.Default(index);
}
