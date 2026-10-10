using LanguageExt;
using UIEngine.Core;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Styling;

namespace UIEngine.Frontend.Tui;

internal sealed class NavigatorPresentation : IDisposable
{
    private readonly Action<Guid> _Select;
    private readonly Button _Header;
    private Visual? _LastFocusedVisual;

    public NavigatorPresentation(
        Navigator navigator,
        NavigatorPresentationConfiguration configuration,
        Either<InteractionError, Option<BaseNode>> entry,
        Action<Guid> select)
    {
        Navigator = navigator;
        Configuration = configuration;
        _Select = select;
        CurrentControl = new PlaceholderNodeControl(entry);
        _Header = new Button(_CreateTitle(CurrentControl));
        _Header.ClickRouted += (_, _) => _Select(Navigator.Id);
        Container = new Group(_Header, CurrentControl.Visual);
        ApplyConfiguration(configuration);
    }

    public Navigator Navigator { get; }

    public NavigatorPresentationConfiguration Configuration { get; private set; }

    public Group Container { get; }

    public Button Header => _Header;

    public PlaceholderNodeControl CurrentControl { get; private set; }

    public bool IsDisposed { get; private set; }

    public bool IsSelected
    {
        get;
        set
        {
            field = value;
            Container.SetStyle(value
                ? GroupStyle.Rounded with
                {
                    BorderCellStyle = Style.None.WithForeground(Colors.Cyan),
                    FocusedBorderCellStyle = Style.None.WithForeground(Colors.Cyan),
                }
                : GroupStyle.Rounded);
        }
    }

    public void ApplyConfiguration(NavigatorPresentationConfiguration configuration)
    {
        Configuration = configuration;
        Container.MinWidth = configuration.Width;
        Container.MaxWidth = configuration.Width;
        Container.MinHeight = configuration.Height;
        Container.MaxHeight = configuration.Height;
    }

    public void ReplaceControl(Either<InteractionError, Option<BaseNode>> entry)
    {
        var replacement = new PlaceholderNodeControl(entry);
        var previous = CurrentControl;
        CurrentControl = replacement;
        Container.Content = replacement.Visual;
        _Header.Content = _CreateTitle(replacement);
        previous.Dispose();
    }

    public void RememberFocus()
    {
        var candidate = Container.App?.FocusedElement;
        for (var current = candidate; current is not null; current = current.Parent)
        {
            if (ReferenceEquals(current, Container))
            {
                _LastFocusedVisual = candidate;
                return;
            }
        }
    }

    public void Focus() => Container.App?.Focus(_LastFocusedVisual ?? _Header);

    public void Dispose()
    {
        if (IsDisposed)
        {
            return;
        }

        IsDisposed = true;
        CurrentControl.Dispose();
        Container.Content = null!;
    }

    private static TextBlock _CreateTitle(PlaceholderNodeControl control)
    {
        var title = new TextBlock(control.DisplayName);
        title.SetStyle(new TextBlockStyle { Foreground = control.State switch
        {
            PlaceholderNodeState.Healthy => Colors.Green,
            PlaceholderNodeState.Invalid => Colors.Red,
            _ => Colors.Yellow,
        }});
        return title;
    }
}

internal sealed class PlaceholderNodeControl : IDisposable
{
    public PlaceholderNodeControl(Either<InteractionError, Option<BaseNode>> entry)
    {
        if (!entry.IsRight)
        {
            State = PlaceholderNodeState.Invalid;
            DisplayName = "Invalid";
            Visual = new TextBlock(string.Empty);
            return;
        }

        var node = ((Option<BaseNode>)entry).IfNoneUnsafe((BaseNode?)null);
        if (node is null)
        {
            State = PlaceholderNodeState.Empty;
            DisplayName = "Empty";
            Visual = new TextBlock("Empty navigation entry");
            Visual.SetStyle(new TextBlockStyle { Foreground = Colors.Yellow });
            return;
        }

        State = PlaceholderNodeState.Healthy;
        DisplayName = node.Name;
        Visual = new TextBlock($"Node: {node.Name}");
        Visual.SetStyle(new TextBlockStyle { Foreground = Colors.Green });
    }

    public PlaceholderNodeState State { get; }

    public string DisplayName { get; }

    public TextBlock Visual { get; }

    public bool IsDisposed { get; private set; }

    public void Dispose() => IsDisposed = true;
}

internal enum PlaceholderNodeState
{
    Healthy,
    Empty,
    Invalid,
}
