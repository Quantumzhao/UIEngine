using System.Globalization;
using System.Text;

namespace UIEngine.Core;

/// <summary>One semantic step in a logical path.</summary>
public interface ILogicalPathSegment;

/// <summary>Selects a named root or exposed member.</summary>
public sealed record MemberLogicalPathSegment : ILogicalPathSegment
{
    public MemberLogicalPathSegment(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("A member name cannot be empty.", nameof(name));
        }

        Name = name;
    }

    public string Name { get; }
}

/// <summary>Selects one element from the list at the parent path.</summary>
public sealed record ListLogicalPathSegment : ILogicalPathSegment
{
    public ListLogicalPathSegment(long index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        Index = index;
    }

    public long Index { get; }
}

/// <summary>Selects one value from the dictionary at the parent path.</summary>
public sealed record DictLogicalPathSegment : ILogicalPathSegment
{
    public DictLogicalPathSegment(string key)
    {
        if (string.IsNullOrEmpty(key))
        {
            throw new ArgumentException("A dictionary key cannot be empty.", nameof(key));
        }

        Key = key;
    }

    public string Key { get; }
}

/// <summary>
/// One immutable semantic location represented as a persistent linked chain of path segments.
/// </summary>
public sealed class LogicalPath : IEquatable<LogicalPath>
{
    private LogicalPath(
        LogicalPath? parent,
        ILogicalPathSegment? segment,
        int count)
    {
        Parent = parent;
        Segment = segment;
        Count = count;
    }

    public static LogicalPath Root { get; } = new(null, null, 0);

    /// <summary>The semantic parent location, or null for a root node or the empty path.</summary>
    public LogicalPath? Parent { get; }

    /// <summary>The final semantic step, or null for the empty path.</summary>
    public ILogicalPathSegment? Segment { get; }

    public int Count { get; }

    public bool IsRoot => Segment is null;

    public IReadOnlyList<ILogicalPathSegment> Segments
    {
        get
        {
            var segments = new ILogicalPathSegment[Count];
            var current = this;
            for (var index = Count - 1; index >= 0; index--)
            {
                segments[index] = current.Segment!;
                current = current.Parent!;
            }

            return segments;
        }
    }

    public LogicalPath Append(string name) => Append(new MemberLogicalPathSegment(name));

    public LogicalPath Append(ILogicalPathSegment segment)
    {
        if (IsRoot && segment is not MemberLogicalPathSegment)
        {
            throw new ArgumentException(
                "The first path segment must identify a registered root.",
                nameof(segment));
        }

        return new LogicalPath(IsRoot ? null : this, segment, Count + 1);
    }

    /// <summary>Returns a human-readable diagnostic representation; it is not a serialization format.</summary>
    public override string ToString()
    {
        if (IsRoot)
        {
            return "/";
        }

        var builder = new StringBuilder();
        foreach (var segment in Segments)
        {
            switch (segment)
            {
                case MemberLogicalPathSegment member:
                    builder.Append('/').Append(member.Name);
                    break;
                case ListLogicalPathSegment list:
                    builder.Append("[index=")
                        .Append(list.Index.ToString(CultureInfo.InvariantCulture))
                        .Append(']');
                    break;
                case DictLogicalPathSegment dictionary:
                    builder.Append("[key=").Append(dictionary.Key).Append(']');
                    break;
                default:
                    throw new InvalidOperationException("Unknown logical path segment type.");
            }
        }

        return builder.ToString();
    }

    public bool Equals(LogicalPath? other) =>
        other is not null && Count == other.Count && Segments.SequenceEqual(other.Segments);

    public override bool Equals(object? obj) => obj is LogicalPath other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var segment in Segments)
        {
            hash.Add(segment);
        }

        return hash.ToHashCode();
    }
}
