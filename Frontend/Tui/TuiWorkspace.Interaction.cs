using UIEngine.Core;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Input;

namespace UIEngine.Frontend.Tui;

public sealed partial class TuiWorkspace
{
    private void _ShowRootDialog()
    {
        if (_RootVisual.App is null)
        {
            return;
        }

        var roots = Workspace.Host.Roots.Select(static root => root.Name).ToArray();
        var selection = new Select<string>(roots, roots.Length > 0 ? 0 : -1);
        var create = new Button("Create") { IsEnabled = roots.Length > 0 };
        var cancel = new Button("Cancel");
        var content = new VStack(
            roots.Length > 0 ? new TextBlock("Choose a registered root") : new TextBlock("No registered roots"),
            selection,
            new HStack(create, cancel) { Spacing = 1 });
        var dialog = new Dialog(new TextBlock("New Navigator"), content)
        {
            IsModal = true,
            Width = 42,
        };
        var previousFocus = _RootVisual.App.FocusedElement;
        void Close()
        {
            dialog.Close();
            if (previousFocus is not null)
            {
                _RootVisual.App?.Focus(previousFocus);
            }
        }

        void Create()
        {
            if (selection.SelectedIndex < 0 || selection.SelectedIndex >= roots.Length)
            {
                return;
            }

            var added = Workspace.AddNavigator(roots[selection.SelectedIndex]);
            if (added.IsRight)
            {
                dialog.Close();
                _SelectAndFocus(((NavigatorAddedChange)added).NavigatorId);
            }
        }

        create.ClickRouted += (_, _) => Create();
        cancel.ClickRouted += (_, _) => Close();
        dialog.AddKeyBinding(new KeyGesture(TerminalKey.Enter, TerminalModifiers.None), Create);
        dialog.AddKeyBinding(new KeyGesture(TerminalKey.Escape, TerminalModifiers.None), Close);
        dialog.Show();
        _RootVisual.App.Focus(roots.Length > 0 ? selection : cancel);
    }

    private void _ShowHelp()
    {
        if (_RootVisual.App is null)
        {
            return;
        }

        var close = new Button("Close");
        var dialog = new Dialog(
            new TextBlock("Keyboard Help"),
            new VStack(
                new TextBlock(
                    "Ctrl+N New   Ctrl+PgUp/PgDn Select   Ctrl+D Duplicate\n" +
                    "Ctrl+W Close   Ctrl+Shift+T Restore   Alt+Left Up\n" +
                    "F1 Help   Ctrl+Q Quit\n" +
                    "Enter Activate, F5 Refresh, Ctrl+S Save, Ctrl+O Load (not available yet)"),
                close))
        {
            IsModal = true,
            Width = 78,
        };
        var previousFocus = _RootVisual.App.FocusedElement;
        void Close()
        {
            dialog.Close();
            if (previousFocus is not null)
            {
                _RootVisual.App?.Focus(previousFocus);
            }
        }

        close.ClickRouted += (_, _) => Close();
        dialog.AddKeyBinding(new KeyGesture(TerminalKey.Escape, TerminalModifiers.None), Close);
        dialog.Show();
        _RootVisual.App.Focus(close);
    }

    private void _DuplicateSelected()
    {
        if (SelectedNavigatorId is Guid id)
        {
            var duplicated = Workspace.DuplicateNavigator(id);
            if (duplicated.IsRight)
            {
                _SelectAndFocus(((NavigatorDuplicatedChange)duplicated).NavigatorId);
            }
        }
    }

    private void _SuspendSelected()
    {
        if (SelectedNavigatorId is Guid id)
        {
            if (_SuspendedPresentation is not null)
            {
                var removed = Workspace.RemoveNavigator(_SuspendedPresentation.Navigator.Id);
                if (!removed.IsRight)
                {
                    return;
                }
            }

            var index = _Presentations.IndexOf(id);
            _SuspendPresentation(id, index);
            _RebuildGrid();
            _UpdateChrome();
        }
    }

    private void _RestoreSuspended() => _ResumePresentation();

    private void _GoBack()
    {
        if (SelectedNavigatorId is Guid id)
        {
            _GetActivePresentation(id).Navigator.GoBack();
        }
    }

