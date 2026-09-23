namespace UIEngine.Core;

public abstract record CollectionEntry(long Position, object? Key);

public sealed record NullCollectionEntry(long Position, object? Key = null)
    : CollectionEntry(Position, Key);

public sealed record ScalarCollectionEntry(long Position, object Value, object? Key = null)
    : CollectionEntry(Position, Key);

public sealed record ReferenceCollectionEntry(
    long Position,
    Guid Handle,
    string? DomainIdentity,
    object? Key = null)
    : CollectionEntry(Position, Key);

public sealed record CollectionSlice(
    long Offset,
    IReadOnlyList<CollectionEntry> Entries,
    long? TotalCount,
    bool HasMore);
