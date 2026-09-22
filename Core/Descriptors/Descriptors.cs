using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace UIEngine.Core;

public enum MemberKind
{
    VALUE,
    REFERENCE,
    COLLECTION,
    ACTION,
}

public abstract class MemberDescriptor
{
    private protected MemberDescriptor(
        UIEngineHost host,
        Guid owner,
        string id,
        MemberKind kind)
    {
        Host = host;
        Owner = owner;
        Id = id;
        Kind = kind;
    }

    internal UIEngineHost Host { get; }

    internal Guid Owner { get; }

    public string Id { get; }

    public MemberKind Kind { get; }
}

public sealed class ObjectDescriptor
{
    internal ObjectDescriptor(
        Guid handle,
        string? domainIdentity,
        string typeName,
        string? summary,
        IReadOnlyList<MemberDescriptor> members)
    {
        Handle = handle;
        DomainIdentity = domainIdentity;
        TypeName = typeName;
        Summary = summary;
        Members = members.ToArray();
    }

    public Guid Handle { get; }

    public string? DomainIdentity { get; }

    public string TypeName { get; }

    public string? Summary { get; }

    public IReadOnlyList<MemberDescriptor> Members { get; }
}

public sealed class ValueDescriptor : MemberDescriptor
{
    internal ValueDescriptor(
        UIEngineHost host,
        Guid owner,
        string id,
        Type valueType,
        bool canRead,
        bool canWrite,
        bool isNullable,
        IReadOnlyList<SelectionOption> options,
        IValueRange? range,
        IReadOnlyList<ValidationAttribute> validationAttributes,
        Func<object, object?> read,
        Action<object, object?>? write)
        : base(host, owner, id, MemberKind.VALUE)
    {
        Binding = new ValueNodeBinding(
            host,
            owner,
            id,
            valueType,
            canRead,
            canWrite,
            isNullable,
            options,
            range,
            validationAttributes,
            read,
            write);
    }

    internal ValueNodeBinding Binding { get; }

    public Type ValueType => Binding.ValueType;

    public bool CanRead => Binding.CanRead;

    public bool CanWrite => Binding.CanWrite;

    public bool IsNullable => Binding.IsNullable;

    public IReadOnlyList<SelectionOption> Options => Binding.Options;

    public IValueRange? Range => Binding.Range;

    public Task<InteractionResult<object?>> ReadAsync() => Binding.ReadAsync();

    public Task<InteractionResult<object?>> WriteAsync(object? value) => Binding.WriteAsync(value);
}

public sealed class ReferenceDescriptor : MemberDescriptor
{
    private readonly UIEngineHost _Host;
    private readonly Guid _Owner;
    private readonly Func<object, object?> _Read;

    internal ReferenceDescriptor(
        UIEngineHost host,
        Guid owner,
        string id,
        Type referenceType,
        Func<object, object?> read)
        : base(host, owner, id, MemberKind.REFERENCE)
    {
        _Host = host;
        _Owner = owner;
        _Read = read;
        ReferenceType = referenceType;
    }

    public Type ReferenceType { get; }

    public Task<InteractionResult<Guid?>> ReadAsync() => _Host.ExecuteAsync(
        $"read reference {Id}",
        async () =>
        {
            var target = _Host.ResolveTarget(_Owner);
            if (!target.IsSuccess)
            {
                return InteractionResult.Failure<Guid?>(target.Error!);
            }

            var value = _Read(target.Value);
            if (value is null)
            {
                return InteractionResult.Success<Guid?>(null);
            }

            if (value.GetType().IsValueType)
            {
                return InteractionResult.Failure<Guid?>(
                    InteractionErrorCode.TYPE_MISMATCH,
                    $"Reference '{Id}' returned a value type.");
            }

            var encountered = _Host.GetOrCreateHandle(value);
            var indexed = await _Host.GetDomainIdentityAsync(encountered);
            return indexed.IsSuccess
                ? InteractionResult.Success<Guid?>(encountered)
                : InteractionResult.Failure<Guid?>(indexed.Error!);
        });
}

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

public sealed class CollectionDescriptor : MemberDescriptor
{
    private readonly UIEngineHost _Host;
    private readonly Guid _Owner;
    private readonly Func<object, object?> _Read;

    internal CollectionDescriptor(
        UIEngineHost host,
        Guid owner,
        string id,
        Type collectionType,
        Func<object, object?> read)
        : base(host, owner, id, MemberKind.COLLECTION)
    {
        _Host = host;
        _Owner = owner;
        _Read = read;
        CollectionType = collectionType;
        ElementType = CollectionReflection.GetElementType(collectionType);
        KeyType = CollectionReflection.GetKeyType(collectionType);
    }

    internal Type CollectionType { get; }

    public Type ElementType { get; }

    public Type? KeyType { get; }