    private void _SelectRelative(int delta)
    {
        if (_Presentations.Count == 0)
        {
            return;
        }

        var index = SelectedNavigatorId is Guid id && _Presentations.IndexOf(id) is var found && found >= 0
            ? found
            : 0;
        var next = (index + delta + _Presentations.Count) % _Presentations.Count;
        _SelectAndFocus(_Presentations.GetAt(next).Key);
    }

    private void _RegisterCommands()
    {
        _AddCommand(TuiCommandIds.New, "New", new('n', TerminalModifiers.Ctrl), _ => _ShowRootDialog());
        _AddCommand(TuiCommandIds.Previous, "Previous", new(TerminalKey.PageUp, TerminalModifiers.Ctrl), _ => _SelectRelative(-1));
        _AddCommand(TuiCommandIds.Next, "Next", new(TerminalKey.PageDown, TerminalModifiers.Ctrl), _ => _SelectRelative(1));
        _AddCommand(TuiCommandIds.Duplicate, "Duplicate", new('d', TerminalModifiers.Ctrl), _ => _DuplicateSelected(), _ => SelectedNavigatorId is not null);
        _AddCommand(TuiCommandIds.Suspend, "Close", new('w', TerminalModifiers.Ctrl), _ => _SuspendSelected(), _ => SelectedNavigatorId is not null);
        _AddCommand(TuiCommandIds.Restore, "Restore", new('t', TerminalModifiers.Ctrl | TerminalModifiers.Shift), _ => _RestoreSuspended(), _ => _SuspendedPresentation is not null);
        _AddCommand(TuiCommandIds.Up, "Up", new(TerminalKey.Left, TerminalModifiers.Alt), _ => _GoBack(), _ => SelectedNavigatorId is Guid id && _GetActivePresentation(id).Navigator.Paths.Count > 1);
        _AddCommand(TuiCommandIds.Help, "Help", new(TerminalKey.F1, TerminalModifiers.None), _ => _ShowHelp());
        _AddCommand(TuiCommandIds.Close, "Quit", new('q', TerminalModifiers.Ctrl), _ => CloseRequested?.Invoke(this, EventArgs.Empty));
        _AddDeferredCommand(TuiCommandIds.Activate, "Activate", new(TerminalKey.Enter, TerminalModifiers.None));
        _AddDeferredCommand(TuiCommandIds.Refresh, "Refresh", new(TerminalKey.F5, TerminalModifiers.None));
        _AddDeferredCommand(TuiCommandIds.Save, "Save", new('s', TerminalModifiers.Ctrl));
        _AddDeferredCommand(TuiCommandIds.Load, "Load", new('o', TerminalModifiers.Ctrl));
    }

    private void _AddCommand(
        string id,
        string label,
        KeyGesture gesture,
        Action<Visual> execute,
        Func<Visual, bool>? canExecute = null) =>
        _RootVisual.AddCommand(new Command
        {
            Id = id,
            Name = id,
            LabelMarkup = label,
            Gesture = gesture,
            Execute = execute,
            CanExecute = canExecute,
            Presentation = CommandPresentation.CommandBar | CommandPresentation.CommandPalette,
        });

    private void _AddDeferredCommand(string id, string label, KeyGesture gesture) =>
        _RootVisual.AddCommand(new Command
        {
            Id = id,
            Name = id,
            LabelMarkup = label,
            DescriptionMarkup = "Not available yet",
            Gesture = gesture,
            Execute = _ => { },
            CanExecute = _ => false,
            ConsumesGestureWhenUnavailable = false,
            Presentation = CommandPresentation.CommandPalette,
        });
}

internal static class TuiCommandIds
{
    public const string New = "workspace.new";
    public const string Previous = "navigator.previous";
    public const string Next = "navigator.next";
    public const string Duplicate = "navigator.duplicate";
    public const string Suspend = "navigator.close";
    public const string Restore = "navigator.restore";
    public const string Up = "navigator.up";
    public const string Help = "workspace.help";
    public const string Close = "workspace.close";
    public const string Activate = "node.activate";
    public const string Refresh = "node.refresh";
    public const string Save = "layout.save";
    public const string Load = "layout.load";
}
