using System.Globalization;
using LanguageExt;
using UIEngine.Core;
using static LanguageExt.Prelude;

namespace UIEngine.Frontend.Cli;

/// <summary>Parses the CLI's command-text path syntax into Core semantic path segments.</summary>
internal static class CliLogicalPathParser
{
    public static Either<InteractionError, LogicalPath> Parse(string text)
    {
        if (string.IsNullOrEmpty(text) || text[0] != '/')
        {
            return _Invalid("A CLI path must be absolute and begin with '/'.");
        }

        if (text == "/")
        {
            return Right(LogicalPath.Root);
        }

        var rawSegments = text[1..].Split('/', StringSplitOptions.None);
        if (rawSegments.Any(static segment => segment.Length == 0))
        {
            return _Invalid("A CLI path cannot contain an empty segment.");
        }

        var path = LogicalPath.Root;
        for (var index = 0; index < rawSegments.Length; index++)
        {
            var parsed = _AppendSegment(path, rawSegments[index], index == 0);
            if (!parsed.IsRight)
            {
                return parsed;
            }

            path = (LogicalPath)parsed;
        }

        return Right(path);
    }

    private static Either<InteractionError, LogicalPath> _AppendSegment(
        LogicalPath path,
        string raw,
        bool isRoot)
    {
        var selectorStart = raw.IndexOf('[');
        if (selectorStart < 0)
        {
            return raw.IndexOfAny([']', '=']) >= 0
                ? _Invalid("A CLI path segment contains an unexpected delimiter.")
                : Right(path.Append(raw));
        }

        if (isRoot)
        {
            return _Invalid("A registered root cannot have a collection selection.");
        }

        if (selectorStart == 0 || raw[^1] != ']' ||
            raw.IndexOf('[', selectorStart + 1) >= 0 ||
            raw.IndexOf(']', selectorStart) != raw.Length - 1)
        {
            return _Invalid("A CLI collection selection is malformed.");
        }

        var selector = raw[(selectorStart + 1)..^1];
        var equals = selector.IndexOf('=');
        if (equals <= 0 || equals == selector.Length - 1 ||
            selector.IndexOf('=', equals + 1) >= 0)
        {
            return _Invalid("A CLI collection selection is malformed.");
        }

        path = path.Append(raw[..selectorStart]);
        var value = selector[(equals + 1)..];
        return selector[..equals] switch
        {
            "index" when long.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var parsedIndex) => Right(
                    path.Append(new ListLogicalPathSegment(parsedIndex))),
            "index" => _Invalid("A list index requires a non-negative integer."),
            "key" => Right(
                path.Append(new DictLogicalPathSegment(value))),
            _ => _Invalid("A CLI collection selection kind is unknown."),
        };
    }

    private static Either<InteractionError, LogicalPath> _Invalid(string message) =>
        Left(new InteractionError(
            InteractionErrorCode.INVALID_INPUT,
            message));
}
