using XenoAtom.Terminal.UI.Threading;

namespace UIEngine.Frontend.Tui;

internal interface ITuiPresentationDispatcher
{
    void Post(Action action);
}

internal sealed class ToolkitTuiPresentationDispatcher : ITuiPresentationDispatcher
{
    public static ToolkitTuiPresentationDispatcher Instance { get; } = new();

    private ToolkitTuiPresentationDispatcher()
    {
    }

    public void Post(Action action)
    {
        var dispatcher = Dispatcher.Current;
        if (dispatcher.CheckAccess())
        {
            action();
            return;
        }

        dispatcher.Post(action);
    }
}
