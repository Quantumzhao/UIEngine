using System.ComponentModel.DataAnnotations;
using UIEngine.Core;
using UIEngine.Core.Attributes;
using Xunit;

namespace UIEngine.Framework.Tests;

public sealed class LiveValueNodeTests
{
    [Fact]
    public void ReflectedValuesReceiveExactMembershipAccessAndShapeFacets()
    {
        var model = new _ReflectedModel();
        using var host = new UIEngineHost();
        host.SetRoot("model", model);

        var first = _ResolveMembers(host, "model");
        var second = _ResolveMembers(host, "model");

        var state = first[nameof(_ReflectedModel.State)];
        Assert.NotSame(state, second[nameof(_ReflectedModel.State)]);
        Assert.True(state is IPropertyNode);
        Assert.True(state is IReadableValueNode);
        Assert.True(state is IWritableValueNode);
        Assert.True(state is INullableValueNode);
        Assert.True(state is IEnumNode);
        Assert.False(state is IFieldNode);
        Assert.False(state is INumberNode);
        Assert.Equal(typeof(_ReflectedModel), ((IPropertyNode)state).DeclaringType);
        Assert.Equal(2, ((IEnumNode)state).Options.Count);

        var count = first[nameof(_ReflectedModel.Count)];
        Assert.True(count is IFieldNode);
        Assert.True(count is IReadableValueNode);
        Assert.True(count is IWritableValueNode);
        var writableCount = (IWritableValueNode)count;
        Assert.True(count is INumberNode);
        Assert.False(count is IPropertyNode);
        Assert.IsType<ValueRange<object>>(writableCount.Range);

        var name = first[nameof(_ReflectedModel.Name)];
        Assert.True(name is IStringNode);
        Assert.True(name is IReadableValueNode);
        Assert.False(name is IWritableValueNode);
        Assert.False(name is INullableValueNode);

        var optional = first[nameof(_ReflectedModel.Optional)];
        Assert.True(optional is IStringNode);
        Assert.True(optional is INullableValueNode);

        Assert.True(first[nameof(_ReflectedModel.Enabled)] is IBooleanNode);
        Assert.True(first[nameof(_ReflectedModel.Initial)] is ICharacterNode);
        var writeOnly = first[nameof(_ReflectedModel.WriteOnly)];
        Assert.True(writeOnly is IWritableValueNode);
        Assert.True(writeOnly is IStringNode);
        Assert.False(writeOnly is IReadableValueNode);
        var timestamp = first[nameof(_ReflectedModel.Timestamp)];
        Assert.False(timestamp is
            IStringNode or ICharacterNode or IBooleanNode or INumberNode or IEnumNode);
        Assert.All(first.Values, static node => Assert.True(node.IsTerminal));

        var write = ((IWritableValueNode)state).WriteValue("READY");
        var writeWithoutRead = ((IWritableValueNode)writeOnly).WriteValue("updated");
        var read = ((IReadableValueNode)second[nameof(_ReflectedModel.State)]).ReadValue();

        Assert.True(write.IsSuccess);
        Assert.True(writeWithoutRead.IsSuccess);
        Assert.Equal(_State.READY, model.State);
        Assert.Equal("updated", model.WrittenValue);
        Assert.Equal(_State.READY, read.Value);
    }

    [Fact]
    public void ProgrammaticValuesAreFreshNodesOverTheSameLiveValue()
    {
        var model = new _ProgrammaticModel();
        var exposure = new ValueExposure<_ProgrammaticModel, decimal>(
            "Amount",
            static value => value.Amount,
            static (value, amount) => value.Amount = amount,
            new ValueRange<decimal>(0m, 100m));
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            Exposures = [new TypeExposure<_ProgrammaticModel>([exposure])],
        });
        host.SetRoot("model", model);

        var first = Assert.Single(_ResolveMembers(host, "model").Values);
        var second = Assert.Single(_ResolveMembers(host, "model").Values);

        Assert.NotSame(first, second);
        Assert.True(first is IProgrammaticValueNode);
        Assert.True(first is IReadableValueNode);
        Assert.True(first is IWritableValueNode);
        var writable = (IWritableValueNode)first;
        Assert.True(first is INumberNode);
        Assert.False(first is IMemberNode);
        Assert.IsType<ValueRange<decimal>>(writable.Range);

        var write = writable.WriteValue("12.5");
        var read = ((IReadableValueNode)second).ReadValue();

        Assert.True(write.IsSuccess);
        Assert.Equal(12.5m, model.Amount);
        Assert.Equal(12.5m, read.Value);
    }

    private static Dictionary<string, BaseNode> _ResolveMembers(
        UIEngineHost host,
        string rootName)
    {
        var resolved = host.ResolveRootNode(rootName);
        Assert.True(resolved.IsSuccess);
        return ((IObjectNode)resolved.Value.Node).Members.ToDictionary(
            static node => node.Name,
            StringComparer.Ordinal);
    }

    private sealed class _ReflectedModel
    {
        private string _WrittenValue = string.Empty;

        [Expose]
        public _State? State { get; set; }

        [Expose]
        [Range(0, 10)]
        public int Count = 0;

        [Expose(ReadOnly = true)]
        public string Name { get; set; } = "model";

        [Expose]
        public string? Optional { get; set; }

        [Expose]
        public bool Enabled { get; set; }

        [Expose]
        public char Initial { get; set; }

        [Expose]
        public string WriteOnly
        {
            set => _WrittenValue = value;
        }

        [Expose]
        public DateTime Timestamp { get; set; }

        public string WrittenValue => _WrittenValue;
    }

    private sealed class _ProgrammaticModel
    {
        public decimal Amount { get; set; }
    }

    private enum _State
    {
        NEW,
        READY,
    }
}
