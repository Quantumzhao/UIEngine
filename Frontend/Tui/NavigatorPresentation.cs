using LanguageExt;
using UIEngine.Core;
using XenoAtom.Terminal.UI.Controls;

namespace UIEngine.Frontend.Tui;

internal sealed class NavigatorPresentation : IDisposable
{
    public NavigatorPresentation(
        Navigator navigator,
        NavigatorPresentationConfiguration configuration,
        Either<InteractionError, Option<BaseNode>> entry)
    {
        Navigator = navigator;
        Configuration = configuration;
        CurrentControl = new PlaceholderNodeControl(entry);
        Container = new VStack(CurrentControl.Visual);
    }

    public Navigator Navigator { get; }

    public NavigatorPresentationConfiguration Configuration { get; private set; }

    public VStack Container { get; }

    public PlaceholderNodeControl CurrentControl { get; private set; }

    public bool IsSelected { get; set; }

    public bool IsVisible
    {
        get => Container.IsVisible;
        set => Container.IsVisible = value;
    }

    public void ApplyConfiguration(NavigatorPresentationConfiguration configuration) =>
        Configuration = configuration;

    public void ReplaceControl(Either<InteractionError, Option<BaseNode>> entry)
    {
        var replacement = new PlaceholderNodeControl(entry);
        Container.Children[0] = replacement.Visual;
        CurrentControl = replacement;
    }

    public void Dispose() => Container.Children.Clear();
}

internal sealed class PlaceholderNodeControl
{
    public PlaceholderNodeControl(Either<InteractionError, Option<BaseNode>> entry)
    {
        if (!entry.IsRight)
        {
            State = PlaceholderNodeState.Broken;
            var error = (InteractionError)entry;
            Visual = new TextBlock($"Broken [{error.Code}]: {error.Message}");
            return;
        }

        var node = ((Option<BaseNode>)entry).IfNoneUnsafe((BaseNode?)null);
        if (node is null)
        {
            State = PlaceholderNodeState.Empty;
            Visual = new TextBlock("Empty navigation entry");
            return;
        }

        State = PlaceholderNodeState.Healthy;
        Visual = new TextBlock($"Node: {node.Name}");
    }

    public PlaceholderNodeState State { get; }

    public TextBlock Visual { get; }
}

internal enum PlaceholderNodeState
{
    Healthy,
    Empty,
    Broken,
}
