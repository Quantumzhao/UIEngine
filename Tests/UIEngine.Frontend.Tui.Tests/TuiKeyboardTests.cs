using UIEngine.Frontend.Tui;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Hosting;
using Xunit;

namespace UIEngine.Frontend.Tui.Tests;

[Collection("Terminal application")]
public sealed class TuiKeyboardTests
{
    [Fact]
    public async Task NavigatorShortcutsRouteThroughTheTerminalAndPreserveRestoredPresentation()
    {
        using var host = TuiTestModel.CreateHost();
        using var tui = TuiFrontend.CreateWorkspace(new TuiFrontendOptions
        {
            Startup = new AddNavigator(TuiTestModel.Path("model", "Child")),
        });
        tui.Workspace.AddNavigator(TuiTestModel.RootId);
        var firstId = tui.Workspace.Navigators[0].Id;
        var secondId = tui.Workspace.Navigators[1].Id;
        NavigatorPresentation? duplicatedPresentation = null;
        var closeRequested = false;
        tui.CloseRequested += (_, _) => closeRequested = true;
        var backend = new InMemoryTerminalBackend(new TerminalSize(100, 30));
        using var terminal = Terminal.Open(backend, new TerminalOptions(), force: true);
        var phase = 0;

        await terminal.Instance.RunAsync(
            tui.Visual,
            async _ =>
            {
                await Task.CompletedTask;
                switch (phase)
                {
                    case 0:
                        backend.PushEvent(new TerminalKeyEvent
                        {
                            Key = TerminalKey.PageDown,
                            Modifiers = TerminalModifiers.Ctrl,
                        });
                        phase = 1;
                        break;
                    case 1 when tui.SelectedNavigatorId == secondId:
                        backend.PushEvent(new TerminalKeyEvent
                        {
                            Key = TerminalKey.PageUp,
                            Modifiers = TerminalModifiers.Ctrl,
                        });
                        phase = 2;
                        break;
                    case 2 when tui.SelectedNavigatorId == firstId:
                        backend.PushEvent(new TerminalKeyEvent
                        {
                            Key = TerminalKey.Left,
                            Modifiers = TerminalModifiers.Alt,
                        });
                        phase = 3;
                        break;
                    case 3 when tui.Workspace.Navigators[0].CurrentPath
                        .ToDisplayString().IfLeft(static error => error.Message) == "/Model":
                        backend.PushEvent(new TerminalKeyEvent
                        {
                            Key = TerminalKey.Unknown,
                            Char = 'd',
                            Modifiers = TerminalModifiers.Ctrl,
                        });
                        phase = 4;
                        break;
                    case 4 when tui.Workspace.Navigators.Count == 3:
                        duplicatedPresentation = tui.Presentations.Single(
                            presentation => presentation.Navigator.Id == tui.SelectedNavigatorId);
                        backend.PushEvent(new TerminalKeyEvent
                        {
                            Key = TerminalKey.Unknown,
                            Char = 'w',
                            Modifiers = TerminalModifiers.Ctrl,
                        });
                        phase = 5;
                        break;
                    case 5 when ReferenceEquals(tui.SuspendedPresentation, duplicatedPresentation):
                        backend.PushEvent(new TerminalKeyEvent
                        {
                            Key = TerminalKey.Unknown,
                            Char = 't',
                            Modifiers = TerminalModifiers.Ctrl | TerminalModifiers.Shift,
                        });
                        phase = 6;
                        break;
                    case 6 when duplicatedPresentation is not null &&
                        tui.Presentations.Contains(duplicatedPresentation):
                        backend.PushEvent(new TerminalKeyEvent
                        {
                            Key = TerminalKey.Unknown,
                            Char = 'q',
                            Modifiers = TerminalModifiers.Ctrl,
                        });
                        phase = 7;
                        break;
                    case 7 when closeRequested:
                        return TerminalLoopResult.Stop;
                }

                return TerminalLoopResult.Continue;
            },
            new TerminalRunOptions(),
            default).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(closeRequested);
        Assert.Equal(3, tui.Workspace.Navigators.Count);
        var restoredPresentation = Assert.IsType<NavigatorPresentation>(duplicatedPresentation);
        Assert.Same(restoredPresentation, tui.Presentations.ElementAt(1));
        Assert.Equal(
            "/Model",
            restoredPresentation.Navigator.CurrentPath.ToDisplayString()
                .IfLeft(static error => error.Message));
    }

    [Fact]
    public void CompleteCommandMapIsStableAndDeferredCommandsAreUnavailable()
    {
        using var host = TuiTestModel.CreateHost();
        using var tui = TuiFrontend.CreateWorkspace();
        var commands = tui.Visual.Commands.ToDictionary(static command => command.Id);

        Assert.Equal(
            [
                TuiCommandIds.New,
                TuiCommandIds.Previous,
                TuiCommandIds.Next,
                TuiCommandIds.Duplicate,
                TuiCommandIds.Suspend,
                TuiCommandIds.Restore,
                TuiCommandIds.Up,
                TuiCommandIds.Help,
                TuiCommandIds.Close,
                TuiCommandIds.Activate,
                TuiCommandIds.Refresh,
                TuiCommandIds.Save,
                TuiCommandIds.Load,
            ],
            commands.Keys);
        Assert.All(
            new[]
            {
                TuiCommandIds.Activate,
                TuiCommandIds.Refresh,
                TuiCommandIds.Save,
                TuiCommandIds.Load,
            },
            id => Assert.False(commands[id].CanExecuteFor(tui.Visual)));
    }

