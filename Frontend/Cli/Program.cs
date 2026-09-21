using UIEngine.Core;
using UIEngine.Examples.CyclicDomain;

namespace UIEngine.Frontend.Cli;

/// <summary>Starts the deterministic UIEngine CLI demonstration.</summary>
internal static class Program
{
    private static async Task Main()
    {
        var host = new UIEngineHost(new UIEngineHostOptions
        {
            Exposures = CyclicWorldFactory.CreateExposures(),
        });
        host.SetRoot("world", CyclicWorldFactory.Create());
        using var session = new CliSession(host, Console.Out);

        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            await new CliTextReaderRunner(session, Console.In).RunAsync();
            return;
        }

        await new CliPromptRunner(session).RunAsync();
    }
}
