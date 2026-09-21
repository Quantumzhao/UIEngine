using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Reflection;

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
    private protected MemberDescriptor(string id, MemberKind kind)
    {
        Id = id;
        Kind = kind;
    }

    public string Id { get; }

    public MemberKind Kind { get; }
}

public sealed class ObjectDescriptor
{
    internal ObjectDescriptor(
        ObjectHandle handle,
        DomainIdentity? domainIdentity,
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

    public ObjectHandle Handle { get; }

    public DomainIdentity? DomainIdentity { get; }

    public string TypeName { get; }

    public string? Summary { get; }

    public IReadOnlyList<MemberDescriptor> Members { get; }
}

public sealed class ValueDescriptor : MemberDescriptor
{
    private readonly UIEngineHost _Host;
    private readonly ObjectHandle _Owner;
    private readonly Func<object, object?> _Read;
    private readonly Action<object, object?>? _Write;
    private readonly IReadOnlyList<ValidationAttribute> _ValidationAttributes;

    internal ValueDescriptor(
        UIEngineHost host,
        ObjectHandle owner,
        string id,
        Type valueType,
        bool canRead,
        bool canWrite,
        bool isNullable,
        IReadOnlyList<SelectionOption> options,
        ValueRange? range,
        IReadOnlyList<ValidationAttribute> validationAttributes,
        Func<object, object?> read,
        Action<object, object?>? write)
        : base(id, MemberKind.VALUE)
    {
        _Host = host;
        _Owner = owner;
        _Read = read;
        _Write = write;
        _ValidationAttributes = validationAttributes;
        ValueType = valueType;
        CanRead = canRead;
        CanWrite = canWrite;
        IsNullable = isNullable;
        Options = options.ToArray();
        Range = range;
    }

    public Type ValueType { get; }

    public bool CanRead { get; }

    public bool CanWrite { get; }

    public bool IsNullable { get; }

    public IReadOnlyList<SelectionOption> Options { get; }

    public ValueRange? Range { get; }

    public Task<InteractionResult<object?>> ReadAsync(
        CancellationToken cancellationToken = default) => _Host.ExecuteAsync(
        $"read value {Id}",
        async () =>
        {
            if (!CanRead)
            {
                return InteractionResult.Failure<object?>(
                    InteractionErrorCode.UNSUPPORTED,
                    $"Value '{Id}' is not readable.");
            }

            var target = _Host.ResolveTarget(_Owner);
            if (!target.IsSuccess)
            {
                return InteractionResult.Failure<object?>(target.Error!);
            }

            await Task.CompletedTask;
            return InteractionResult.Success(_Read(target.Value));
        },
        cancellationToken);

    public Task<InteractionResult<object?>> WriteAsync(
        object? value,
        CancellationToken cancellationToken = default) => _Host.ExecuteAsync(
        $"write value {Id}",
        async () =>
        {
            if (!CanWrite || _Write is null)
            {
                var message = $"Value '{Id}' is read-only.";
                return InteractionResult.Failure<object?>(
                    InteractionErrorCode.VALIDATION_FAILED,
                    message,
                    [new ValidationIssue(ValidationIssueCode.READ_ONLY, Id, message)]);
            }

            var target = _Host.ResolveTarget(_Owner);
            if (!target.IsSuccess)
            {
                return InteractionResult.Failure<object?>(target.Error!);
            }

            var converted = ValueConversion.Convert(value, ValueType);
            if (!converted.IsSuccess)
            {
                return InteractionResult.Failure<object?>(converted.Error!);
            }

            var issues = ValueConversion.Validate(
                converted.Value,
                IsNullable,
                Options,
                Range,
                _ValidationAttributes,
                target.Value,
                Id);
            if (issues.Count > 0)
            {
                return InteractionResult.Failure<object?>(
                    InteractionErrorCode.VALIDATION_FAILED,
                    $"Value '{Id}' failed validation.",
                    issues);
            }

            try
            {
                _Write(target.Value, converted.Value);
            }
            catch (TargetInvocationException exception)
                when (exception.InnerException is ArgumentException or InvalidOperationException)
            {
                return _SetterRejected(exception.InnerException);
            }
            catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
            {
                return _SetterRejected(exception);
            }

            await Task.CompletedTask;
            return InteractionResult.Success(converted.Value);
        },
        cancellationToken);

    private InteractionResult<object?> _SetterRejected(Exception exception) =>
        InteractionResult.Failure<object?>(
            InteractionErrorCode.VALIDATION_FAILED,
            exception.Message,
            [new ValidationIssue(ValidationIssueCode.RULE_FAILED, Id, exception.Message)]);
}

public sealed class ReferenceDescriptor : MemberDescriptor
{
    private readonly UIEngineHost _Host;
    private readonly ObjectHandle _Owner;
    private readonly Func<object, object?> _Read;

    internal ReferenceDescriptor(
        UIEngineHost host,
        ObjectHandle owner,
        string id,
        Type referenceType,
        Func<object, object?> read)
        : base(id, MemberKind.REFERENCE)
    {
        _Host = host;
        _Owner = owner;
        _Read = read;
        ReferenceType = referenceType;
    }

    public Type ReferenceType { get; }

