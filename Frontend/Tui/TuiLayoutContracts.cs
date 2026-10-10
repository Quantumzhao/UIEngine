using UIEngine.Core;

namespace UIEngine.Frontend.Tui;

/// <summary>One explicit startup action for a TUI presentation.</summary>
public abstract record TuiStartupConfiguration
{
    private protected TuiStartupConfiguration()
    {
    }

    internal abstract TuiStartupConfiguration Copy();
}

/// <summary>Presents the Core workspace without changing its contents.</summary>
public sealed record PresentWorkspace : TuiStartupConfiguration
{
    internal override TuiStartupConfiguration Copy() => new PresentWorkspace();
}

/// <summary>Adds one navigator before exposing the initial visual tree.</summary>
public sealed record AddNavigator : TuiStartupConfiguration
{
    public AddNavigator(LogicalPath path)
    {
        ArgumentNullException.ThrowIfNull(path);
        Path = path;
    }

    public LogicalPath Path { get; }

    internal override TuiStartupConfiguration Copy() => new AddNavigator(Path);
}

/// <summary>Replaces the Core workspace from a TUI layout before presentation.</summary>
public sealed record RestoreLayout : TuiStartupConfiguration
{
    public RestoreLayout(TuiLayoutSnapshot layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        Layout = layout;
    }

    public TuiLayoutSnapshot Layout { get; }

    internal override TuiStartupConfiguration Copy() =>
        new RestoreLayout(Layout.Copy());
}

/// <summary>Serializable Core navigation and frontend-owned presentation state.</summary>
public sealed record TuiLayoutSnapshot(
    WorkspaceSnapshot Workspace,
    IReadOnlyDictionary<Guid, NavigatorPresentationConfiguration> Navigators)
{
    internal TuiLayoutSnapshot Copy()
    {
        ArgumentNullException.ThrowIfNull(Workspace);
        return new TuiLayoutSnapshot(
            _CopyWorkspace(Workspace),
            Navigators is null
                ? null!
                : new Dictionary<Guid, NavigatorPresentationConfiguration>(Navigators));
    }

    private static WorkspaceSnapshot _CopyWorkspace(WorkspaceSnapshot snapshot) => new(
        snapshot.Navigators is null
            ? null!
            : snapshot.Navigators.Select(static navigator => navigator is null
                ? null!
                : new NavigatorSnapshot(
                    navigator.Id,
                    navigator.CurrentPath is null
                        ? null!
                        : navigator.CurrentPath.ToArray()))
                .ToArray(),
        snapshot.SelectedNavigatorId);
}

/// <summary>Stable logical placement and preferred size for one navigator.</summary>
public sealed record NavigatorPresentationConfiguration(
    int Row,
    int Column,
    int Width,
    int Height)
{
    internal bool IsValid =>
        Row is >= 0 and <= ushort.MaxValue &&
        Column is >= 0 and <= ushort.MaxValue &&
        Width is >= 1 and <= ushort.MaxValue &&
        Height is >= 1 and <= ushort.MaxValue;
}
