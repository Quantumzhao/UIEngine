namespace UIEngine.Frontend.Cli;

/// <summary>Runs a CLI session from a plain text stream for redirected input and deterministic tests.</summary>
internal sealed class CliTextReaderRunner
{
    private readonly CliSession _Session;
    private readonly TextReader _Input;

    public CliTextReaderRunner(CliSession session, TextReader input)
    {
        _Session = session;
        _Input = input;
    }

    internal async Task RunAsync()
    {
        while (true)
        {
            var line = await _Input.ReadLineAsync();
            if (line is null ||
                !await _Session.ExecuteAsync(line))
            {
                return;
            }
        }
    }
}
