using System.Globalization;
using System.Text;

namespace UIEngine.Core;

/// <summary>Identifies the selector attached to one collection member in a logical path.</summary>
public enum CollectionSelectorKind
{
    INDEX,
    KEY,
    DOMAIN_IDENTITY,
}

/// <summary>Contains one decoded collection selector value.</summary>
public sealed record CollectionSelector
{
    public CollectionSelector(CollectionSelectorKind kind, string value)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown collection selector kind.");
        }

        ArgumentException.ThrowIfNullOrEmpty(value);
        if (kind == CollectionSelectorKind.INDEX)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
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

    public static CollectionSelector AtIndex(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return new CollectionSelector(
            CollectionSelectorKind.INDEX,
            index.ToString(CultureInfo.InvariantCulture));
    }
}

/// <summary>Contains one decoded semantic member segment and its optional collection selector.</summary>
public sealed record LogicalPathSegment(string Identifier, CollectionSelector? Selector = null);

/// <summary>Represents a canonical absolute path over exposed UIEngine semantics.</summary>
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
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        if (_Segments.Length == 0 && selector is not null)
        {
            throw new ArgumentException("A root alias cannot have a collection selector.", nameof(selector));
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
            return _Invalid($"Logical path '{path}' contains an empty segment.");
        }

        var segments = new List<LogicalPathSegment>(encodedSegments.Length);
        for (var index = 0; index < encodedSegments.Length; index++)
        {
            var parsed = _ParseSegment(encodedSegments[index]);
            if (!parsed.IsSuccess)
            {
                return InteractionResult.Failure<LogicalPath>(
                    parsed.Error!.Code,
                    parsed.Error.Message);
            }

            if (index == 0 && parsed.Value.Selector is not null)
            {
                return _Invalid("A root alias cannot have a collection selector.");
            }

            segments.Add(parsed.Value);
        }

        return InteractionResult.Success(new LogicalPath(segments));
    }

    public override string ToString()
    {
        if (_Segments.Length == 0)
        {
            return "/";
        }

        return "/" + string.Join('/', _Segments.Select(_FormatSegment));
    }

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
                return _InvalidSegment($"Logical path segment '{encoded}' contains an unescaped delimiter.");
            }

            var decoded = _Decode(encoded);
            return decoded.IsSuccess
                ? InteractionResult.Success(new LogicalPathSegment(decoded.Value))
                : InteractionResult.Failure<LogicalPathSegment>(decoded.Error!.Code, decoded.Error.Message);
        }

        if (selectorStart == 0 ||
            encoded[^1] != ']' ||
            encoded.IndexOf('[', selectorStart + 1) >= 0 ||
            encoded.IndexOf(']', selectorStart) != encoded.Length - 1)
        {
            return _InvalidSegment($"Logical path segment '{encoded}' has malformed selector syntax.");
        }

        var identifier = _Decode(encoded[..selectorStart]);
        if (!identifier.IsSuccess)
        {
            return InteractionResult.Failure<LogicalPathSegment>(identifier.Error!.Code, identifier.Error.Message);
        }

        var selectorText = encoded[(selectorStart + 1)..^1];
        var equalsIndex = selectorText.IndexOf('=');
        if (equalsIndex <= 0 ||
            equalsIndex == selectorText.Length - 1 ||
            selectorText.IndexOf('=', equalsIndex + 1) >= 0)
        {
            return _InvalidSegment($"Logical path segment '{encoded}' has malformed selector syntax.");
        }

        var kind = selectorText[..equalsIndex] switch
        {
            "index" => CollectionSelectorKind.INDEX,
            "key" => CollectionSelectorKind.KEY,
            "identity" => CollectionSelectorKind.DOMAIN_IDENTITY,
            _ => (CollectionSelectorKind?)null,
        };
        if (kind is null)
        {
            return _InvalidSegment($"Logical path segment '{encoded}' uses an unknown selector.");
        }

        var value = _Decode(selectorText[(equalsIndex + 1)..]);
        if (!value.IsSuccess)
        {
            return InteractionResult.Failure<LogicalPathSegment>(value.Error!.Code, value.Error.Message);
        }

        if (kind == CollectionSelectorKind.INDEX)
        {
            if (!int.TryParse(value.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var numericIndex))
            {
                return _InvalidSegment($"Collection index '{value.Value}' is not a non-negative integer.");
            }

            value = InteractionResult.Success(numericIndex.ToString(CultureInfo.InvariantCulture));
        }

        return InteractionResult.Success(new LogicalPathSegment(
            identifier.Value,
            new CollectionSelector(kind.Value, value.Value)));
    }

    private static string _FormatSegment(LogicalPathSegment segment)
    {
        var identifier = Escape(segment.Identifier);
        if (segment.Selector is null)
        {
            return identifier;
        }

        var selectorName = segment.Selector.Kind switch
        {
            CollectionSelectorKind.INDEX => "index",
            CollectionSelectorKind.KEY => "key",
            CollectionSelectorKind.DOMAIN_IDENTITY => "identity",
            _ => throw new InvalidOperationException("Unknown collection selector kind."),
        };
        return $"{identifier}[{selectorName}={Escape(segment.Selector.Value)}]";
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
                    InteractionErrorCode.INVALID_PATH,
                    $"Encoded path component '{encoded}' contains an invalid percent escape.");
            }

            index += 2;
        }

        string decoded;
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

            decoded = builder.ToString();
        }
        catch (DecoderFallbackException)
        {
            return InteractionResult.Failure<string>(
                InteractionErrorCode.INVALID_PATH,
                $"Encoded path component '{encoded}' is not valid UTF-8.");
        }

        return decoded.Length == 0
            ? InteractionResult.Failure<string>(
                InteractionErrorCode.INVALID_PATH,
                "Logical path identifiers and selector values cannot be empty.")
            : InteractionResult.Success(decoded);
    }

    private static InteractionResult<LogicalPath> _Invalid(string message) =>
        InteractionResult.Failure<LogicalPath>(InteractionErrorCode.INVALID_PATH, message);

    private static InteractionResult<LogicalPathSegment> _InvalidSegment(string message) =>
        InteractionResult.Failure<LogicalPathSegment>(InteractionErrorCode.INVALID_PATH, message);
}
