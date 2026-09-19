using System.Collections;
using UIEngine.Core;
using UIEngine.Core.Attributes;
using UIEngine.Core.Reflection;
using Xunit;

namespace UIEngine.Framework.Tests;

/// <summary>Verifies live reflected values, collections, and synchronous actions.</summary>
public sealed class ReflectionLiveOperationTests
{
    [Fact]
    public async Task ValueReadsAndWritesOperateOnCurrentDomainState()
    {
        var model = new _LiveModel { Count = 4 };
        var (_, descriptor) = await _DescribeAsync(model);
        var count = Assert.Single(descriptor.Values, value => value.Id == nameof(_LiveModel.Count));
        var fieldValue = Assert.Single(descriptor.Values, value => value.Id == nameof(_LiveModel.FieldValue));
        var enabled = Assert.Single(descriptor.Values, value => value.Id == nameof(_LiveModel.Enabled));
        var label = Assert.Single(descriptor.Values, value => value.Id == nameof(_LiveModel.Label));
        var mode = Assert.Single(descriptor.Values, value => value.Id == nameof(_LiveModel.Mode));
        var optionalCount = Assert.Single(
            descriptor.Values,
            value => value.Id == nameof(_LiveModel.OptionalCount));
        var locked = Assert.Single(descriptor.Values, value => value.Id == nameof(_LiveModel.Locked));
        var getterOnly = Assert.Single(descriptor.Values, value => value.Id == nameof(_LiveModel.GetterOnly));

        model.Count = 9;
        var read = await count.ReadAsync();
        var write = await count.WriteAsync("42");
        await fieldValue.WriteAsync("5");
        await enabled.WriteAsync("true");
        await label.WriteAsync(12);
        await mode.WriteAsync("active");
        await optionalCount.WriteAsync("7");

        Assert.True(count.CanRead);
        Assert.True(count.CanWrite);
        Assert.Equal(9, read.Value);
        Assert.Equal(42, write.Value);
        Assert.Equal(42, model.Count);
        Assert.Equal(5, model.FieldValue);
        Assert.True(model.Enabled);
        Assert.Equal("12", model.Label);
        Assert.Equal(OperationMode.ACTIVE, model.Mode);
        Assert.Equal(7, model.OptionalCount);
        Assert.Null((await optionalCount.WriteAsync(null)).Value);
        Assert.Null(model.OptionalCount);
        Assert.False(locked.CanWrite);
        Assert.False(getterOnly.CanWrite);
        Assert.Equal(
            InteractionErrorCode.VALIDATION_FAILED,
            (await locked.WriteAsync("8")).Error?.Code);
    }

    [Fact]
    public void ScalarConversionSupportsNumericBooleanNullableStringCharacterAndEnumForms()
    {
        Assert.Equal((sbyte)-1, ReflectionValueConverter.Convert("-1", typeof(sbyte)).Value);
        Assert.Equal((byte)1, ReflectionValueConverter.Convert("1", typeof(byte)).Value);
        Assert.Equal((short)-2, ReflectionValueConverter.Convert("-2", typeof(short)).Value);
        Assert.Equal((ushort)2, ReflectionValueConverter.Convert("2", typeof(ushort)).Value);
        Assert.Equal(-3, ReflectionValueConverter.Convert("-3", typeof(int)).Value);
        Assert.Equal((uint)3, ReflectionValueConverter.Convert("3", typeof(uint)).Value);
        Assert.Equal((long)-4, ReflectionValueConverter.Convert("-4", typeof(long)).Value);
        Assert.Equal((ulong)4, ReflectionValueConverter.Convert("4", typeof(ulong)).Value);
        Assert.Equal(1.5f, ReflectionValueConverter.Convert("1.5", typeof(float)).Value);
        Assert.Equal(2.5d, ReflectionValueConverter.Convert("2.5", typeof(double)).Value);
        Assert.Equal(3.5m, ReflectionValueConverter.Convert("3.5", typeof(decimal)).Value);
        Assert.Equal(true, ReflectionValueConverter.Convert("true", typeof(bool)).Value);
        Assert.Equal('x', ReflectionValueConverter.Convert("x", typeof(char)).Value);
        Assert.Equal("12", ReflectionValueConverter.Convert(12, typeof(string)).Value);
        Assert.Equal(OperationMode.ACTIVE, ReflectionValueConverter.Convert("active", typeof(OperationMode)).Value);
        Assert.Null(ReflectionValueConverter.Convert(null, typeof(int?)).Value);
        Assert.Equal(7, ReflectionValueConverter.Convert("7", typeof(int?)).Value);
        Assert.Equal(
            InteractionErrorCode.CONVERSION_FAILED,
            ReflectionValueConverter.Convert("unknown", typeof(OperationMode)).Error?.Code);
    }

    [Fact]
    public async Task ValueWritesReturnStructuredConversionAndValidationFailures()
    {
        var model = new _LiveModel();
        var (_, descriptor) = await _DescribeAsync(model);
        var count = Assert.Single(descriptor.Values, value => value.Id == nameof(_LiveModel.Count));
        var guarded = Assert.Single(descriptor.Values, value => value.Id == nameof(_LiveModel.Guarded));

        var invalidNumber = await count.WriteAsync("not-a-number");
        var invalidNull = await count.WriteAsync(null);
        var rejectedByDomain = await guarded.WriteAsync("-1");

        Assert.Equal(InteractionErrorCode.CONVERSION_FAILED, invalidNumber.Error?.Code);
        Assert.Equal(InteractionErrorCode.VALIDATION_FAILED, invalidNull.Error?.Code);
        Assert.Equal(InteractionErrorCode.VALIDATION_FAILED, rejectedByDomain.Error?.Code);
    }

