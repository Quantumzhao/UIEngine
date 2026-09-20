using System.Collections;
using System.Collections.ObjectModel;
using UIEngine.Core;
using UIEngine.Core.Attributes;
using UIEngine.Core.Reflection;
using Xunit;

namespace UIEngine.Framework.Tests;

/// <summary>Verifies bounded collection contracts, reflection adaptation, and native providers.</summary>
public sealed class CollectionAccessTests
{
    [Fact]
    public async Task ReflectionAdvertisesOnlyCapabilitiesSupportedByTheDeclaredShape()
    {
        var model = new _CollectionModel();
        using var host = _CreateReflectionHost();
        var descriptor = await _DescribeAsync(host, model);

        var array = _Collection(descriptor, nameof(_CollectionModel.ArrayItems));
        var dictionary = _Collection(descriptor, nameof(_CollectionModel.DictionaryItems));
        var observable = _Collection(descriptor, nameof(_CollectionModel.ObservableItems));
        var legacy = _Collection(descriptor, nameof(_CollectionModel.LegacyItems));
        var lazy = _Collection(descriptor, nameof(_CollectionModel.LazyItems));

        Assert.Equal(
            CollectionCapabilities.FINITE_SNAPSHOT |
                CollectionCapabilities.VIRTUALIZED_RANGE |
                CollectionCapabilities.INDEXED,
            array.Capabilities);
        Assert.Equal(typeof(object), array.ElementType);
        Assert.Null(array.KeyType);

        Assert.True(dictionary.Capabilities.HasFlag(CollectionCapabilities.FINITE_SNAPSHOT));
        Assert.True(dictionary.Capabilities.HasFlag(CollectionCapabilities.KEYED));
        Assert.False(dictionary.Capabilities.HasFlag(CollectionCapabilities.VIRTUALIZED_RANGE));
        Assert.Equal(typeof(string), dictionary.KeyType);
        Assert.Equal(typeof(object), dictionary.ElementType);

        Assert.True(observable.Capabilities.HasFlag(CollectionCapabilities.FINITE_SNAPSHOT));
        Assert.True(observable.Capabilities.HasFlag(CollectionCapabilities.VIRTUALIZED_RANGE));
        Assert.True(observable.Capabilities.HasFlag(CollectionCapabilities.LIVE_OBSERVATION));
        Assert.True(legacy.Capabilities.HasFlag(CollectionCapabilities.FINITE_SNAPSHOT));
        Assert.True(legacy.Capabilities.HasFlag(CollectionCapabilities.VIRTUALIZED_RANGE));
        var legacyRange = await legacy.ReadAsync(CollectionReadRequest.Range(0, 2));
        Assert.Equal(CollectionEntryKind.NULL, legacyRange.Value.Entries[0].Kind);
        Assert.Equal(2, legacyRange.Value.Entries[1].ScalarValue);
        Assert.Equal(CollectionCapabilities.NONE, lazy.Capabilities);
        Assert.Equal(0, model.LazyItems.EnumerationCount);
    }

    [Fact]
    public async Task ReflectionRangeRepresentsNullScalarAndReferenceEntriesWithIdentity()
    {
        var identified = new _Identified("item/1");
        var model = new _CollectionModel
        {
            ArrayItems = [null, 7, "text", identified],
        };
        using var host = _CreateReflectionHost();
        var collection = _Collection(
            await _DescribeAsync(host, model),
            nameof(_CollectionModel.ArrayItems));

        var first = await collection.ReadAsync(CollectionReadRequest.Range(0, 3));
        var second = await collection.ReadAsync(CollectionReadRequest.Range(3, 3));

        Assert.True(first.IsSuccess);
        Assert.Equal(4, first.Value.TotalCount);
        Assert.True(first.Value.HasMore);
        Assert.Equal([0L, 1L, 2L], first.Value.Entries.Select(static entry => entry.Position));
        Assert.Equal(CollectionEntryKind.NULL, first.Value.Entries[0].Kind);
        Assert.Equal(7, first.Value.Entries[1].ScalarValue);
        Assert.Equal("text", first.Value.Entries[2].ScalarValue);

        var reference = Assert.Single(second.Value.Entries);
        Assert.Equal(CollectionEntryKind.REFERENCE, reference.Kind);
        Assert.Equal(new DomainIdentity("item/1"), reference.DomainIdentity);
        Assert.Same(identified, _Resolve(host, reference.Reference!.Value));
        Assert.False(second.Value.HasMore);
    }

