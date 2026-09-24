using System.Collections;
using System.Globalization;
using LanguageExt;
using static LanguageExt.Prelude;

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

    public Either<InteractionError, CollectionSlice> Read(long offset, int limit)
    {
        if (offset < 0 || limit <= 0)
        {
            return Left(new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                "Collection offset must be non-negative and limit must be positive."));
        }

        if (limit > Host.MaxCollectionItems)
        {
            return Left(new InteractionError(
                InteractionErrorCode.INVALID_INPUT,
                $"Collection limit {limit} exceeds the host maximum {Host.MaxCollectionItems}."));
        }

        return Host.Execute(
            $"read collection {Name}",
            () =>
            {
                var source = _ReadSource();
                return source.IsRight
                    ? _ReadSlice(
                        source.IfLeft(static error =>
                            throw new InvalidOperationException(error.Message)),
                        offset,
                        limit)
                    : Left((InteractionError)source);
            });
    }

    public Either<InteractionError, IReadOnlyList<Guid>> Select(ListLogicalPathSegment segment)
    {
        return Host.Execute(
            $"select from list {Name}",
            () =>
            {
                var source = _ReadSource();
                if (!source.IsRight)
                {
                    return Left((InteractionError)source);
                }

                var enumerable = source.IfLeft(
                    static error => throw new InvalidOperationException(error.Message));
                if (!CollectionReflection.IsList(enumerable.GetType()))
                {
                    return Left(new InteractionError(
                        InteractionErrorCode.TYPE_MISMATCH,
                        $"Collection '{Name}' does not provide list semantics."));
                }

                if (segment.Index > int.MaxValue)
                {
                    return Right((IReadOnlyList<Guid>)[]);
                }

                var slice = _ReadSlice(enumerable, segment.Index, 1);
                return slice.IsRight
                    ? _ReferenceHandles(((CollectionSlice)slice).Entries)
                    : Left((InteractionError)slice);
            });
    }

    public Either<InteractionError, IReadOnlyList<Guid>> Select(DictLogicalPathSegment segment)
    {
        return Host.Execute<IReadOnlyList<Guid>>(
            $"select from dictionary {Name}",
            () =>
            {
                var source = _ReadSource();
                if (!source.IsRight)
                {
                    return Left((InteractionError)source);
                }

                var enumerable = source.IfLeft(
                    static error => throw new InvalidOperationException(error.Message));
                if (CollectionReflection.GetKeyType(enumerable.GetType()) is null)
                {
                    return Left(new InteractionError(
                        InteractionErrorCode.TYPE_MISMATCH,
                        $"Collection '{Name}' does not provide dictionary semantics."));
                }

                var snapshot = _ReadSlice(enumerable, 0, Host.MaxCollectionItems);
                if (!snapshot.IsRight)
                {
                    return Left((InteractionError)snapshot);
                }

                var slice = (CollectionSlice)snapshot;
                if (slice.HasMore)
                {
                    return Left(new InteractionError(
                        InteractionErrorCode.UNSUPPORTED,
                        $"Collection '{Name}' is too large for bounded selector lookup."));
                }

                var matches = slice.Entries
                    .OfType<ReferenceCollectionEntry>()
                    .Where(entry => StringComparer.Ordinal.Equals(
                        Convert.ToString(entry.Key, CultureInfo.InvariantCulture),
                        segment.Key))
                    .Select(static entry => entry.Handle)
                    .ToArray();
                return Right((IReadOnlyList<Guid>)matches);
            });
    }

    private Either<InteractionError, IEnumerable> _ReadSource()
    {
        var target = Host.ResolveTarget(owner);
        if (!target.IsRight)
        {
            return Left((InteractionError)target);
        }

        return read(target.IfLeft(
            static error => throw new InvalidOperationException(error.Message))) is IEnumerable source
            ? Right(source)
            : Left(new InteractionError(
                InteractionErrorCode.UNAVAILABLE,
                $"Collection '{Name}' is null or unavailable."));
    }

    private Either<InteractionError, CollectionSlice> _ReadSlice(
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
                return Left(new InteractionError(
                    InteractionErrorCode.UNSUPPORTED,
                    "A non-indexed collection read would exceed the host's bounded scan limit."));
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

        return Right(
            new CollectionSlice(offset, entries, total, hasMore));
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

    private static Either<InteractionError, IReadOnlyList<Guid>> _ReferenceHandles(
        IReadOnlyList<CollectionEntry> entries)
    {
        if (entries.Count == 0)
        {
            return Right((IReadOnlyList<Guid>)[]);
        }

        return entries.All(static entry => entry is ReferenceCollectionEntry)
            ? Right((IReadOnlyList<Guid>)entries.Cast<ReferenceCollectionEntry>()
                    .Select(static entry => entry.Handle)
                    .ToArray())
            : Left(new InteractionError(
                InteractionErrorCode.TYPE_MISMATCH,
                "The collection selector does not identify a reference object."));
    }
}
