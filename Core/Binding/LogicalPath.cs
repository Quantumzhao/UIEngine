using System.Globalization;
using System.Text;

namespace UIEngine.Core;

public enum CollectionSelectorKind
{
    INDEX,
    KEY,
    DOMAIN_IDENTITY,
}

public sealed record CollectionSelector
{
    public CollectionSelector(CollectionSelectorKind kind, string value)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        if (string.IsNullOrEmpty(value))
        {
            throw new ArgumentException("A selector value cannot be empty.", nameof(value));
        }

        if (kind == CollectionSelectorKind.INDEX)
        {
            if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
            {
                throw new ArgumentException("An index selector requires a non-negative integer.", nameof(value));
            }

            value = index.ToString(CultureInfo.InvariantCulture);
        }

        Kind = kind;
        Value = value;
    }

    public CollectionSelectorKind Kind { get; }

    public string Value { get; }

    public static CollectionSelector AtIndex(long index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new CollectionSelector(
            CollectionSelectorKind.INDEX,
            index.ToString(CultureInfo.InvariantCulture));
    }
}

public sealed record LogicalPathSegment(string Identifier, CollectionSelector? Selector = null);

public sealed class LogicalPath : IEquatable<LogicalPath>
{
    private readonly LogicalPathSegment[] _Segments;

    private LogicalPath(IEnumerable<LogicalPathSegment> segments)
    {
        _Segments = segments.ToArray();
    }

    public static LogicalPath Root { get; } = new([]);

    public IReadOnlyList<LogicalPathSegment> Segments => _Segments;