    public Task<InteractionResult<CollectionSlice>> ReadAsync(
        long offset,
        int limit)
    {
        if (offset < 0 || limit <= 0)
        {
            return Task.FromResult(InteractionResult.Failure<CollectionSlice>(
                InteractionErrorCode.INVALID_INPUT,
                "Collection offset must be non-negative and limit must be positive."));
        }

        if (limit > _Host.MaxCollectionItems)
        {
            return Task.FromResult(InteractionResult.Failure<CollectionSlice>(
                InteractionErrorCode.INVALID_INPUT,
                $"Collection limit {limit} exceeds the host maximum {_Host.MaxCollectionItems}."));
        }

        return _Host.ExecuteAsync(
            $"read collection {Id}",
            async () =>
            {
                var source = await _ReadSourceCoreAsync();
                if (!source.IsSuccess)
                {
                    return InteractionResult.Failure<CollectionSlice>(source.Error!);
                }

                return await _ReadSliceAsync(source.Value, offset, limit);
            });
    }

    internal Task<InteractionResult<IEnumerable>> ReadSourceAsync() => _Host.ExecuteAsync(
        $"read collection source {Id}",
        _ReadSourceCoreAsync);

    internal async Task<InteractionResult<IReadOnlyList<Guid>>> SelectAsync(
        CollectionSelector selector)
    {
        if (selector.Kind == CollectionSelectorKind.INDEX)
        {
            var index = long.Parse(selector.Value, NumberStyles.None, CultureInfo.InvariantCulture);
            if (index > int.MaxValue)
            {
                return InteractionResult.Success<IReadOnlyList<Guid>>([]);
            }

            var slice = await ReadAsync(index, 1);
            return slice.IsSuccess
                ? _ReferenceHandles(slice.Value.Entries)
                : InteractionResult.Failure<IReadOnlyList<Guid>>(slice.Error!);
        }

        var snapshot = await ReadAsync(0, _Host.MaxCollectionItems);
        if (!snapshot.IsSuccess)
        {
            return InteractionResult.Failure<IReadOnlyList<Guid>>(snapshot.Error!);
        }

        if (snapshot.Value.HasMore)
        {
            return InteractionResult.Failure<IReadOnlyList<Guid>>(
                InteractionErrorCode.UNSUPPORTED,
                $"Collection '{Id}' is too large for bounded selector lookup.");
        }

        var matches = snapshot.Value.Entries
            .OfType<ReferenceCollectionEntry>()
            .Where(entry => selector.Kind switch
            {
                CollectionSelectorKind.KEY => StringComparer.Ordinal.Equals(
                    Convert.ToString(entry.Key, CultureInfo.InvariantCulture),
                    selector.Value),
                CollectionSelectorKind.DOMAIN_IDENTITY => StringComparer.Ordinal.Equals(
                    entry.DomainIdentity,
                    selector.Value),
                _ => false,
            })
            .Select(static entry => entry.Handle)
            .ToArray();
        return InteractionResult.Success<IReadOnlyList<Guid>>(matches);
    }

    private Task<InteractionResult<IEnumerable>> _ReadSourceCoreAsync()
    {
        var target = _Host.ResolveTarget(_Owner);
        if (!target.IsSuccess)
        {
            return Task.FromResult(InteractionResult.Failure<IEnumerable>(target.Error!));
        }

        return Task.FromResult(_Read(target.Value) is IEnumerable source
            ? InteractionResult.Success(source)
            : InteractionResult.Failure<IEnumerable>(
                InteractionErrorCode.UNAVAILABLE,
                $"Collection '{Id}' is null or unavailable."));
    }

    private async Task<InteractionResult<CollectionSlice>> _ReadSliceAsync(
        IEnumerable source,
        long offset,
        int limit)
    {
        var entries = new List<CollectionEntry>(limit);
        long? total = null;
        var hasMore = false;
        if (CollectionReflection.TryGetIndexed(source, out var count, out var read))
        {
            total = count;
            var end = Math.Min((long)count, offset + limit);
            for (var index = offset; index < end; index++)
            {
                var split = CollectionReflection.SplitEntry(source, read((int)index));
                entries.Add(await _CreateEntryAsync(index, split.Key, split.Value));
            }

            hasMore = end < count;
        }
        else
        {
            if (offset + limit > _Host.MaxCollectionItems)
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
                entries.Add(await _CreateEntryAsync(position, split.Key, split.Value));
                position++;
            }
        }

        return InteractionResult.Success(new CollectionSlice(offset, entries, total, hasMore));
    }

    private async Task<CollectionEntry> _CreateEntryAsync(
        long position,
        object? key,
        object? value)
    {
        if (value is null)
        {
            return new NullCollectionEntry(position, key);
        }

        if (value.GetType().IsValueType || value is string)
        {
            return new ScalarCollectionEntry(position, value, key);
        }

        var handle = _Host.GetOrCreateHandle(value);
        var identity = await _Host.GetDomainIdentityAsync(handle);
        if (!identity.IsSuccess)
        {
            throw new InvalidOperationException(identity.Error!.Message);
        }

        return new ReferenceCollectionEntry(position, handle, identity.Value, key);
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
                entries.Cast<ReferenceCollectionEntry>().Select(static entry => entry.Handle).ToArray())
            : InteractionResult.Failure<IReadOnlyList<Guid>>(
                InteractionErrorCode.TYPE_MISMATCH,
                "The collection selector does not identify a reference object.");
    }
}

public sealed record ActionParameter(
    string Id,
    Type ParameterType,
    bool IsRequired,
    bool IsNullable,
    bool HasDefaultValue,
    object? DefaultValue,
    IReadOnlyList<SelectionOption> Options,
    IValueRange? Range);
