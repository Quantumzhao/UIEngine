namespace UIEngine.Core;

[Flags]
public enum CollectionCapabilities
{
    NONE = 0,
    FINITE_SNAPSHOT = 1 << 0,
    PAGING = 1 << 1,
    VIRTUALIZED_RANGE = 1 << 2,
    INDEXED = 1 << 3,
    KEYED = 1 << 4,
    LIVE_OBSERVATION = 1 << 5,
}

public enum CollectionAccessMode
{
    SNAPSHOT = 0,
    PAGE = 1,
    VIRTUALIZED_RANGE = 2,
}

public enum CollectionEntryKind
{
    NULL = 0,
    SCALAR = 1,
    REFERENCE = 2,
}

/// <summary>Identifies a provider key separately from a possibly null entry value.</summary>
public sealed record CollectionEntryKey(object? Value);

/// <summary>Represents one collection value without requiring a wrapper domain object.</summary>
public sealed record CollectionEntry
{
    private CollectionEntry(
        long position,
        CollectionEntryKind kind,
        object? scalarValue,
        ObjectHandle? reference,
        DomainIdentity? domainIdentity,
        CollectionEntryKey? key)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        Position = position;
        Kind = kind;
        ScalarValue = scalarValue;
        Reference = reference;
        DomainIdentity = domainIdentity;
        Key = key;
    }

    public long Position { get; }

    public CollectionEntryKey? Key { get; }

    public CollectionEntryKind Kind { get; }

    public object? ScalarValue { get; }

    public ObjectHandle? Reference { get; }

    public DomainIdentity? DomainIdentity { get; }

    public static CollectionEntry Null(long position, CollectionEntryKey? key = null) =>
        new(position, CollectionEntryKind.NULL, null, null, null, key);

    public static CollectionEntry Scalar(
        long position,
        object value,
        CollectionEntryKey? key = null)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(position, CollectionEntryKind.SCALAR, value, null, null, key);
    }

    public static CollectionEntry ReferenceValue(
        long position,
        ObjectHandle reference,
        DomainIdentity? domainIdentity = null,
        CollectionEntryKey? key = null) =>
        new(position, CollectionEntryKind.REFERENCE, null, reference, domainIdentity, key);
}

/// <summary>Requests one explicitly bounded collection access operation.</summary>
public sealed record CollectionReadRequest
{
    public CollectionReadRequest(
        CollectionAccessMode mode,
        long offset,
        int limit,
        string? continuationToken = null)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);
        if (mode == CollectionAccessMode.SNAPSHOT && offset != 0)
        {
            throw new ArgumentException("Snapshot requests must start at offset zero.", nameof(offset));
        }

        if (mode != CollectionAccessMode.PAGE && continuationToken is not null)
        {
            throw new ArgumentException(
                "Continuation tokens are supported only by page requests.",
                nameof(continuationToken));
        }

        Mode = mode;
        Offset = offset;
        Limit = limit;
        ContinuationToken = continuationToken;
    }

    public CollectionAccessMode Mode { get; }

    public long Offset { get; }

    public int Limit { get; }

    public string? ContinuationToken { get; }

    public static CollectionReadRequest Snapshot(int maxItems) =>
        new(CollectionAccessMode.SNAPSHOT, 0, maxItems);

    public static CollectionReadRequest Page(
        long offset,
        int limit,
        string? continuationToken = null) =>
        new(CollectionAccessMode.PAGE, offset, limit, continuationToken);

    public static CollectionReadRequest Range(long offset, int limit) =>
        new(CollectionAccessMode.VIRTUALIZED_RANGE, offset, limit);
}

/// <summary>Returns one bounded collection result and the metadata needed for a next request.</summary>
public sealed record CollectionReadResult
{
    public CollectionReadResult(
        CollectionAccessMode mode,
        IReadOnlyList<CollectionEntry> entries,
        long offset,
        long? totalCount,
        bool hasMore,
        string? continuationToken = null)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        if (totalCount is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCount));
        }

        if (mode != CollectionAccessMode.PAGE && continuationToken is not null)
        {
            throw new ArgumentException(
                "Continuation tokens are supported only by page results.",
                nameof(continuationToken));
        }

        if (!hasMore && continuationToken is not null)
        {
            throw new ArgumentException(
                "A terminal result cannot include a continuation token.",
                nameof(continuationToken));
        }

        Mode = mode;
        Entries = entries.ToArray();
        Offset = offset;
        TotalCount = totalCount;
        HasMore = hasMore;
        ContinuationToken = continuationToken;
    }

    public CollectionAccessMode Mode { get; }

    public IReadOnlyList<CollectionEntry> Entries { get; }

    public long Offset { get; }

    public long? TotalCount { get; }

    public bool HasMore { get; }

    public string? ContinuationToken { get; }
}
