using System.Globalization;
using System.Text;

namespace UIEngine.Core;

public interface ILogicalPathSegment
{
    string Name { get; }
}

public sealed record MemberLogicalPathSegment(string Name) : ILogicalPathSegment;

public sealed record ListLogicalPathSegment : ILogicalPathSegment
{
    public ListLogicalPathSegment(string name, long index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        Name = name;
        Index = index;
    }

    public string Name { get; }

    public long Index { get; }
}

public sealed record DictLogicalPathSegment : ILogicalPathSegment
{
    public DictLogicalPathSegment(string name, string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("A dictionary key cannot be empty.", nameof(key));
        }

        Name = name;
        Key = key;
    }

    public string Name { get; }

    public string Key { get; }
}

public sealed class LogicalPath : IEquatable<LogicalPath>
{
    private readonly ILogicalPathSegment[] _Segments;

    private LogicalPath(IEnumerable<ILogicalPathSegment> segments)
    {
        _Segments = [.. segments];
    }

    public static LogicalPath Root { get; } = new([]);

    public IReadOnlyList<ILogicalPathSegment> Segments => _Segments;

    /// <summary>
    /// The semantic parent node path, or <see langword="null"/> when this path identifies a root.
    /// A selected collection element has its collection path as its parent.
    /// </summary>
    public LogicalPath? Parent
    {
        get
        {
            if (_Segments.Length <= 1)
            {
                return null;
            }

            var final = _Segments[^1];
            if (final is MemberLogicalPathSegment)
            {
                return new LogicalPath(_Segments[..^1]);
            }

            var parent = (ILogicalPathSegment[])_Segments.Clone();
            parent[^1] = new MemberLogicalPathSegment(final.Name);
            return new LogicalPath(parent);
        }
    }

    public LogicalPath Append(string name) => Append(new MemberLogicalPathSegment(name));

    public LogicalPath Append(ILogicalPathSegment segment)
    {
        if (string.IsNullOrEmpty(segment.Name))
        {
            throw new ArgumentException("A path name cannot be empty.", nameof(segment));
        }

        if (_Segments.Length == 0 && segment is not MemberLogicalPathSegment)
        {
            throw new ArgumentException("A root cannot have a selector.", nameof(segment));
        }

        return new LogicalPath(_Segments.Append(segment));
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

        var segments = new List<ILogicalPathSegment>(encodedSegments.Length);
        for (var index = 0; index < encodedSegments.Length; index++)
        {
            var parsed = _ParseSegment(encodedSegments[index]);
            if (!parsed.IsSuccess)
            {
                return InteractionResult.Failure<LogicalPath>(parsed.Error!);
            }

            if (index == 0 && parsed.Value is not MemberLogicalPathSegment)
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

    private static InteractionResult<ILogicalPathSegment> _ParseSegment(string encoded)
    {
        var selectorStart = encoded.IndexOf('[');
        if (selectorStart < 0)
        {
            if (encoded.IndexOfAny([']', '=']) >= 0)
            {
                return _InvalidSegment("A path segment contains an unescaped delimiter.");
            }

            var name = _Decode(encoded);
            return name.IsSuccess
                ? InteractionResult.Success<ILogicalPathSegment>(
                    new MemberLogicalPathSegment(name.Value))
                : InteractionResult.Failure<ILogicalPathSegment>(name.Error!);
        }

        if (selectorStart == 0 || encoded[^1] != ']' ||
            encoded.IndexOf('[', selectorStart + 1) >= 0 ||
            encoded.IndexOf(']', selectorStart) != encoded.Length - 1)
        {
            return _InvalidSegment("A path selector is malformed.");
        }

        var nameResult = _Decode(encoded[..selectorStart]);
        if (!nameResult.IsSuccess)
        {
            return InteractionResult.Failure<ILogicalPathSegment>(nameResult.Error!);
        }

        var selectorText = encoded[(selectorStart + 1)..^1];
        var equals = selectorText.IndexOf('=');
        if (equals <= 0 || equals == selectorText.Length - 1 ||
            selectorText.IndexOf('=', equals + 1) >= 0)
        {
            return _InvalidSegment("A path selector is malformed.");
        }

        var value = _Decode(selectorText[(equals + 1)..]);
        if (!value.IsSuccess)
        {
            return InteractionResult.Failure<ILogicalPathSegment>(value.Error!);
        }

        try
        {
            return selectorText[..equals] switch
            {
                "index" when long.TryParse(
                    value.Value,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var index) => InteractionResult.Success<ILogicalPathSegment>(
                        new ListLogicalPathSegment(nameResult.Value, index)),
                "index" => _InvalidSegment(
                    "An index selector requires a non-negative integer."),
                "key" => InteractionResult.Success<ILogicalPathSegment>(
                    new DictLogicalPathSegment(nameResult.Value, value.Value)),
                _ => _InvalidSegment("A path selector kind is unknown."),
            };
        }
        catch (ArgumentException exception)
        {
            return _InvalidSegment(exception.Message);
        }
    }

    private static string _FormatSegment(ILogicalPathSegment segment)
    {
        var name = Escape(segment.Name);
        return segment switch
        {
            MemberLogicalPathSegment => name,
            ListLogicalPathSegment list =>
                $"{name}[index={list.Index.ToString(CultureInfo.InvariantCulture)}]",
            DictLogicalPathSegment dictionary =>
                $"{name}[key={Escape(dictionary.Key)}]",
            _ => throw new InvalidOperationException("Unknown logical path segment type."),
        };
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
                    "Path names and selector values cannot be empty.")
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

    private static InteractionResult<ILogicalPathSegment> _InvalidSegment(string message) =>
        InteractionResult.Failure<ILogicalPathSegment>(InteractionErrorCode.INVALID_INPUT, message);
}