    [Fact]
    public async Task NarrowResizeKeepsEveryNavigatorAndControlAndSelectionScrollsIntoView()
    {
        using var host = TuiTestModel.CreateHost();
        using var tui = TuiFrontend.CreateWorkspace(new TuiFrontendOptions
        {
            Startup = new AddNavigator(TuiTestModel.Path("model")),
        });
        tui.Workspace.AddNavigator(TuiTestModel.Path("model", "Child"));
        var firstId = tui.Workspace.Navigators[0].Id;
        var secondId = tui.Workspace.Navigators[1].Id;
        tui.SetPresentationConfiguration(firstId, new(0, 0, 40, 12));
        tui.SetPresentationConfiguration(secondId, new(1, 1, 40, 12));
        var presentations = tui.Presentations.ToArray();
        var controls = presentations.Select(static item => item.CurrentControl).ToArray();
        var backend = new InMemoryTerminalBackend(new TerminalSize(50, 18));
        using var terminal = Terminal.Open(backend, new TerminalOptions(), force: true);
        var phase = 0;

        await terminal.Instance.RunAsync(
            tui.Visual,
            async _ =>
            {
                await Task.CompletedTask;
                switch (phase)
                {
                    case 0:
                        backend.PushEvent(new TerminalKeyEvent
                        {
                            Key = TerminalKey.PageDown,
                            Modifiers = TerminalModifiers.Ctrl,
                        });
                        phase = 1;
                        break;
                    case 1 when tui.SelectedNavigatorId == secondId &&
                        tui.NavigatorScroll.HorizontalOffset > 0 &&
                        tui.NavigatorScroll.VerticalOffset > 0:
                        backend.SetSize(new TerminalSize(30, 10), true);
                        phase = 2;
                        break;
                    case 2 when tui.Visual.Bounds.Width == 30 && tui.Visual.Bounds.Height == 10:
                        return TerminalLoopResult.Stop;
                }

                return TerminalLoopResult.Continue;
            },
            new TerminalRunOptions(),
            default).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(secondId, tui.SelectedNavigatorId);
        Assert.Equal(presentations, tui.Presentations);
        Assert.Equal(controls, tui.Presentations.Select(static item => item.CurrentControl));
        Assert.All(tui.Presentations, static presentation => Assert.NotNull(presentation.Container.Parent));
    }

    [Fact]
    public async Task RootAndHelpDialogsTrapAndRestoreFocus()
    {
        using var host = TuiTestModel.CreateHost();
        using var tui = TuiFrontend.CreateWorkspace();
        var backend = new InMemoryTerminalBackend(new TerminalSize(100, 30));
        using var terminal = Terminal.Open(backend, new TerminalOptions(), force: true);
        Visual? originalFocus = null;
        var phase = 0;

        await terminal.Instance.RunAsync(
            tui.Visual,
            async context =>
            {
                await Task.CompletedTask;
                switch (phase)
                {
                    case 0:
                        originalFocus = context.App.FocusedElement;
                        backend.PushEvent(new TerminalKeyEvent
                        {
                            Key = TerminalKey.Unknown,
                            Char = 'n',
                            Modifiers = TerminalModifiers.Ctrl,
                        });
                        phase = 1;
                        break;
                    case 1 when context.App.FocusedElement is Select<string>:
                        Assert.True(_IsInDialog(context.App.FocusedElement));
                        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Escape });
                        phase = 2;
                        break;
                    case 2 when ReferenceEquals(context.App.FocusedElement, originalFocus):
                        backend.PushEvent(new TerminalKeyEvent
                        {
                            Key = TerminalKey.Unknown,
                            Char = 'n',
                            Modifiers = TerminalModifiers.Ctrl,
                        });
                        phase = 3;
                        break;
                    case 3 when context.App.FocusedElement is Select<string>:
                        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Enter });
                        phase = 4;
                        break;
                    case 4 when tui.Workspace.Navigators.Count == 1:
                        Assert.Same(
                            Assert.Single(tui.Presentations).Header,
                            context.App.FocusedElement);
                        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.F1 });
                        phase = 5;
                        break;
                    case 5 when _IsInDialog(context.App.FocusedElement):
                        backend.PushEvent(new TerminalKeyEvent { Key = TerminalKey.Escape });
                        phase = 6;
                        break;
                    case 6 when ReferenceEquals(
                        context.App.FocusedElement,
                        Assert.Single(tui.Presentations).Header):
                        return TerminalLoopResult.Stop;
                }

                return TerminalLoopResult.Continue;
            },
            new TerminalRunOptions(),
            default).AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Single(tui.Workspace.Navigators);
        Assert.Single(tui.Presentations);
    }

    private static bool _IsInDialog(Visual? visual)
    {
        for (var current = visual; current is not null; current = current.Parent)
        {
            if (current is Dialog)
            {
                return true;
            }
        }

        return false;
    }
}
