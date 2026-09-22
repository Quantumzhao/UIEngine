using UIEngine.Core;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Hosting;

namespace UIEngine.Frontend.Tui;

/// <summary>Creates embeddable or fullscreen TUI workspaces over a caller-owned Core host.</summary>
public static class TuiFrontend
{
    /// <summary>Creates a workspace for composition into an existing XenoAtom application.</summary>
    /// <remarks>
    /// The caller retains ownership of <paramref name="host"/> and must dispose it separately.
    /// </remarks>
    public static TuiWorkspace CreateWorkspace(
        UIEngineHost host,
        TuiFrontendOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        ObjectDisposedException.ThrowIf(host.IsDisposed, host);

        var settings = (options ?? new TuiFrontendOptions()).ValidateAndCopy();
        var visual = new VStack(
            new TextBlock(settings.ApplicationTitle),
            new TextBlock($"Ready at {settings.InitialPath}."));
        return new TuiWorkspace(host, settings, visual);
    }

    /// <summary>Runs a workspace in a fullscreen terminal application until exit.</summary>
    /// <remarks>
    /// This convenience host creates and disposes the workspace and terminal application. It does
    /// not dispose <paramref name="host"/>. The same workspace and visual created by
    /// <see cref="CreateWorkspace"/> are used by the fullscreen path.
    /// </remarks>
    public static async Task RunAsync(
        UIEngineHost host,
        TuiFrontendOptions? options = null)
    {
        var workspace = CreateWorkspace(host, options);
        await using var app = new TerminalApp(
            workspace.Visual,
            Terminal.Instance,
            new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen });
        try
        {
            await app.RunAsync(default);
        }
        finally
        {
            workspace.Dispose();
        }
    }
}
