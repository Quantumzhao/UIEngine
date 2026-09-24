using System.Collections;
using System.Globalization;

namespace UIEngine.Core;

internal sealed class CollectionNodeBinding(
    UIEngineHost host,
    Guid owner,
    string name,
    Type collectionType,
    Func<object, object?> read)
{
    public UIEngineHost Host { get; } = host;

    public string Name { get; } = name;

    public Type CollectionType { get; } = collectionType;

    public Type ElementType { get; } = CollectionReflection.GetElementType(collectionType);

    public Type? KeyType { get; } = CollectionReflection.GetKeyType(collectionType);

    public InteractionResult<CollectionSlice> Read(long offset, int limit)
    {
        if (offset < 0 || limit <= 0)
        {
            return InteractionResult.Failure<CollectionSlice>(
                InteractionErrorCode.INVALID_INPUT,
                "Collection offset must be non-negative and limit must be positive.");
        }

        if (limit > Host.MaxCollectionItems)
        {
            return InteractionResult.Failure<CollectionSlice>(
                InteractionErrorCode.INVALID_INPUT,
                $"Collection limit {limit} exceeds the host maximum {Host.MaxCollectionItems}.");
        }

        return Host.Execute(
            $"read collection {Name}",
            () =>
            {
                var source = _ReadSource();
                return source.IsSuccess
                    ? _ReadSlice(source.Value, offset, limit)
                    : InteractionResult.Failure<CollectionSlice>(source.Error!);
            });
    }

    public InteractionResult<IReadOnlyList<Guid>> Select(ListLogicalPathSegment segment)
    {
        return Host.Execute(
            $"select from list {Name}",
            () =>
            {
                var source = _ReadSource();
                if (!source.IsSuccess)
                {
                    return InteractionResult.Failure<IReadOnlyList<Guid>>(source.Error!);
                }

                if (!CollectionReflection.IsList(source.Value.GetType()))
                {
                    return InteractionResult.Failure<IReadOnlyList<Guid>>(
                        InteractionErrorCode.TYPE_MISMATCH,
                        $"Collection '{Name}' does not provide list semantics.");
                }

                if (segment.Index > int.MaxValue)
                {
                    return InteractionResult.Success<IReadOnlyList<Guid>>([]);
                }

                var slice = _ReadSlice(source.Value, segment.Index, 1);
                return slice.IsSuccess
                    ? _ReferenceHandles(slice.Value.Entries)
                    : InteractionResult.Failure<IReadOnlyList<Guid>>(slice.Error!);
            });
    }

    public InteractionResult<IReadOnlyList<Guid>> Select(DictLogicalPathSegment segment)
    {
        return Host.Execute(
            $"select from dictionary {Name}",
            () =>
            {
                var source = _ReadSource();
                if (!source.IsSuccess)
                {
                    return InteractionResult.Failure<IReadOnlyList<Guid>>(source.Error!);
                }

                if (CollectionReflection.GetKeyType(source.Value.GetType()) is null)
                {
                    return InteractionResult.Failure<IReadOnlyList<Guid>>(
                        InteractionErrorCode.TYPE_MISMATCH,
                        $"Collection '{Name}' does not provide dictionary semantics.");
                }

                var snapshot = _ReadSlice(source.Value, 0, Host.MaxCollectionItems);
                if (!snapshot.IsSuccess)
                {
                    return InteractionResult.Failure<IReadOnlyList<Guid>>(snapshot.Error!);
                }

                if (snapshot.Value.HasMore)
                {
                    return InteractionResult.Failure<IReadOnlyList<Guid>>(
                        InteractionErrorCode.UNSUPPORTED,
                        $"Collection '{Name}' is too large for bounded selector lookup.");
                }

                var matches = snapshot.Value.Entries
                    .OfType<ReferenceCollectionEntry>()
                    .Where(entry => StringComparer.Ordinal.Equals(
                        Convert.ToString(entry.Key, CultureInfo.InvariantCulture),
                        segment.Key))
                    .Select(static entry => entry.Handle)
                    .ToArray();
                return InteractionResult.Success<IReadOnlyList<Guid>>(matches);
            });
    }

    private InteractionResult<IEnumerable> _ReadSource()
    {
        var target = Host.ResolveTarget(owner);
        if (!target.IsSuccess)
        {
            return InteractionResult.Failure<IEnumerable>(target.Error!);
        }

        return read(target.Value) is IEnumerable source
            ? InteractionResult.Success(source)
            : InteractionResult.Failure<IEnumerable>(
                InteractionErrorCode.UNAVAILABLE,
                $"Collection '{Name}' is null or unavailable.");
    }

    private InteractionResult<CollectionSlice> _ReadSlice(
        IEnumerable source,
        long offset,
        int limit)
    {
        var entries = new List<CollectionEntry>(limit);
        long? total = null;
        var hasMore = false;
        if (CollectionReflection.TryGetIndexed(source, out var count, out var indexedRead))
        {
            total = count;
            var end = Math.Min((long)count, offset + limit);
            for (var index = offset; index < end; index++)
            {
                var split = CollectionReflection.SplitEntry(source, indexedRead((int)index));
                entries.Add(_CreateEntry(index, split.Key, split.Value));
            }

            hasMore = end < count;
        }
        else
        {
            if (offset + limit > Host.MaxCollectionItems)
            {
                return InteractionResult.Failure<CollectionSlice>(
                    InteractionErrorCode.UNSUPPORTED,
                    "A non-indexed collection read would exceed the host's bounded scan limit.");
            }

            if (CollectionReflection.TryGetCount(source, out count))
            {
                total = count;
            }

            var position = 0L;
            foreach (var raw in source)
            {
                if (position < offset)
                {
                    position++;
                    continue;
                }

                if (entries.Count == limit)
                {
                    hasMore = true;
                    break;
                }

                var split = CollectionReflection.SplitEntry(source, raw);
                entries.Add(_CreateEntry(position, split.Key, split.Value));
                position++;
            }
        }

        return InteractionResult.Success(new CollectionSlice(offset, entries, total, hasMore));
    }

    private CollectionEntry _CreateEntry(long position, object? key, object? value)
    {
        if (value is null)
        {
            return new NullCollectionEntry(position, key);
        }

        if (value.GetType().IsValueType || value is string)
        {
            return new ScalarCollectionEntry(position, value, key);
        }

        return new ReferenceCollectionEntry(position, Host.GetOrCreateHandle(value), key);
    }

    private static InteractionResult<IReadOnlyList<Guid>> _ReferenceHandles(
        IReadOnlyList<CollectionEntry> entries)
    {
        if (entries.Count == 0)
        {
            return InteractionResult.Success<IReadOnlyList<Guid>>([]);
        }

        return entries.All(static entry => entry is ReferenceCollectionEntry)
            ? InteractionResult.Success<IReadOnlyList<Guid>>(
                entries.Cast<ReferenceCollectionEntry>()
                    .Select(static entry => entry.Handle)
                    .ToArray())
            : InteractionResult.Failure<IReadOnlyList<Guid>>(
                InteractionErrorCode.TYPE_MISMATCH,
                "The collection selector does not identify a reference object.");
    }
}
