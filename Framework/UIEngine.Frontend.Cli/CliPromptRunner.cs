using PrettyPrompt;

namespace UIEngine.Frontend.Cli;

/// <summary>Runs a CLI session with interactive editing, history, and completion.</summary>
internal sealed class CliPromptRunner
{
    private readonly CliSession _Session;

    public CliPromptRunner(CliSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        _Session = session;
    }

    internal async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var configuration = new PromptConfiguration();
        await using var prompt = new Prompt(
            callbacks: new CliPromptCallbacks(_Session),
            configuration: configuration);

        while (!cancellationToken.IsCancellationRequested)
        {
            configuration.Prompt = $"{_Session.CurrentPath}> ";
            var response = await prompt.ReadLineAsync().ConfigureAwait(false);
            if (!response.IsSuccess)
            {
                continue;
            }

            using var commandCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                response.CancellationToken);
            if (!await _Session.ExecuteAsync(response.Text, commandCancellation.Token)
                    .ConfigureAwait(false))
            {
                return;
            }
        }
    }
}
