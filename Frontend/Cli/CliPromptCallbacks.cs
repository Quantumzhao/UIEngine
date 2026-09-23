using PrettyPrompt;
using PrettyPrompt.Completion;
using PrettyPrompt.Documents;

namespace UIEngine.Frontend.Cli;

/// <summary>Adapts live CLI command and node metadata to PrettyPrompt completions.</summary>
internal sealed class CliPromptCallbacks : PromptCallbacks
{
    private readonly CliSession _Session;

    public CliPromptCallbacks(CliSession session)
    {
        _Session = session;
    }

    protected override Task<TextSpan> GetSpanToReplaceByCompletionAsync(
        string text,
        int caret,
        CancellationToken _)
    {
        var start = caret;
        while (start > 0 && !char.IsWhiteSpace(text[start - 1]))
        {
            start--;
        }

        var end = caret;
        while (end < text.Length && !char.IsWhiteSpace(text[end]))
        {
            end++;
        }

        return Task.FromResult(TextSpan.FromBounds(start, end));
    }

    protected override Task<IReadOnlyList<CompletionItem>> GetCompletionItemsAsync(
        string text,
        int caret,
        TextSpan spanToBeReplaced,
        CancellationToken _)
    {
        var completions = _Session.GetCompletions(text, caret);
        return Task.FromResult<IReadOnlyList<CompletionItem>>(
            completions.Select(static completion => new CompletionItem(completion)).ToArray());
    }
}
