using System.ComponentModel;
using UIEngine.Nodes;
using Xunit;

namespace UIEngine.Legacy.Characterization.Tests;

[Collection(LegacyDashboardTestGroup.NAME)]
public sealed class LegacyDashboardTests
{
    private static readonly string[] _EXPECTED_ITEMS = { "alpha", "beta" };

    private readonly LegacyDashboardFixture _Fixture;

    public LegacyDashboardTests(LegacyDashboardFixture fixture)
    {
        _Fixture = fixture;
        _Fixture.Reset();
    }

    [Fact]
    public void ImportEntryObjectsDiscoversOnlyExplicitlyVisibleRootsAndMembers()
    {
        Assert.Null(Dashboard.GetRootNode<ObjectNode>(nameof(LegacyDashboardFixture.LegacyEntryPoints.HiddenModel)));
        Assert.Contains(_Fixture.Root.Properties, node => node.Name == nameof(LegacyDashboardFixture.CharacterizedModel.Value));
        Assert.Contains(_Fixture.Root.Properties, node => node.Name == nameof(LegacyDashboardFixture.CharacterizedModel.Items));
        Assert.DoesNotContain(_Fixture.Root.Properties, node => node.Name == nameof(LegacyDashboardFixture.CharacterizedModel.HiddenValue));
        Assert.Contains(_Fixture.Root.Methods, node => node.Name == nameof(LegacyDashboardFixture.CharacterizedModel.Add));
    }

    [Fact]
    public void ObjectDataReadsAndWritesTheUnderlyingProperty()
    {
        var valueNode = _GetProperty(nameof(LegacyDashboardFixture.CharacterizedModel.Value));

        Assert.Equal(2, Assert.IsType<int>(valueNode.ObjectData));

        valueNode.ObjectData = 11;

        Assert.Equal(11, _Fixture.Model.Value);
        Assert.Equal(11, Assert.IsType<int>(valueNode.ObjectData));
    }

    [Fact]
    public void InvokeCallsSynchronousMethodWithAssignedParameter()
    {
        var method = _Fixture.Root.Methods.Single(
            node => node.Name == nameof(LegacyDashboardFixture.CharacterizedModel.Add));

        Assert.True(method.SetParameter(5, 0));

        var result = method.Invoke();

        Assert.Equal(7, Assert.IsType<int>(result.ObjectData));
    }

    [Fact]
    public void CollectionNodeEnumeratesTheUnderlyingCollection()
    {
        var itemsNode = Assert.IsType<CollectionNode>(
            _GetProperty(nameof(LegacyDashboardFixture.CharacterizedModel.Items)));

        var items = itemsNode.Select(node => node.GetObjectData<string>()).ToArray();

        Assert.Equal(_EXPECTED_ITEMS, items);
    }

    [Fact]
    public void PropertyChangedRefreshesTheMatchingPropertyNode()
    {
        var valueNode = _GetProperty(nameof(LegacyDashboardFixture.CharacterizedModel.Value));
        var objectDataChanged = false;

        void OnPropertyChanged(object? sender, PropertyChangedEventArgs args)
        {
            objectDataChanged |= args.PropertyName == nameof(ObjectNode.ObjectData);
        }

        valueNode.PropertyChanged += OnPropertyChanged;
        try
        {
            _Fixture.Model.Value = 23;

            Assert.True(objectDataChanged);
            Assert.Equal(23, Assert.IsType<int>(valueNode.ObjectData));
        }
        finally
        {
            valueNode.PropertyChanged -= OnPropertyChanged;
        }
    }

    private ObjectNode _GetProperty(string name) => _Fixture.Root.Properties.Single(node => node.Name == name);
}
