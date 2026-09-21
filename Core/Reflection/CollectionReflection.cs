using System.Collections;
using System.Collections.Specialized;

namespace UIEngine.Core;

internal static class CollectionReflection
{
    public static Type GetElementType(Type collectionType)
    {
        var dictionary = _FindGenericShape(
            collectionType,
            typeof(IDictionary<,>),
            typeof(IReadOnlyDictionary<,>));
        if (dictionary is not null)
        {
            return dictionary.GetGenericArguments()[1];
        }

        if (collectionType.IsArray)
        {
            return collectionType.GetElementType() ?? typeof(object);
        }

        return _FindGenericShape(collectionType, typeof(IEnumerable<>))
            ?.GetGenericArguments()[0] ?? typeof(object);
    }

    public static Type? GetKeyType(Type collectionType) => _FindGenericShape(
        collectionType,
        typeof(IDictionary<,>),
        typeof(IReadOnlyDictionary<,>))?.GetGenericArguments()[0] ??
        (typeof(IDictionary).IsAssignableFrom(collectionType) ? typeof(object) : null);

    public static bool TryGetCount(IEnumerable collection, out int count)
    {
        if (collection is ICollection nonGeneric)
        {
            count = nonGeneric.Count;
            return true;
        }

        var shape = _FindGenericShape(
            collection.GetType(),
            typeof(ICollection<>),
            typeof(IReadOnlyCollection<>));
        if (shape?.GetProperty(nameof(ICollection<object>.Count))?.GetValue(collection) is int value)
        {
            count = value;
            return true;
        }

        count = 0;
        return false;
    }

    public static bool TryGetIndexed(
        IEnumerable collection,
        out int count,
        out Func<int, object?> read)
    {
        if (collection is IList list)
        {
            count = list.Count;
            read = index => list[index];
            return true;
        }

        var shape = _FindGenericShape(
            collection.GetType(),
            typeof(IList<>),
            typeof(IReadOnlyList<>));
        var item = shape?.GetProperty("Item");
        if (item is not null && TryGetCount(collection, out count))
        {
            read = index => item.GetValue(collection, [index]);
            return true;
        }

        count = 0;
        read = static _ => null;
        return false;
    }

    public static (object? Key, object? Value) SplitEntry(IEnumerable collection, object? entry)
    {
        if (GetKeyType(collection.GetType()) is null)
        {
            return (null, entry);
        }

        if (entry is DictionaryEntry dictionaryEntry)
        {
            return (dictionaryEntry.Key, dictionaryEntry.Value);
        }

        if (entry is not null)
        {
            var type = entry.GetType();
            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(KeyValuePair<,>))
            {
                return (
                    type.GetProperty(nameof(KeyValuePair<object, object>.Key))!.GetValue(entry),
                    type.GetProperty(nameof(KeyValuePair<object, object>.Value))!.GetValue(entry));
            }
        }

        return (null, entry);
    }

    public static bool SupportsNotifications(Type collectionType) =>
        typeof(INotifyCollectionChanged).IsAssignableFrom(collectionType);

    private static Type? _FindGenericShape(Type type, params Type[] definitions) =>
        type.GetInterfaces()
            .Append(type)
            .Where(static candidate => candidate.IsGenericType)
            .FirstOrDefault(candidate => definitions.Contains(candidate.GetGenericTypeDefinition()));
}
