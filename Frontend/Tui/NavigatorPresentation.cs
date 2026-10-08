using LanguageExt;
using UIEngine.Core;
using XenoAtom.Terminal.UI.Controls;

namespace UIEngine.Frontend.Tui;

internal sealed class NavigatorPresentation : IDisposable
{
    private int _Disposed;
    private int _VisualsDetached;

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

    public bool IsDisposed => Volatile.Read(ref _Disposed) != 0;

    public void ApplyConfiguration(NavigatorPresentationConfiguration configuration) =>
        Configuration = configuration;

    public void ReplaceControl(Either<InteractionError, Option<BaseNode>> entry)
    {
        if (IsDisposed)
        {
            return;
        }

        var previous = CurrentControl;
        var replacement = new PlaceholderNodeControl(entry);
        Container.Children[0] = replacement.Visual;
        CurrentControl = replacement;
        previous.Dispose();
    }

    public void Dispose()
    {
        Retire();
        if (Interlocked.Exchange(ref _VisualsDetached, 1) != 0)
        {
            return;
        }

        Container.Children.Clear();
    }

    public void Retire()
    {
        if (Interlocked.Exchange(ref _Disposed, 1) == 0)
        {
            CurrentControl.Dispose();
        }
    }
}

internal sealed class PlaceholderNodeControl : IDisposable
{
    private int _Disposed;

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

    public bool IsDisposed => Volatile.Read(ref _Disposed) != 0;

    public void Dispose() => Interlocked.Exchange(ref _Disposed, 1);
}

internal enum PlaceholderNodeState
{
    Healthy,
    Empty,
    Broken,
}
