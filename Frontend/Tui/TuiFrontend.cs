using UIEngine.Core;
using XenoAtom.Terminal;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Hosting;

namespace UIEngine.Frontend.Tui;

/// <summary>Creates embeddable or fullscreen TUI presentations over a Core workspace.</summary>
public static class TuiFrontend
{
    /// <summary>Creates a TUI presentation with an internally owned Core workspace.</summary>
    public static TuiWorkspace CreateWorkspace(
        TuiFrontendOptions? options = null)
    {
        var workspace = new UIEngineWorkspace();
        try
        {
            var copiedOptions = (options ?? new TuiFrontendOptions()).ValidateAndCopy();
            return new TuiWorkspace(workspace, copiedOptions);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    /// <summary>Runs an internally owned workspace in a fullscreen terminal application.</summary>
    public static Task RunAsync(
        TuiFrontendOptions? options = null) =>
        _RunAsync(CreateWorkspace(options));

    private static async Task _RunAsync(TuiWorkspace workspace)
    {
        using (workspace)
        await using (var app = new TerminalApp(
            workspace.Visual,
            Terminal.Instance,
            new TerminalAppOptions { HostKind = TerminalHostKind.Fullscreen }))
        {
            workspace.CloseRequested += (_, _) => app.Stop();
            await app.RunAsync(default);
        }
    }
}

/// <summary>Configures one TUI workspace.</summary>
public sealed record TuiFrontendOptions
{
    /// <summary>Gets the title shown by the workspace.</summary>
    public string ApplicationTitle { get; init; } = "UIEngine";

    /// <summary>Gets the action applied before the initial visual tree is returned.</summary>
    public TuiStartupConfiguration Startup { get; init; } = new PresentWorkspace();

    /// <summary>Gets the maximum number of collection entries requested for one visible window.</summary>
    public int CollectionWindowSize { get; init; } = 50;

    internal TuiFrontendOptions ValidateAndCopy()
    {
        if (string.IsNullOrWhiteSpace(ApplicationTitle))
        {
            throw new ArgumentException(
                "The application title cannot be empty or whitespace.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(CollectionWindowSize);
        ArgumentNullException.ThrowIfNull(Startup);

        return this with { Startup = Startup.Copy() };
    }
}
