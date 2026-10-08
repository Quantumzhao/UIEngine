using System.Runtime.ExceptionServices;
using UIEngine.Core;
using UIEngine.Frontend.Tui;
using XenoAtom.Terminal;
using XenoAtom.Terminal.Backends;
using XenoAtom.Terminal.UI;
using Xunit;

namespace UIEngine.Frontend.Tui.Tests;

[Collection("Terminal application")]
public sealed class TuiDispatcherTests
{
    [Fact]
    public async Task WorkspaceChangesUseDispatcherAndQueuedMutationCannotReachDisposedControl()
    {
        var model = new TuiTestModel();
        using var host = TuiTestModel.CreateHost(model);
        using var tui = TuiFrontend.CreateWorkspace(host, new TuiFrontendOptions
        {
            Startup = new AddNavigator("main", TuiTestModel.Path("model")),
        });
        var coreWorkspace = tui.Workspace;
        var presentation = Assert.Single(tui.Presentations);
        var initialControl = presentation.CurrentControl;
        var navigationThreadId = 0;
        var blockerStarted = new ManualResetEventSlim();
        var releaseBlocker = new ManualResetEventSlim();
        Thread? disposalThread = null;
        ExceptionDispatchInfo? disposalError = null;
        var phase = 0;
        var backend = new InMemoryTerminalBackend(new TerminalSize(60, 15));
        using var terminal = Terminal.Open(backend, new TerminalOptions(), force: true);

        await terminal.Instance.RunAsync(
            tui.Visual,
            context =>
            {
                switch (phase)
                {
                    case 0:
                        coreWorkspace.AddNavigator("immediate", "model");
                        Assert.Equal(2, tui.Presentations.Count);
                        var navigationThread = new Thread(() =>
                        {
                            navigationThreadId = Environment.CurrentManagedThreadId;
                            coreWorkspace.Navigators[0].Navigate(
                                new MemberLogicalPathSegment("Child"));
                        });
                        navigationThread.Start();
                        navigationThread.Join();
                        Assert.True(tui.PostedChangeCount > 0);
                        phase = 1;
                        return ValueTask.FromResult(TerminalLoopResult.Continue);
                    case 1:
                        Assert.NotSame(initialControl, presentation.CurrentControl);
                        Assert.True(initialControl.IsDisposed);
                        Assert.Equal(navigationThreadId, model.ChildAccessThreadId);
                        var currentControl = presentation.CurrentControl;
                        context.App.Dispatcher.Post(() =>
                        {
                            blockerStarted.Set();
                            releaseBlocker.Wait(TimeSpan.FromSeconds(5));
                        });
                        disposalThread = new Thread(() =>
                        {
                            try
                            {
                                Assert.True(blockerStarted.Wait(TimeSpan.FromSeconds(5)));
                                coreWorkspace.Navigators[0].Navigate(
                                    new MemberLogicalPathSegment("Value"));
                                tui.Dispose();
                                Assert.True(currentControl.IsDisposed);
                            }
                            catch (Exception exception)
                            {
                                disposalError = ExceptionDispatchInfo.Capture(exception);
                            }
                            finally
                            {
                                releaseBlocker.Set();
                            }
                        });
                        disposalThread.Start();
                        phase = 2;
                        return ValueTask.FromResult(TerminalLoopResult.Continue);
                    default:
                        Assert.True(tui.IsDisposed);
                        return ValueTask.FromResult(TerminalLoopResult.Stop);
                }
            },
            new TerminalRunOptions(),
            default).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
        disposalThread!.Join();
        disposalError?.Throw();
    }
}
