using UIEngine.Core;
using UIEngine.Examples.CyclicDomain;
using UIEngine.Frontend.Tui;

using var host = new UIEngineHost(new UIEngineHostOptions
{
    Exposures = CyclicWorldFactory.CreateExposures(),
});
host.SetRoot("world", CyclicWorldFactory.Create());

await TuiFrontend.RunAsync(host, new TuiFrontendOptions
{
    ApplicationTitle = "UIEngine Cyclic World",
    InitialPath = LogicalPath.Root.Append("world"),
});