    public Task<InteractionResult<ObjectHandle?>> ReadAsync(
        CancellationToken cancellationToken = default) => _Host.ExecuteAsync(
        $"read reference {Id}",
        async () =>
        {
            var target = _Host.ResolveTarget(_Owner);
            if (!target.IsSuccess)
            {
                return InteractionResult.Failure<ObjectHandle?>(target.Error!);
            }

            var value = _Read(target.Value);
            if (value is null)
            {
                return InteractionResult.Success<ObjectHandle?>(null);
            }

            if (value.GetType().IsValueType)
            {
                return InteractionResult.Failure<ObjectHandle?>(
                    InteractionErrorCode.TYPE_MISMATCH,
                    $"Reference '{Id}' returned a value type.");
            }

            var encountered = _Host.GetOrCreateHandle(value);
            var indexed = await _Host.GetDomainIdentityAsync(encountered, cancellationToken);
            return indexed.IsSuccess
                ? InteractionResult.Success<ObjectHandle?>(encountered)
                : InteractionResult.Failure<ObjectHandle?>(indexed.Error!);
        },
        cancellationToken);
}

public abstract record CollectionEntry(long Position, object? Key);

public sealed record NullCollectionEntry(long Position, object? Key = null)
    : CollectionEntry(Position, Key);

public sealed record ScalarCollectionEntry(long Position, object Value, object? Key = null)
    : CollectionEntry(Position, Key);

public sealed record ReferenceCollectionEntry(
    long Position,
    ObjectHandle Handle,
    DomainIdentity? DomainIdentity,
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
    private readonly ObjectHandle _Owner;
    private readonly Func<object, object?> _Read;

    internal CollectionDescriptor(
        UIEngineHost host,
        ObjectHandle owner,
        string id,
        Type collectionType,
        Func<object, object?> read)
        : base(id, MemberKind.COLLECTION)
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
        int limit,
        CancellationToken cancellationToken = default)
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

                return await _ReadSliceAsync(source.Value, offset, limit, cancellationToken);
            },
            cancellationToken);
    }

    internal Task<InteractionResult<IEnumerable>> ReadSourceAsync(
        CancellationToken cancellationToken = default) => _Host.ExecuteAsync(
        $"read collection source {Id}",
        _ReadSourceCoreAsync,
        cancellationToken);

    internal async Task<InteractionResult<IReadOnlyList<ObjectHandle>>> SelectAsync(
        CollectionSelector selector,
        CancellationToken cancellationToken)
    {
        if (selector.Kind == CollectionSelectorKind.INDEX)
        {
            var index = long.Parse(selector.Value, NumberStyles.None, CultureInfo.InvariantCulture);
            if (index > int.MaxValue)
            {
                return InteractionResult.Success<IReadOnlyList<ObjectHandle>>([]);
            }

            var slice = await ReadAsync(index, 1, cancellationToken);
            return slice.IsSuccess
                ? _ReferenceHandles(slice.Value.Entries)
                : InteractionResult.Failure<IReadOnlyList<ObjectHandle>>(slice.Error!);
        }

        var snapshot = await ReadAsync(0, _Host.MaxCollectionItems, cancellationToken);
        if (!snapshot.IsSuccess)
        {
            return InteractionResult.Failure<IReadOnlyList<ObjectHandle>>(snapshot.Error!);
        }

        if (snapshot.Value.HasMore)
        {
            return InteractionResult.Failure<IReadOnlyList<ObjectHandle>>(
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
                    entry.DomainIdentity?.Value,
                    selector.Value),
                _ => false,
            })
            .Select(static entry => entry.Handle)
            .ToArray();
        return InteractionResult.Success<IReadOnlyList<ObjectHandle>>(matches);
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
        int limit,
        CancellationToken cancellationToken)
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
                cancellationToken.ThrowIfCancellationRequested();
                var split = CollectionReflection.SplitEntry(source, read((int)index));
                entries.Add(await _CreateEntryAsync(index, split.Key, split.Value, cancellationToken));
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
                cancellationToken.ThrowIfCancellationRequested();
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
                entries.Add(await _CreateEntryAsync(position, split.Key, split.Value, cancellationToken));
                position++;
            }
        }

        return InteractionResult.Success(new CollectionSlice(offset, entries, total, hasMore));
    }

    private async Task<CollectionEntry> _CreateEntryAsync(
        long position,
        object? key,
        object? value,
        CancellationToken cancellationToken)
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
        var identity = await _Host.GetDomainIdentityAsync(handle, cancellationToken);
        if (!identity.IsSuccess)
        {
            throw new InvalidOperationException(identity.Error!.Message);
        }

        return new ReferenceCollectionEntry(position, handle, identity.Value, key);
    }

    private static InteractionResult<IReadOnlyList<ObjectHandle>> _ReferenceHandles(
        IReadOnlyList<CollectionEntry> entries)
    {
        if (entries.Count == 0)
        {
            return InteractionResult.Success<IReadOnlyList<ObjectHandle>>([]);
        }

        return entries.All(static entry => entry is ReferenceCollectionEntry)
            ? InteractionResult.Success<IReadOnlyList<ObjectHandle>>(
                entries.Cast<ReferenceCollectionEntry>().Select(static entry => entry.Handle).ToArray())
            : InteractionResult.Failure<IReadOnlyList<ObjectHandle>>(
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
    ValueRange? Range);
