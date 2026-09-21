using System.Text;
using UIEngine.Core;

namespace UIEngine.Frontend.Cli;

/// <summary>Splits a CLI command line while preserving whitespace inside double quotes.</summary>
internal static class CliTokenizer
{
    public static InteractionResult<IReadOnlyList<string>> Tokenize(string line)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;
        var escaping = false;
        var tokenStarted = false;

        foreach (var character in line)
        {
            if (escaping)
            {
                current.Append(character);
                escaping = false;
                tokenStarted = true;
                continue;
            }

            if (inQuotes && character == '\\')
            {
                escaping = true;
                continue;
            }

            if (character == '"')
            {
                inQuotes = !inQuotes;
                tokenStarted = true;
                continue;
            }

            if (char.IsWhiteSpace(character) && !inQuotes)
            {
                _AddToken(tokens, current, ref tokenStarted);
                continue;
            }

            current.Append(character);
            tokenStarted = true;
        }

        if (inQuotes || escaping)
        {
            return InteractionResult.Failure<IReadOnlyList<string>>(
                InteractionErrorCode.INVALID_INPUT,
                "The command contains an unterminated quoted value.");
        }

        _AddToken(tokens, current, ref tokenStarted);
        return InteractionResult.Success<IReadOnlyList<string>>(tokens);
    }

    private static void _AddToken(
        List<string> tokens,
        StringBuilder current,
        ref bool tokenStarted)
    {
        if (!tokenStarted)
        {
            return;
        }

        tokens.Add(current.ToString());
        current.Clear();
        tokenStarted = false;
    }
}