    [Fact]
    public async Task CollectionSnapshotIsExplicitAndPreservesRepeatedReferenceIdentity()
    {
        var item = new object();
        var items = new _TrackingCollection(item, item);
        var model = new _LiveModel(items);
        var (host, descriptor) = await _DescribeAsync(model);

        Assert.Equal(0, items.EnumerationCount);

        var snapshot = await Assert.Single(descriptor.Collections).SnapshotAsync();

        Assert.Equal(1, items.EnumerationCount);
        Assert.Equal(2, snapshot.Value.Count);
        Assert.Equal(snapshot.Value[0], snapshot.Value[1]);
        Assert.Equal(host.GetOrCreateHandle(item), snapshot.Value[0]);
    }

    [Fact]
    public async Task ActionInvocationBindsNamedArgumentsAndOptionalDefaults()
    {
        var model = new _LiveModel();
        var (_, descriptor) = await _DescribeAsync(model);
        var scale = Assert.Single(descriptor.Actions, action => action.Id == nameof(_LiveModel.Scale));

        var named = await scale.InvokeAsync(new Dictionary<string, object?>
        {
            ["factor"] = "3",
            ["amount"] = "4",
        });
        var withDefault = await scale.InvokeAsync(new Dictionary<string, object?>
        {
            ["amount"] = "2",
        });

        Assert.Equal(12, named.Value);
        Assert.Equal(16, withDefault.Value);
        Assert.Equal(16, model.Count);
        Assert.Equal(2, scale.Parameters.Count);
        Assert.True(scale.Parameters[0].IsRequired);
        Assert.False(scale.Parameters[1].IsRequired);
    }

    [Fact]
    public async Task ActionInvocationReturnsStructuredInputConversionAndDomainFailures()
    {
        var model = new _LiveModel();
        var (_, descriptor) = await _DescribeAsync(model);
        var scale = Assert.Single(descriptor.Actions, action => action.Id == nameof(_LiveModel.Scale));
        var fail = Assert.Single(descriptor.Actions, action => action.Id == nameof(_LiveModel.Fail));

        var missing = await scale.InvokeAsync(new Dictionary<string, object?>());
        var unknown = await scale.InvokeAsync(new Dictionary<string, object?> { ["Amount"] = "2" });
        var invalid = await scale.InvokeAsync(new Dictionary<string, object?> { ["amount"] = "invalid" });
        var domainFailure = await fail.InvokeAsync(new Dictionary<string, object?>());

        Assert.Equal(InteractionErrorCode.INVALID_INPUT, missing.Error?.Code);
        Assert.Equal(InteractionErrorCode.INVALID_INPUT, unknown.Error?.Code);
        Assert.Equal(InteractionErrorCode.CONVERSION_FAILED, invalid.Error?.Code);
        Assert.Equal(InteractionErrorCode.INVOCATION_FAILED, domainFailure.Error?.Code);
    }

    private static async Task<(UIEngineHost Host, IObjectDescriptor Descriptor)> _DescribeAsync(
        _LiveModel model)
    {
        var host = new UIEngineHost([new ReflectionObjectDescriptorProvider()]);
        var handle = host.RegisterRoot("model", model).Value;
        var descriptor = (await host.DescribeAsync(handle)).Value;
        return (host, descriptor);
    }

    /// <summary>Provides mutable values, a collection, and actions for live-operation tests.</summary>
    private sealed class _LiveModel
    {
        private int _Guarded;

        [Expose]
        public int FieldValue = 0;

        public _LiveModel(IEnumerable<object>? items = null)
        {
            Items = items ?? Array.Empty<object>();
        }

        [Expose]
        public int Count { get; set; }

        [Expose]
        public bool Enabled { get; set; }

        [Expose]
        public int Guarded
        {
            get => _Guarded;
            set => _Guarded = value >= 0
                ? value
                : throw new ArgumentOutOfRangeException(nameof(value));
        }

        [Expose]
        public int GetterOnly => Count;

        [Expose]
        public string Label { get; set; } = string.Empty;

        [Expose(ReadOnly = true)]
        public int Locked { get; set; } = 7;

        [Expose]
        public OperationMode Mode { get; set; }

        [Expose]
        public int? OptionalCount { get; set; }

        [Children]
        public IEnumerable<object> Items { get; }

        [Action]
        public int Scale(int amount, int factor = 2)
        {
            Count += amount * factor;
            return Count;
        }

        [Action]
        public void Fail() => throw new InvalidOperationException(
            $"Expected domain failure at count {Count}.");
    }

    /// <summary>Counts enumeration requests while yielding a fixed sequence.</summary>
    private sealed class _TrackingCollection : IEnumerable<object>
    {
        private readonly object[] _Items;

        public _TrackingCollection(params object[] items)
        {
            _Items = items;
        }

        public int EnumerationCount { get; private set; }

        public IEnumerator<object> GetEnumerator()
        {
            EnumerationCount++;
            return ((IEnumerable<object>)_Items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary>Provides enum values used by scalar conversion tests.</summary>
internal enum OperationMode
{
    INACTIVE,
    ACTIVE,
}
