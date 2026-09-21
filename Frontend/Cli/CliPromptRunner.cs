using PrettyPrompt;

namespace UIEngine.Frontend.Cli;

/// <summary>Runs a CLI session with interactive editing, history, and completion.</summary>
internal sealed class CliPromptRunner
{
    // PrettyPrompt reserves completion-pane rows even while the pane is closed.
    private const int MAX_COMPLETION_ITEMS = 3;
    private const double MAX_COMPLETION_HEIGHT_PROPORTION = 0.25;

    private readonly CliSession _Session;

    public CliPromptRunner(CliSession session)
    {
        _Session = session;
    }

    internal async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var configuration = CreateConfiguration();
        await using var prompt = new Prompt(
            callbacks: new CliPromptCallbacks(_Session),
            configuration: configuration);

        while (!cancellationToken.IsCancellationRequested)
        {
            configuration.Prompt = $"{_Session.CurrentPath}> ";
            var response = await prompt.ReadLineAsync();
            if (!response.IsSuccess)
            {
                continue;
            }

            using var commandCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                response.CancellationToken);
            if (!await _Session.ExecuteAsync(response.Text, commandCancellation.Token))
            {
                return;
            }
        }
    }

    internal static PromptConfiguration CreateConfiguration() => new(
        maxCompletionItemsCount: MAX_COMPLETION_ITEMS,
        proportionOfWindowHeightForCompletionPane: MAX_COMPLETION_HEIGHT_PROPORTION);
}