    public LogicalPath Append(string identifier, CollectionSelector? selector = null)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            throw new ArgumentException("A path identifier cannot be empty.", nameof(identifier));
        }

        if (_Segments.Length == 0 && selector is not null)
        {
            throw new ArgumentException("A root cannot have a selector.", nameof(selector));
        }

        return new LogicalPath(_Segments.Append(new LogicalPathSegment(identifier, selector)));
    }

    public static InteractionResult<LogicalPath> Parse(string path)
    {
        if (string.IsNullOrEmpty(path) || path[0] != '/')
        {
            return _Invalid("A logical path must be absolute and begin with '/'.");
        }

        if (path == "/")
        {
            return InteractionResult.Success(Root);
        }

        var encodedSegments = path[1..].Split('/', StringSplitOptions.None);
        if (encodedSegments.Any(static segment => segment.Length == 0))
        {
            return _Invalid("A logical path cannot contain an empty segment.");
        }

        var segments = new List<LogicalPathSegment>(encodedSegments.Length);
        for (var index = 0; index < encodedSegments.Length; index++)
        {
            var parsed = _ParseSegment(encodedSegments[index]);
            if (!parsed.IsSuccess)
            {
                return InteractionResult.Failure<LogicalPath>(parsed.Error!);
            }

            if (index == 0 && parsed.Value.Selector is not null)
            {
                return _Invalid("A root cannot have a selector.");
            }

            segments.Add(parsed.Value);
        }

        return InteractionResult.Success(new LogicalPath(segments));
    }

    public override string ToString() => _Segments.Length == 0
        ? "/"
        : "/" + string.Join('/', _Segments.Select(_FormatSegment));

    public bool Equals(LogicalPath? other) =>
        other is not null && _Segments.SequenceEqual(other._Segments);

    public override bool Equals(object? obj) => obj is LogicalPath other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var segment in _Segments)
        {
            hash.Add(segment);
        }

        return hash.ToHashCode();
    }

    internal static string Escape(string value) => Uri.EscapeDataString(value);

    private static InteractionResult<LogicalPathSegment> _ParseSegment(string encoded)
    {
        var selectorStart = encoded.IndexOf('[');
        if (selectorStart < 0)
        {
            if (encoded.IndexOfAny([']', '=']) >= 0)
            {
                return _InvalidSegment("A path segment contains an unescaped delimiter.");
            }

            var identifier = _Decode(encoded);
            return identifier.IsSuccess
                ? InteractionResult.Success(new LogicalPathSegment(identifier.Value))
                : InteractionResult.Failure<LogicalPathSegment>(identifier.Error!);
        }

        if (selectorStart == 0 || encoded[^1] != ']' ||
            encoded.IndexOf('[', selectorStart + 1) >= 0 ||
            encoded.IndexOf(']', selectorStart) != encoded.Length - 1)
        {
            return _InvalidSegment("A path selector is malformed.");
        }

        var identifierResult = _Decode(encoded[..selectorStart]);
        if (!identifierResult.IsSuccess)
        {
            return InteractionResult.Failure<LogicalPathSegment>(identifierResult.Error!);
        }

        var selectorText = encoded[(selectorStart + 1)..^1];
        var equals = selectorText.IndexOf('=');
        if (equals <= 0 || equals == selectorText.Length - 1 ||
            selectorText.IndexOf('=', equals + 1) >= 0)
        {
            return _InvalidSegment("A path selector is malformed.");
        }

        var kind = selectorText[..equals] switch
        {
            "index" => CollectionSelectorKind.INDEX,
            "key" => CollectionSelectorKind.KEY,
            "identity" => CollectionSelectorKind.DOMAIN_IDENTITY,
            _ => (CollectionSelectorKind?)null,
        };
        if (kind is null)
        {
            return _InvalidSegment("A path selector kind is unknown.");
        }

        var value = _Decode(selectorText[(equals + 1)..]);
        if (!value.IsSuccess)
        {
            return InteractionResult.Failure<LogicalPathSegment>(value.Error!);
        }

        try
        {
            return InteractionResult.Success(new LogicalPathSegment(
                identifierResult.Value,
                new CollectionSelector(kind.Value, value.Value)));
        }
        catch (ArgumentException exception)
        {
            return _InvalidSegment(exception.Message);
        }
    }

    private static string _FormatSegment(LogicalPathSegment segment)
    {
        var identifier = Escape(segment.Identifier);
        if (segment.Selector is null)
        {
            return identifier;
        }

        var selector = segment.Selector.Kind switch
        {
            CollectionSelectorKind.INDEX => "index",
            CollectionSelectorKind.KEY => "key",
            CollectionSelectorKind.DOMAIN_IDENTITY => "identity",
            _ => throw new InvalidOperationException("Unknown collection selector kind."),
        };
        return $"{identifier}[{selector}={Escape(segment.Selector.Value)}]";
    }

    private static InteractionResult<string> _Decode(string encoded)
    {
        for (var index = 0; index < encoded.Length; index++)
        {
            if (encoded[index] != '%')
            {
                continue;
            }

            if (index + 2 >= encoded.Length ||
                !Uri.IsHexDigit(encoded[index + 1]) ||
                !Uri.IsHexDigit(encoded[index + 2]))
            {
                return InteractionResult.Failure<string>(
                    InteractionErrorCode.INVALID_INPUT,
                    "A path component contains an invalid percent escape.");
            }

            index += 2;
        }

        try
        {
            var builder = new StringBuilder(encoded.Length);
            for (var index = 0; index < encoded.Length; index++)
            {
                if (encoded[index] != '%')
                {
                    builder.Append(encoded[index]);
                    continue;
                }

                var bytes = new List<byte>();
                while (index < encoded.Length && encoded[index] == '%')
                {
                    bytes.Add(byte.Parse(
                        encoded.AsSpan(index + 1, 2),
                        NumberStyles.HexNumber,
                        CultureInfo.InvariantCulture));
                    index += 3;
                }

                index--;
                builder.Append(new UTF8Encoding(false, true).GetString(bytes.ToArray()));
            }

            return builder.Length == 0
                ? InteractionResult.Failure<string>(
                    InteractionErrorCode.INVALID_INPUT,
                    "Path identifiers and selector values cannot be empty.")
                : InteractionResult.Success(builder.ToString());
        }
        catch (DecoderFallbackException)
        {
            return InteractionResult.Failure<string>(
                InteractionErrorCode.INVALID_INPUT,
                "A path component is not valid UTF-8.");
        }
    }

    private static InteractionResult<LogicalPath> _Invalid(string message) =>
        InteractionResult.Failure<LogicalPath>(InteractionErrorCode.INVALID_INPUT, message);

    private static InteractionResult<LogicalPathSegment> _InvalidSegment(string message) =>
        InteractionResult.Failure<LogicalPathSegment>(InteractionErrorCode.INVALID_INPUT, message);
}
