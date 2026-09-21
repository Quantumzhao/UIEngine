using UIEngine.Core;

namespace UIEngine.Frontend.Tui;

/// <summary>Configures one TUI session.</summary>
public sealed record TuiFrontendOptions
{
    /// <summary>Gets the title shown by the workspace.</summary>
    public string ApplicationTitle { get; init; } = "UIEngine";

    /// <summary>Gets the logical path selected when the session starts.</summary>
    public LogicalPath InitialPath { get; init; } = LogicalPath.Root;

    /// <summary>Gets the maximum number of collection entries requested for one visible window.</summary>
    public int CollectionWindowSize { get; init; } = 50;

    internal TuiFrontendOptions ValidateAndCopy()
    {
        if (string.IsNullOrWhiteSpace(ApplicationTitle))
        {
            throw new ArgumentException("The application title cannot be empty or whitespace.", nameof(ApplicationTitle));
        }

        ArgumentNullException.ThrowIfNull(InitialPath);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(CollectionWindowSize);
        return this with { };
    }
}