    [Fact]
    public async Task DictionarySnapshotPreservesKeysAndAllValueKinds()
    {
        var referenced = new object();
        var model = new _CollectionModel
        {
            DictionaryItems = new Dictionary<string, object?>
            {
                ["empty"] = null,
                ["count"] = 3,
                ["child"] = referenced,
            },
        };
        using var host = _CreateReflectionHost();
        var collection = _Collection(
            await _DescribeAsync(host, model),
            nameof(_CollectionModel.DictionaryItems));

        var result = await collection.ReadAsync(CollectionReadRequest.Snapshot(10));

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.TotalCount);
        var byKey = result.Value.Entries.ToDictionary(
            static entry => Assert.IsType<string>(entry.Key?.Value));
        Assert.Equal(CollectionEntryKind.NULL, byKey["empty"].Kind);
        Assert.Equal(3, byKey["count"].ScalarValue);
        Assert.Equal(host.GetOrCreateHandle(referenced), byKey["child"].Reference);
    }

    [Fact]
    public async Task ReflectionReadsCurrentCollectionStateWithoutEagerEnumeration()
    {
        var model = new _CollectionModel();
        using var host = _CreateReflectionHost(new CollectionAccessLimits
        {
            MaxPageSize = 3,
            MaxSnapshotSize = 4,
        });
        var descriptor = await _DescribeAsync(host, model);
        var indexed = _Collection(descriptor, nameof(_CollectionModel.IndexedItems));
        var finite = _Collection(descriptor, nameof(_CollectionModel.FiniteItems));
        var lazy = _Collection(descriptor, nameof(_CollectionModel.LazyItems));

        var range = await indexed.ReadAsync(CollectionReadRequest.Range(2, 3));
        var oversizedSnapshot = await finite.ReadAsync(CollectionReadRequest.Snapshot(2));
        var unavailableLazySnapshot = await lazy.ReadAsync(CollectionReadRequest.Snapshot(1));

        Assert.Equal([2, 3, 4], range.Value.Entries.Select(static entry => entry.ScalarValue));
        Assert.Equal(3, model.IndexedItems.IndexReadCount);
        Assert.Equal(0, model.IndexedItems.EnumerationCount);
        Assert.Equal(InteractionErrorCode.COLLECTION_LIMIT_EXCEEDED, oversizedSnapshot.Error?.Code);
        Assert.Equal(0, model.FiniteItems.EnumerationCount);
        Assert.Equal(InteractionErrorCode.COLLECTION_ACCESS_UNSUPPORTED, unavailableLazySnapshot.Error?.Code);
        Assert.Equal(0, model.LazyItems.EnumerationCount);

        model.IndexedItems.Add(5);
        var afterMutation = await indexed.ReadAsync(CollectionReadRequest.Range(5, 1));
        Assert.Equal(6, afterMutation.Value.TotalCount);
        Assert.Equal(5, Assert.Single(afterMutation.Value.Entries).ScalarValue);
    }

    [Fact]
    public async Task HostEnforcesBoundsAndCancellationBeforeTouchingACollection()
    {
        var model = new _CollectionModel();
        using var host = _CreateReflectionHost(new CollectionAccessLimits
        {
            MaxPageSize = 2,
            MaxSnapshotSize = 4,
        });
        var indexed = _Collection(
            await _DescribeAsync(host, model),
            nameof(_CollectionModel.IndexedItems));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var overLimit = await indexed.ReadAsync(CollectionReadRequest.Range(0, 3));
        var cancelled = await indexed.ReadAsync(
            CollectionReadRequest.Range(0, 1),
            cancellation.Token);

        Assert.Equal(InteractionErrorCode.COLLECTION_LIMIT_EXCEEDED, overLimit.Error?.Code);
        Assert.Equal(InteractionErrorCode.CANCELLED, cancelled.Error?.Code);
        Assert.Equal(0, model.IndexedItems.IndexReadCount);
        Assert.Equal(0, model.IndexedItems.EnumerationCount);
    }

    [Fact]
    public async Task CustomDescriptorCanProvideNativePagingAndVirtualizedRanges()
    {
        var nativeCollection = new _NativeCollectionDescriptor();
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            DescriptorProviders = [new _NativeProvider(nativeCollection)],
            CollectionLimits = new CollectionAccessLimits
            {
                MaxPageSize = 2,
                MaxSnapshotSize = 4,
            },
        });
        var descriptor = await _DescribeAsync(host, new _NativeModel());
        var collection = Assert.Single(descriptor.Collections);

        var page = await collection.ReadAsync(CollectionReadRequest.Page(0, 2));
        var range = await collection.ReadAsync(CollectionReadRequest.Range(2, 2));
        var rejected = await collection.ReadAsync(CollectionReadRequest.Page(0, 3));

        Assert.Equal(
            CollectionCapabilities.PAGING | CollectionCapabilities.VIRTUALIZED_RANGE,
            collection.Capabilities);
        Assert.Null(page.Value.TotalCount);
        Assert.True(page.Value.HasMore);
        Assert.Equal("2", page.Value.ContinuationToken);
        Assert.Equal([10, 11], page.Value.Entries.Select(static entry => entry.ScalarValue));
        Assert.Equal(5, range.Value.TotalCount);
        Assert.Equal([12, 13], range.Value.Entries.Select(static entry => entry.ScalarValue));
        Assert.Equal(InteractionErrorCode.COLLECTION_LIMIT_EXCEEDED, rejected.Error?.Code);
        Assert.Equal(2, nativeCollection.ReadCount);
    }

    [Fact]
    public void RequestsRejectInvalidOffsetsLimitsAndContinuationUsage()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CollectionReadRequest.Range(-1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => CollectionReadRequest.Page(0, 0));
        Assert.Throws<ArgumentException>(() => new CollectionReadRequest(
            CollectionAccessMode.SNAPSHOT,
            1,
            1));
        Assert.Throws<ArgumentException>(() => new CollectionReadRequest(
            CollectionAccessMode.VIRTUALIZED_RANGE,
            0,
            1,
            "invalid"));
    }

    private static UIEngineHost _CreateReflectionHost(CollectionAccessLimits? limits = null) =>
        new(new UIEngineHostOptions
        {
            DescriptorProviders = [new ReflectionObjectDescriptorProvider()],
            CollectionLimits = limits ?? new CollectionAccessLimits(),
        });

    private static async Task<IObjectDescriptor> _DescribeAsync(UIEngineHost host, object instance)
    {
        var handle = host.RegisterRoot("root", instance).Value;
        return (await host.DescribeAsync(handle)).Value;
    }

    private static ICollectionDescriptor _Collection(IObjectDescriptor descriptor, string id) =>
        Assert.Single(descriptor.Collections, collection => collection.Id == id);

    private static _Identified _Resolve(UIEngineHost host, ObjectHandle handle)
    {
        Assert.True(host.TryResolve(handle, out var instance));
        return Assert.IsType<_Identified>(instance);
    }

    private sealed class _CollectionModel
    {
        [Children]
        public object?[] ArrayItems { get; init; } = [];

        [Children]
        public Dictionary<string, object?> DictionaryItems { get; init; } = [];

        [Children]
        public ObservableCollection<object?> ObservableItems { get; } = [];

        [Children]
        public ArrayList LegacyItems { get; } = new() { null, 2 };

        [Children]
        public _TrackingEnumerable LazyItems { get; } = new();

        [Children]
        public _TrackingReadOnlyCollection FiniteItems { get; } = new(0, 1, 2);

        [Children]
        public _TrackingReadOnlyList IndexedItems { get; } = new(0, 1, 2, 3, 4);
    }

    private sealed class _Identified(string identity) : IStableDomainIdentity
    {
        public string DomainIdentity => identity;
    }

    private sealed class _TrackingEnumerable : IEnumerable<int>
    {
        public int EnumerationCount { get; private set; }

        public IEnumerator<int> GetEnumerator()
        {
            EnumerationCount++;
            throw new InvalidOperationException("The lazy sequence must not be enumerated implicitly.");
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class _TrackingReadOnlyCollection(params int[] items) : IReadOnlyCollection<int>
    {
        public int Count => items.Length;

        public int EnumerationCount { get; private set; }

        public IEnumerator<int> GetEnumerator()
        {
            EnumerationCount++;
            return ((IEnumerable<int>)items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class _TrackingReadOnlyList(params int[] initialItems) : IReadOnlyList<int>
    {
        private readonly List<int> _Items = [.. initialItems];

        public int Count => _Items.Count;

        public int EnumerationCount { get; private set; }

        public int IndexReadCount { get; private set; }

        public int this[int index]
        {
            get
            {
                IndexReadCount++;
                return _Items[index];
            }
        }

        public void Add(int value) => _Items.Add(value);

        public IEnumerator<int> GetEnumerator()
        {
            EnumerationCount++;
            return _Items.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    private sealed class _NativeModel;

    private sealed class _NativeProvider(_NativeCollectionDescriptor collection) : IObjectDescriptorProvider
    {
        public bool CanDescribe(Type objectType) => objectType == typeof(_NativeModel);

        public ValueTask<InteractionResult<IObjectDescriptor>> DescribeAsync(
            UIEngineHost host,
            object instance,
            ObjectHandle handle,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InteractionResult.Success<IObjectDescriptor>(
                new _NativeObjectDescriptor(handle.Identity, collection)));
    }

    private sealed class _NativeObjectDescriptor(
        ObjectIdentity identity,
        ICollectionDescriptor collection) : IObjectDescriptor
    {
        public ObjectIdentity Identity => identity;

        public string TypeName => nameof(_NativeModel);

        public string DisplayName => nameof(_NativeModel);

        public string? Summary => null;

        public IReadOnlyList<IValueDescriptor> Values => [];

        public IReadOnlyList<IReferenceDescriptor> References => [];

        public IReadOnlyList<ICollectionDescriptor> Collections => [collection];

        public IReadOnlyList<IActionDescriptor> Actions => [];
    }

    private sealed class _NativeCollectionDescriptor : ICollectionDescriptor
    {
        private static readonly int[] _VALUES = [10, 11, 12, 13, 14];

        public string Id => "native";

        public string DisplayName => "Native";

        public Type ElementType => typeof(int);

        public Type? KeyType => null;

        public CollectionCapabilities Capabilities =>
            CollectionCapabilities.PAGING | CollectionCapabilities.VIRTUALIZED_RANGE;

        public int ReadCount { get; private set; }

        public ValueTask<InteractionResult<CollectionReadResult>> ReadAsync(
            CollectionReadRequest request,
            CancellationToken cancellationToken = default)
        {
            ReadCount++;
            var start = request.ContinuationToken is null
                ? checked((int)request.Offset)
                : int.Parse(request.ContinuationToken, System.Globalization.CultureInfo.InvariantCulture);
            var values = _VALUES.Skip(start).Take(request.Limit).ToArray();
            var entries = values
                .Select((value, index) => CollectionEntry.Scalar(start + index, value))
                .ToArray();
            var hasMore = start + entries.Length < _VALUES.Length;
            return ValueTask.FromResult(InteractionResult.Success(new CollectionReadResult(
                request.Mode,
                entries,
                start,
                request.Mode == CollectionAccessMode.PAGE ? null : _VALUES.Length,
                hasMore,
                request.Mode == CollectionAccessMode.PAGE && hasMore
                    ? (start + entries.Length).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : null)));
        }
    }
}
