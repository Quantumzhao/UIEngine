using UIEngine.Core;
using UIEngine.Core.Reflection;
using UIEngine.Examples.CyclicDomain;

namespace UIEngine.Frontend.Cli;

/// <summary>Starts the deterministic UIEngine CLI demonstration.</summary>
internal static class Program
{
    private static async Task Main()
    {
        var host = new UIEngineHost(new UIEngineHostOptions
        {
            Exposure = CyclicWorldFactory.CreateExposureRegistry(),
            DescriptorProviders = [new ReflectionObjectDescriptorProvider()],
        });
        host.RegisterRoot("world", CyclicWorldFactory.Create());
        await using var session = new CliSession(host, Console.Out);

        if (Console.IsInputRedirected || Console.IsOutputRedirected)
        {
            await new CliTextReaderRunner(session, Console.In).RunAsync().ConfigureAwait(false);
            return;
        }

        await new CliPromptRunner(session).RunAsync().ConfigureAwait(false);
    }
}
