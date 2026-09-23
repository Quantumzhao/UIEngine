using System.Globalization;
using UIEngine.Core;
using UIEngine.Examples.CyclicDomain;
using Xunit;

namespace UIEngine.Frontend.Cli.Tests;

public sealed class CliBehaviorTests
{
    [Fact]
    public void TokenizerPreservesQuotedValuesAndReportsMalformedInput()
    {
        var valid = CliTokenizer.Tokenize("call Rename name=\"New York\"");
        var invalid = CliTokenizer.Tokenize("set Name \"New York");

        Assert.True(valid.IsSuccess);
        Assert.Equal(["call", "Rename", "name=New York"], valid.Value);
        Assert.Equal(InteractionErrorCode.INVALID_INPUT, invalid.Error?.Code);
    }

    [Fact]
    public async Task RedirectedWorkflowCoversNavigationInspectionMutationCollectionsAndActions()
    {
        var input = new StringReader(
            "ls\n" +
            "cd /world\n" +
            "inspect\n" +
            "ls PopulationForecast offset=9998 limit=2\n" +
            "cd /world/Nations[index=0]\n" +
            "ls\n" +
            "get Population\n" +
            "set Population 120\n" +
            "call AdvanceTurn populationDelta=5\n" +
            "get Population\n" +
            "cd /world/Economy\n" +
            "get GrossDomesticProduct\n" +
            "set GrossDomesticProduct 1250\n" +
            "exit\n");
        var output = new StringWriter(CultureInfo.InvariantCulture);
        using var session = _CreateSession(output);

        await new CliTextReaderRunner(session, input).RunAsync();

        var transcript = output.ToString();
        Assert.Contains("root world", transcript, StringComparison.Ordinal);
        Assert.Contains("domain-identity: world/Earth", transcript, StringComparison.Ordinal);
        Assert.Contains(
            "collection PopulationForecast offset=9998 count=2 total=10000 hasMore=False",
            transcript,
            StringComparison.Ordinal);
        Assert.Contains("[9998] value=10098", transcript, StringComparison.Ordinal);
        Assert.Contains("value Population", transcript, StringComparison.Ordinal);
        Assert.Contains("reference Capital", transcript, StringComparison.Ordinal);
        Assert.Contains("collection Cities", transcript, StringComparison.Ordinal);
        Assert.Contains("action AdvanceTurn", transcript, StringComparison.Ordinal);
        Assert.Contains("Population = 100", transcript, StringComparison.Ordinal);
        Assert.Contains("Population = 120", transcript, StringComparison.Ordinal);
        Assert.Contains("AdvanceTurn => 125", transcript, StringComparison.Ordinal);
        Assert.Contains("Population = 125", transcript, StringComparison.Ordinal);
        Assert.Contains("GrossDomesticProduct = 1000", transcript, StringComparison.Ordinal);
        Assert.Contains("GrossDomesticProduct = 1250", transcript, StringComparison.Ordinal);
        Assert.EndsWith($"bye{Environment.NewLine}", transcript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompletionUsesTheCurrentConcreteDescriptor()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        using var session = _CreateSession(output);

        Assert.Contains("cd", session.GetCompletions("c", 1));
        Assert.Contains("/world", session.GetCompletions("cd /w", 5));
        await session.ExecuteAsync("cd /world/Nations[index=0]");

        Assert.Contains("Population", session.GetCompletions("get P", 5));
        Assert.DoesNotContain("Turn", session.GetCompletions("set T", 5));
        Assert.Contains("Cities", session.GetCompletions("ls C", 4));
        Assert.Contains("AdvanceTurn", session.GetCompletions("call A", 6));
        Assert.Contains(
            "populationDelta=",
            session.GetCompletions("call AdvanceTurn ", 17));
    }

    [Fact]
    public async Task StructuredFailuresDoNotTerminateTheSession()
    {
        var input = new StringReader(
            "cd /world/Nations[index=0]\n" +
            "get population\n" +
            "set Population invalid\n" +
            "set Population -1\n" +
            "set Motto \"unterminated\n" +
            "get Population\n" +
            "exit\n");
        var output = new StringWriter(CultureInfo.InvariantCulture);
        using var session = _CreateSession(output);

        await new CliTextReaderRunner(session, input).RunAsync();

        var transcript = output.ToString();
        Assert.Contains("error NOT_FOUND", transcript, StringComparison.Ordinal);
        Assert.Contains("error CONVERSION_FAILED", transcript, StringComparison.Ordinal);
        Assert.Contains("error VALIDATION_FAILED", transcript, StringComparison.Ordinal);
        Assert.Contains("issue OUT_OF_RANGE id=Population", transcript, StringComparison.Ordinal);
        Assert.Contains("error INVALID_INPUT", transcript, StringComparison.Ordinal);
        Assert.Contains("Population = 100", transcript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NavigationTracksCyclesAndCompatibleReplacement()
    {
        var world = CyclicWorldFactory.Create();
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var host = _CreateHost();
        host.SetRoot("world", world);
        using var session = new CliSession(host, output);

        await session.ExecuteAsync("cd /world/Nations[index=0]");
        var nation = session.CurrentHandle;
        await session.ExecuteAsync("cd /world/Nations[index=0]/Capital/OwnerNation");
        Assert.Equal(nation, session.CurrentHandle);

        await session.ExecuteAsync("cd /world/Nations[index=0]/Capital");
        var originalCapital = session.CurrentHandle;
        Assert.Single(world.Nations).ReplaceCapital("Replacement");
        await session.ExecuteAsync("get Name");

        Assert.NotEqual(originalCapital, session.CurrentHandle);
        Assert.Contains("Name = Replacement", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void InteractivePromptKeepsOutputSpaceAvailable()
    {
        var configuration = CliPromptRunner.CreateConfiguration();

        Assert.Equal(3, configuration.MaxCompletionItemsCount);
        Assert.Equal(0.25, configuration.ProportionOfWindowHeightForCompletionPane);
    }

    private static CliSession _CreateSession(TextWriter output)
    {
        var host = _CreateHost();
        host.SetRoot("world", CyclicWorldFactory.Create());
        return new CliSession(host, output);
    }

    private static UIEngineHost _CreateHost() => new(new UIEngineHostOptions
    {
        Exposures = CyclicWorldFactory.CreateExposures(),
    });

}
