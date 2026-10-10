using UIEngine.Core;
using UIEngine.Core.Attributes;
using UIEngine.Examples.CyclicDomain;
using UIEngine.Frontend.Tui;

using var host = new UIEngineHost(new UIEngineHostOptions
{
    Exposures = CyclicWorldFactory.CreateExposures(),
});

await TuiFrontend.RunAsync(new TuiFrontendOptions
{
    ApplicationTitle = "UIEngine Cyclic World",
    Startup = new AddNavigator(ApplicationRoots.Path),
});

internal static class ApplicationRoots
{
    [Root]
    public static World World { get; } = CyclicWorldFactory.Create();

    public static LogicalPath Path
    {
        get
        {
            foreach (var id in UIEngineHost.Instance.RootIds)
            {
                var path = LogicalPath.Empty.Append(new RootLogicalPathSegment(id));
                var names = path.ResolveNames();
                if (names.IsRight && StringComparer.Ordinal.Equals(
                    names.IfLeft(static error => throw new InvalidOperationException(error.Message))[0],
                    nameof(World)))
                {
                    return path;
                }
            }

            throw new InvalidOperationException("The world root was not discovered.");
        }
    }
}
