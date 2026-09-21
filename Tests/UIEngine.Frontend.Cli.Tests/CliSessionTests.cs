using System.Globalization;
using System.Text;
using UIEngine.Core;
using UIEngine.Core.Attributes;
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
    public async Task NavigationRecoversCompatibleReplacementAndRefusesDifferentIdentity()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        using var host = _CreateHost();
        host.RegisterRoot("world", CyclicWorldFactory.Create());
        var session = new CliSession(host, output);

        await session.ExecuteAsync("cd /world/Nations/0");
        Assert.Equal("/world/Nations[index=0]", session.CurrentPath);

        await session.ExecuteAsync("cd /world");
        var originalHandle = session.CurrentHandle;
        host.ReplaceRoot("world", new World("Earth"));
        await session.ExecuteAsync("get Name");
        var replacementHandle = session.CurrentHandle;

        host.ReplaceRoot("world", new World("Mars"));
        await session.ExecuteAsync("get Name");

        Assert.NotEqual(originalHandle, replacementHandle);
        Assert.Contains("Name = Earth", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("error TARGET_MISSING", output.ToString(), StringComparison.Ordinal);
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
        Assert.Contains("error TARGET_MISSING", transcript, StringComparison.Ordinal);
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

        var collectionCompletions = await session.GetCompletionsAsync("ls C", 4);
        Assert.Contains("Cities", collectionCompletions);

        var watchCompletions = await session.GetCompletionsAsync("watch P", 7);
        Assert.Contains("Population", watchCompletions);
    }

    [Fact]
    public async Task RedirectedWorkflowCoversBoundedCollectionsValidationAndAsyncProgress()
    {
        var input = new StringReader(
            "ls\n" +
            "cd /world\n" +
            "inspect\n" +
            "ls PopulationForecast offset=9998 limit=2\n" +
            "ls PopulationForecast limit=101\n" +
            "cd /world/Nations/0\n" +
            "set Population -1\n" +
            "set Population 120\n" +
            "call SimulateGrowthAsync years=3 populationPerYear=2\n" +
            "cd /world/Economy\n" +
            "get GrossDomesticProduct\n" +
            "set GrossDomesticProduct 1250\n" +
            "exit\n");
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var host = _CreateHost();
        host.RegisterRoot("world", CyclicWorldFactory.Create());
        var session = new CliSession(host, output);

        await new CliTextReaderRunner(session, input).RunAsync();

        var transcript = output.ToString();
        Assert.Contains("domain-identity: world/Earth", transcript, StringComparison.Ordinal);
        Assert.Contains(
            "collection PopulationForecast mode=VIRTUALIZED_RANGE offset=9998 count=2 total=10000",
            transcript,
            StringComparison.Ordinal);
        Assert.Contains("[9998] value=10098", transcript, StringComparison.Ordinal);
        Assert.Contains("[9999] value=10099", transcript, StringComparison.Ordinal);
        Assert.Contains("error COLLECTION_LIMIT_EXCEEDED", transcript, StringComparison.Ordinal);
        Assert.Contains("error VALIDATION_FAILED", transcript, StringComparison.Ordinal);
        Assert.Equal(3, _CountLinesStartingWith(transcript, "progress SimulateGrowthAsync"));
        Assert.Contains("SimulateGrowthAsync => 126", transcript, StringComparison.Ordinal);
        Assert.Contains("GrossDomesticProduct = 1000", transcript, StringComparison.Ordinal);
        Assert.Contains("GrossDomesticProduct = 1250", transcript, StringComparison.Ordinal);
        Assert.EndsWith($"bye{Environment.NewLine}", transcript, StringComparison.Ordinal);
        Assert.True(host.IsDisposed);
    }

    [Theory]
    [InlineData(nameof(Nation.Population), ChangeKind.MEMBER_CHANGED)]
    [InlineData(nameof(Nation.Cities), ChangeKind.COLLECTION_ITEMS_ADDED)]
    public async Task RedirectedWatchStreamsChangesUntilCancellation(
        string memberId,
        ChangeKind expectedKind)
    {
        var world = CyclicWorldFactory.Create();
        var nation = Assert.Single(world.Nations);
        var output = new _SignalingWriter();
        var host = _CreateHost();
        host.RegisterRoot("world", world);
        await using var session = new CliSession(host, output);
        using var cancellation = new CancellationTokenSource();
        var input = new StringReader($"cd /world/Nations/0\nwatch {memberId}\n");

        var run = new CliTextReaderRunner(session, input).RunAsync(cancellation.Token);
        await output.Watching.WaitAsync(TimeSpan.FromSeconds(5));
        if (memberId == nameof(Nation.Population))
        {
            nation.Population = 101;
        }
        else
        {
            nation.Cities.Add(new City("city/second", "Second City", nation));
        }

        await output.Change.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await run;

        Assert.Contains($"kind={expectedKind}", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("watch cancelled", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RedirectedCallForwardsCancellationToSupportedInvocation()
    {
        var output = new _SignalingWriter();
        var host = new UIEngineHost([new ReflectionObjectDescriptorProvider()]);
        host.RegisterRoot("model", new _CancellableModel());
        await using var session = new CliSession(host, output);
        using var cancellation = new CancellationTokenSource();
        var input = new StringReader("cd /model\ncall WaitForCancellationAsync\n");

        var run = new CliTextReaderRunner(session, input).RunAsync(cancellation.Token);
        await output.Progress.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await run;

        Assert.Contains("progress WaitForCancellationAsync", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("error CANCELLED", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task MemberBindingRecoversCapitalReplacementWithTheSameIdentity()
    {
        var world = CyclicWorldFactory.Create();
        var nation = Assert.Single(world.Nations);
        var output = new StringWriter(CultureInfo.InvariantCulture);
        await using var session = _CreateSession(output, world);

        await session.ExecuteAsync("cd /world/Nations/0/Capital");
        var originalHandle = session.CurrentHandle;
        nation.ReplaceCapital("Replacement Capital");
        await session.ExecuteAsync("get Name");

        Assert.NotEqual(originalHandle, session.CurrentHandle);
        Assert.Contains("Name = Replacement Capital", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task SuccessfulNullAndUnavailableLocationHaveDistinctOutput()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var host = _CreateHost();
        host.RegisterRoot("world", CyclicWorldFactory.Create());
        await using var session = new CliSession(host, output);

        await session.ExecuteAsync("cd /world/Nations/0");
        await session.ExecuteAsync("get OptionalNote");
        host.UnregisterRoot("world");
        await session.ExecuteAsync("get OptionalNote");

        var transcript = output.ToString();
        Assert.Contains("OptionalNote = null", transcript, StringComparison.Ordinal);
        Assert.Contains("error TARGET_MISSING", transcript, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CollectionListingPreservesPositionsKeysAndNullEntries()
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var host = new UIEngineHost([new ReflectionObjectDescriptorProvider()]);
        host.RegisterRoot("model", new _CollectionOutputModel());
        await using var session = new CliSession(host, output);

        await session.ExecuteAsync("cd /model");
        await session.ExecuteAsync("ls Items limit=3");

        var transcript = output.ToString();
        Assert.Contains("collection Items mode=SNAPSHOT", transcript, StringComparison.Ordinal);
        Assert.Contains("[0] key=empty null", transcript, StringComparison.Ordinal);
        Assert.Contains("[1] key=number value=7", transcript, StringComparison.Ordinal);
        Assert.Contains("[2] key=text value=hello", transcript, StringComparison.Ordinal);
    }

    private static CliSession _CreateSession(TextWriter output, World? world = null)
    {
        var host = _CreateHost();
        host.RegisterRoot("world", world ?? CyclicWorldFactory.Create());
        return new CliSession(host, output);
    }

    private static UIEngineHost _CreateHost() => new(new UIEngineHostOptions
    {
        Exposure = CyclicWorldFactory.CreateExposureRegistry(),
        DescriptorProviders = [new ReflectionObjectDescriptorProvider()],
    });

    private static int _CountLinesStartingWith(string text, string prefix) => text
        .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
        .Count(line => line.StartsWith(prefix, StringComparison.Ordinal));

    private sealed class _CancellableModel
    {
        private int _InvocationCount;

        [Action]
        public async Task WaitForCancellationAsync(
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            progress.Report(++_InvocationCount);
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
    }

    private sealed class _CollectionOutputModel
    {
        [Children]
        public Dictionary<string, object?> Items { get; } = new()
        {
            ["empty"] = null,
            ["number"] = 7,
            ["text"] = "hello",
        };
    }

    private sealed class _SignalingWriter : TextWriter
    {
        private readonly object _Gate = new();
        private readonly StringWriter _Writer = new(CultureInfo.InvariantCulture);
        private readonly TaskCompletionSource _Watching =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _Change =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _Progress =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override Encoding Encoding => Encoding.UTF8;

        public Task Watching => _Watching.Task;

        public Task Change => _Change.Task;

        public Task Progress => _Progress.Task;

        public override void WriteLine(string? value)
        {
            lock (_Gate)
            {
                _Writer.WriteLine(value);
            }

            if (value?.StartsWith("watching ", StringComparison.Ordinal) == true)
            {
                _Watching.TrySetResult();
            }

            if (value?.StartsWith("change ", StringComparison.Ordinal) == true)
            {
                _Change.TrySetResult();
            }

            if (value?.StartsWith("progress ", StringComparison.Ordinal) == true)
            {
                _Progress.TrySetResult();
            }
        }

        public override string ToString()
        {
            lock (_Gate)
            {
                return _Writer.ToString();
            }
        }
    }
}
