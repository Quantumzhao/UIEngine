using LanguageExt;
using UIEngine.Core;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Styling;

namespace UIEngine.Frontend.Tui;

/// <summary>A disposable TUI presentation that can be embedded in a XenoAtom visual tree.</summary>
public sealed partial class TuiWorkspace : IDisposable
{
    private readonly OrderedDictionary<Guid, NavigatorPresentation> _Presentations = [];
    private readonly Grid _NavigatorGrid = new() { RowGap = 1, ColumnGap = 1 };
    private readonly ScrollViewer _NavigatorScroll;
    private readonly TextBlock _Address = new("No navigator");
    private readonly TextBlock _State = new("Empty");
    private readonly Button _NewButton = new("New");
    private readonly Button _UpButton = new("Up");
    private readonly Button _DuplicateButton = new("Duplicate");
    private readonly Button _CloseButton = new("Close");
    private readonly Button _RestoreButton = new("Restore Closed");
    private readonly VStack _RootVisual;
    private NavigatorPresentation? _SuspendedPresentation;
    private int _SuspendedPresentationIndex;

    internal TuiWorkspace(
        UIEngineWorkspace workspace,
        TuiFrontendOptions options)
    {
        Workspace = workspace;
        Options = options;
        _NavigatorScroll = new ScrollViewer(_NavigatorGrid, false)
        {
            HorizontalScrollEnabled = true,
            VerticalScrollEnabled = true,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };

        var actions = new HStack(
            _NewButton,
            _UpButton,
            _DuplicateButton,
            _CloseButton,
            _RestoreButton,
            new TextBlock("Path:"),
            _Address,
            new TextBlock("State:"),
            _State)
        {
            Spacing = 1,
        };
        _RootVisual = new VStack(
            new TextBlock(options.ApplicationTitle),
            actions,
            _NavigatorScroll,
            new CommandBar(CommandPresentation.CommandBar));
        Visual = _RootVisual;

        _NewButton.ClickRouted += (_, _) => _ShowRootDialog();
        _UpButton.ClickRouted += (_, _) => _GoBack();
        _DuplicateButton.ClickRouted += (_, _) => _DuplicateSelected();
        _CloseButton.ClickRouted += (_, _) => _SuspendSelected();
        _RestoreButton.ClickRouted += (_, _) => _RestoreSuspended();
        _RegisterCommands();

        foreach (var navigator in workspace.Navigators)
        {
            _AddPresentation(
                navigator,
                _Presentations.Count,
                _FindFreeConfiguration(),
                navigator.CurrentEntry);
        }

        _SetSelectedNavigator(workspace.Navigators.Count > 0
            ? workspace.Navigators[0].Id
            : null);
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
    public Guid? SelectedNavigatorId { get; private set; }

    /// <summary>Raised when the reusable workspace asks its host to close.</summary>
    public event EventHandler? CloseRequested;

    internal IReadOnlyCollection<NavigatorPresentation> Presentations => _Presentations.Values;
    internal NavigatorPresentation? SuspendedPresentation => _SuspendedPresentation;
    internal ScrollViewer NavigatorScroll => _NavigatorScroll;
    internal string AddressText => _Address.Text ?? string.Empty;
    internal string StateText => _State.Text ?? string.Empty;

    /// <summary>Creates a serializable snapshot of active Core and presentation state.</summary>
    public Either<InteractionError, TuiLayoutSnapshot> CreateLayoutSnapshot()
    {
        var selectedId = _Presentations.ContainsKey(SelectedNavigatorId ?? Guid.Empty)
            ? SelectedNavigatorId
            : _Presentations.Count > 0 ? _Presentations.GetAt(0).Key : null;
        var coreSelection = selectedId ?? _SuspendedPresentation?.Navigator.Id;
        var coreSnapshot = Workspace.CreateSnapshot(coreSelection);
        if (!coreSnapshot.IsRight)
        {
            return LanguageExt.Prelude.Left<InteractionError, TuiLayoutSnapshot>(
                (InteractionError)coreSnapshot);
        }

        var suspendedId = _SuspendedPresentation?.Navigator.Id;
        var activeSnapshot = new WorkspaceSnapshot(
            ((WorkspaceSnapshot)coreSnapshot).Navigators
                .Where(navigator => navigator.Id != suspendedId)
                .ToArray(),
            selectedId);
        var configurations = _Presentations.Values.ToDictionary(
            static presentation => presentation.Navigator.Id,
            static presentation => presentation.Configuration);
        return LanguageExt.Prelude.Right<InteractionError, TuiLayoutSnapshot>(new(
            activeSnapshot,
            configurations));
    }

    /// <summary>Replaces the active Core workspace and applies saved presentation configuration.</summary>
    public WorkspaceRestoreResult RestoreLayout(TuiLayoutSnapshot layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var copiedLayout = layout.Copy();
        var result = Workspace.RestoreSnapshot(copiedLayout.Workspace);
        var occupied = new System.Collections.Generic.HashSet<(int Row, int Column)>();
        foreach (var presentation in _Presentations.Values)
        {
            var requested = copiedLayout.Navigators is not null &&
                copiedLayout.Navigators.TryGetValue(presentation.Navigator.Id, out var saved) &&
                saved is not null && saved.IsValid
                    ? saved
                    : _FindFreeConfiguration(occupied);
            var configuration = occupied.Add((requested.Row, requested.Column))
                ? requested
                : _FindFreeConfiguration(occupied, requested.Width, requested.Height);
            occupied.Add((configuration.Row, configuration.Column));
            presentation.ApplyConfiguration(configuration);
        }

        _RebuildGrid();
        _SetSelectedNavigator(result.SelectedNavigatorId);
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
        Guid navigatorId,
        NavigatorPresentationConfiguration configuration)
    {
        if (!configuration.IsValid)
        {
            throw new ArgumentOutOfRangeException(nameof(configuration));
        }

        var presentation = _GetActivePresentation(navigatorId);
        presentation.ApplyConfiguration(configuration);
        _NormalizeConfigurations();
        _RebuildGrid();
    }

    private void _ApplyStartup(TuiStartupConfiguration startup)
    {
        switch (startup)
        {
            case PresentWorkspace:
                return;
            case AddNavigator add:
                Workspace.AddNavigator(add.Path);
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
        var addedNavigator = eventArgs.Change is NavigatorAddedChange or
            NavigatorDuplicatedChange
            ? Workspace.Navigators.FirstOrDefault(navigator =>
                navigator.Id == eventArgs.Change.NavigatorId)
            : null;
        _ApplyWorkspaceChange(eventArgs.Change, addedNavigator);
    }

    private void _ApplyWorkspaceChange(WorkspaceChange change, Navigator? changedNavigator)
    {
        switch (change)
        {
            case NavigatorAddedChange added:
                _AddPresentation(
                    changedNavigator ?? throw _MissingNavigator(added.NavigatorId),
                    _ActiveIndexForCoreNavigator(added.NavigatorId),
                    _FindFreeConfiguration(),
                    added.InitialEntry);
                break;
            case NavigatorDuplicatedChange duplicated:
                var source = _GetPresentation(duplicated.SourceNavigatorId);
                _AddPresentation(
                    changedNavigator ?? throw _MissingNavigator(duplicated.NavigatorId),
                    _ActiveIndexForCoreNavigator(duplicated.NavigatorId),
                    _FindFreeConfiguration(
                        null,
                        source.Configuration.Width,
                        source.Configuration.Height,
                        source.Configuration.Row,
                        source.Configuration.Column + 1),
                    duplicated.InitialEntry);
                break;
            case NavigationPushedChange pushed:
                _GetPresentation(pushed.NavigatorId).ReplaceControl(pushed.Entry);
                break;
            case NavigationPoppedChange popped:
                _GetPresentation(popped.NavigatorId).ReplaceControl(popped.CurrentEntry);
                break;
            case NavigatorReorderedChange reordered:
                _MovePresentation(reordered.NavigatorId);
                break;
            case NavigatorRemovedChange removed:
                _RemovePresentation(removed.NavigatorId);
                break;
            default:
                throw new NotSupportedException(
                    $"Workspace change '{change.GetType().FullName}' is not supported.");
        }

        _RebuildGrid();
        _UpdateChrome();
    }

    private void _AddPresentation(
        Navigator navigator,
        int index,
        NavigatorPresentationConfiguration configuration,
        Either<InteractionError, Option<BaseNode>> entry)
    {
        if (_Presentations.ContainsKey(navigator.Id))
        {
            throw new InvalidOperationException(
                $"Navigator '{navigator.Id}' already has a presentation.");
        }

        var presentation = new NavigatorPresentation(
            navigator,
            configuration,
            entry,
            _SelectAndFocus);
        _Presentations.Insert(index, navigator.Id, presentation);
        if (SelectedNavigatorId is null)
        {
            SelectedNavigatorId = navigator.Id;
            presentation.IsSelected = true;
        }

        _RebuildGrid();
        _UpdateChrome();
    }

    private void _MovePresentation(Guid id)
    {
        if (_SuspendedPresentation?.Navigator.Id == id)
        {
            return;
        }

        var presentation = _GetActivePresentation(id);
        var previousIndex = _Presentations.IndexOf(id);
        var currentIndex = _ActiveIndexForCoreNavigator(id);
        _Presentations.RemoveAt(previousIndex);
        _Presentations.Insert(currentIndex, id, presentation);
    }

    private void _RemovePresentation(Guid id)
    {
        if (_SuspendedPresentation?.Navigator.Id == id)
        {
            _SuspendedPresentation.Dispose();
            _SuspendedPresentation = null;
            return;
        }

        var presentation = _GetActivePresentation(id);
        var previousIndex = _Presentations.IndexOf(id);
        _Presentations.RemoveAt(previousIndex);
        presentation.Dispose();
        if (SelectedNavigatorId == id)
        {
            _SetSelectedNavigator(_SelectionAfterRemoval(previousIndex));
        }
    }

    private void _SuspendPresentation(Guid id, int previousIndex)
    {
        var presentation = _GetActivePresentation(id);
        presentation.RememberFocus();
        _Presentations.RemoveAt(previousIndex);
        _SuspendedPresentation = presentation;
        _SuspendedPresentationIndex = previousIndex;
        if (SelectedNavigatorId == id)
        {
            _SetSelectedNavigator(_SelectionAfterRemoval(previousIndex));
        }
    }

    private void _ResumePresentation()
    {
        if (_SuspendedPresentation is null)
        {
            return;
        }

        var presentation = _SuspendedPresentation;
        var id = presentation.Navigator.Id;
        var index = Math.Min(_SuspendedPresentationIndex, _Presentations.Count);
        var coreIndex = Workspace.Navigators.Index().Single(item => item.Item.Id == id).Index;
        if (coreIndex != index)
        {
            Workspace.ReorderNavigator(id, index);
        }

        _SuspendedPresentation = null;
        _Presentations.Insert(index, id, presentation);
        _SetSelectedNavigator(id);
        presentation.Focus();
        _RebuildGrid();
        _UpdateChrome();
    }

    private Guid? _SelectionAfterRemoval(int previousIndex) =>
        _Presentations.Count == 0
            ? null
            : _Presentations.GetAt(Math.Min(previousIndex, _Presentations.Count - 1)).Key;

    private NavigatorPresentation _GetActivePresentation(Guid navigatorId) =>
        _Presentations.TryGetValue(navigatorId, out var presentation)
            ? presentation
            : throw _MissingNavigator(navigatorId);

    private NavigatorPresentation _GetPresentation(Guid navigatorId) =>
        _SuspendedPresentation?.Navigator.Id == navigatorId
            ? _SuspendedPresentation
            : _GetActivePresentation(navigatorId);

    private int _ActiveIndexForCoreNavigator(Guid navigatorId)
    {
        var index = 0;
        foreach (var navigator in Workspace.Navigators)
        {
            if (navigator.Id == navigatorId)
            {
                return index;
            }

            if (_Presentations.ContainsKey(navigator.Id))
            {
                index++;
            }
        }

        throw _MissingNavigator(navigatorId);
    }

    private void _SetSelectedNavigator(Guid? navigatorId)
    {
        if (SelectedNavigatorId is Guid previousId &&
            previousId != navigatorId &&
            _Presentations.TryGetValue(previousId, out var previous))
        {
            previous.RememberFocus();
        }

        SelectedNavigatorId = navigatorId;
        foreach (var presentation in _Presentations.Values)
        {
            presentation.IsSelected = presentation.Navigator.Id == navigatorId;
        }

        _UpdateChrome();
        _ScrollSelectionIntoView();
    }

    private void _SelectAndFocus(Guid navigatorId)
    {
        _SetSelectedNavigator(navigatorId);
        _GetActivePresentation(navigatorId).Focus();
    }

    private void _UpdateChrome()
    {
        var selected = SelectedNavigatorId is Guid id && _Presentations.TryGetValue(id, out var value)
            ? value
            : null;
        var displayPath = selected?.Navigator.CurrentPath.ToDisplayString();
        _Address.Text = displayPath is null
            ? "No navigator"
            : displayPath.Value.IsRight ? (string)displayPath.Value : string.Empty;
        _State.Text = selected?.CurrentControl.State.ToString() ?? "Empty";
        _State.SetStyle(new TextBlockStyle { Foreground = selected?.CurrentControl.State switch
        {
            PlaceholderNodeState.Healthy => Colors.Green,
            PlaceholderNodeState.Invalid => Colors.Red,
            _ => Colors.Yellow,
        }});
        _UpButton.IsEnabled = selected is not null && selected.Navigator.Paths.Count > 1;
        _DuplicateButton.IsEnabled = selected is not null;
        _CloseButton.IsEnabled = selected is not null;
        _RestoreButton.IsEnabled = _SuspendedPresentation is not null;
    }

    private void _DisposePresentations()
    {
        foreach (var presentation in _Presentations.Values)
        {
            presentation.Dispose();
        }

        _SuspendedPresentation?.Dispose();
        _SuspendedPresentation = null;
        _Presentations.Clear();
        foreach (var cell in _NavigatorGrid.Cells)
        {
            cell.Content = null!;
        }
        _NavigatorGrid.Cells.Clear();
        SelectedNavigatorId = null;
    }

    private static InvalidOperationException _MissingNavigator(Guid id) => new(
        $"Navigator '{id}' has no active presentation.");
}
