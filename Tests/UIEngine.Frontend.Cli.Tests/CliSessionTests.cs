using System.Globalization;
using UIEngine.Core;
using UIEngine.Core.Reflection;
using UIEngine.Examples.CyclicDomain;
using Xunit;

namespace UIEngine.Frontend.Cli.Tests;

/// <summary>Verifies command parsing, navigation, live operations, and session recovery.</summary>
public sealed class CliSessionTests
{
    [Fact]
    public void InteractivePromptLeavesMostOfTheTerminalAvailableForCommandOutput()
    {
        var configuration = CliPromptRunner.CreateConfiguration();

        Assert.Equal(3, configuration.MaxCompletionItemsCount);
        Assert.Equal(0.25, configuration.ProportionOfWindowHeightForCompletionPane);
    }

    [Fact]
    public void TokenizerPreservesQuotedValuesAndRejectsUnterminatedQuotes()
    {
        var valid = CliTokenizer.Tokenize("call Rename name=\"New York\"");
        var invalid = CliTokenizer.Tokenize("set Name \"New York");

        Assert.True(valid.IsSuccess);
        Assert.Equal(3, valid.Value.Count);
        Assert.Equal("call", valid.Value[0]);
        Assert.Equal("Rename", valid.Value[1]);
        Assert.Equal("name=New York", valid.Value[2]);
        Assert.Equal(InteractionErrorCode.INVALID_INPUT, invalid.Error?.Code);
    }

    [Fact]
    public async Task AbsoluteAndParentNavigationPreserveIdentityAcrossTheCycle()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var session = _CreateSession(output);

        await session.ExecuteAsync("cD /world/Nations/0");
        var nationHandle = Assert.IsType<ObjectHandle>(session.CurrentHandle);
        await session.ExecuteAsync("CD /world/Nations/0/Capital/OwnerNation");

        Assert.Equal(nationHandle, session.CurrentHandle);
        Assert.Equal("/world/Nations[index=0]/Capital/OwnerNation", session.CurrentPath);

        await session.ExecuteAsync("cd ..");

        Assert.Equal("/world/Nations[index=0]/Capital", session.CurrentPath);
        Assert.NotEqual(nationHandle, session.CurrentHandle);
    }

    [Fact]
    public async Task NavigationUsesCanonicalCorePathsAndRecoversRootReplacement()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        using var host = new UIEngineHost([new ReflectionObjectDescriptorProvider()]);
        host.RegisterRoot("world", CyclicWorldFactory.Create());
        var session = new CliSession(host, output);

        await session.ExecuteAsync("cd /world/Nations/0");
        Assert.Equal("/world/Nations[index=0]", session.CurrentPath);

        await session.ExecuteAsync("cd /world");
        host.ReplaceRoot("world", new World("Mars"));
        await session.ExecuteAsync("get Name");

        Assert.Contains("Name = Mars", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListInspectGetSetAndCallUseLiveDescriptors()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var session = _CreateSession(output);

        await session.ExecuteAsync("LS");
        await session.ExecuteAsync("cd /world/Nations/0");
        await session.ExecuteAsync("ls");
        await session.ExecuteAsync("inspect");
        await session.ExecuteAsync("GeT Population");
        await session.ExecuteAsync("set Population 120");
        await session.ExecuteAsync("set Motto \"New Horizon\"");
        await session.ExecuteAsync("call AdvanceTurn populationDelta=5");
        await session.ExecuteAsync("get Population");
        await session.ExecuteAsync("get Motto");

        var transcript = output.ToString();
        Assert.Contains("root world", transcript, StringComparison.Ordinal);
        Assert.Contains("value Population", transcript, StringComparison.Ordinal);
        Assert.Contains("reference Capital", transcript, StringComparison.Ordinal);
        Assert.Contains("collection Cities", transcript, StringComparison.Ordinal);
        Assert.Contains("action AdvanceTurn", transcript, StringComparison.Ordinal);
        Assert.Contains("identity: ", transcript, StringComparison.Ordinal);
        Assert.Contains("Population = 100", transcript, StringComparison.Ordinal);
        Assert.Contains("Population = 120", transcript, StringComparison.Ordinal);
        Assert.Contains("Motto = New Horizon", transcript, StringComparison.Ordinal);
        Assert.Contains("AdvanceTurn => 125", transcript, StringComparison.Ordinal);
        Assert.Contains("Population = 125", transcript, StringComparison.Ordinal);
        Assert.EndsWith($"Motto = New Horizon{Environment.NewLine}", transcript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedCommandsAndOperationFailuresDoNotTerminateTheSession()
    {
        var input = new StringReader(
            "cd /world/Nations/0\n" +
            "get population\n" +
            "set Population invalid\n" +
            "call AdvanceTurn populationDelta=-1\n" +
            "set Population \"unterminated\n" +
            "get Population\n" +
            "exit\n");
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var session = _CreateSession(output);

        await new CliTextReaderRunner(session, input).RunAsync();

        var transcript = output.ToString();
        Assert.Contains("error TARGET_UNAVAILABLE", transcript, StringComparison.Ordinal);
        Assert.Contains("error CONVERSION_FAILED", transcript, StringComparison.Ordinal);
        Assert.Contains("error INVOCATION_FAILED", transcript, StringComparison.Ordinal);
        Assert.Contains("error INVALID_INPUT", transcript, StringComparison.Ordinal);
        Assert.Contains("Population = 100", transcript, StringComparison.Ordinal);
        Assert.EndsWith($"bye{Environment.NewLine}", transcript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompletionsFollowCommandsAndLiveDescriptors()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var session = _CreateSession(output);

        var commandCompletions = await session.GetCompletionsAsync("c", 1);
        Assert.Contains("cd", commandCompletions);
        Assert.Contains("call", commandCompletions);

        var rootCompletions = await session.GetCompletionsAsync("cd /w", 5);
        Assert.Contains("/world", rootCompletions);

        await session.ExecuteAsync("cd /world/Nations/0");

        var readCompletions = await session.GetCompletionsAsync("get P", 5);
        Assert.Contains("Population", readCompletions);

        var writeCompletions = await session.GetCompletionsAsync("set P", 5);
        Assert.Contains("Population", writeCompletions);
        Assert.DoesNotContain("Turn", writeCompletions);

        var actionCompletions = await session.GetCompletionsAsync("call A", 6);
        Assert.Contains("AdvanceTurn", actionCompletions);

        var parameterCompletions = await session.GetCompletionsAsync("call AdvanceTurn ", 17);
        Assert.Contains("populationDelta=", parameterCompletions);
    }

    private static CliSession _CreateSession(TextWriter output)
    {
        var host = new UIEngineHost([new ReflectionObjectDescriptorProvider()]);
        host.RegisterRoot("world", CyclicWorldFactory.Create());
        return new CliSession(host, output);
    }
}
