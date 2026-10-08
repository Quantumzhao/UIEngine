using System.Collections.ObjectModel;
using System.ComponentModel;
using UIEngine.Core;
using UIEngine.Core.Attributes;
using Xunit;

namespace UIEngine.Framework.Tests;

public sealed class NodeRefreshTests
{
    [Fact]
    public void PropertyNotificationsReachEveryMatchingOccurrenceAndAllowDetachment()
    {
        var model = new _NotifyingModel();
        using var host = new UIEngineHost();
        host.SetRoot("model", model);
        var first = _ResolveMembers(host);
        var second = _ResolveMembers(host);
        var firstCount = first[nameof(_NotifyingModel.Count)];
        var secondCount = second[nameof(_NotifyingModel.Count)];
        var firstName = first[nameof(_NotifyingModel.Name)];
        var firstCountEvents = new List<NodeRefreshRequestedEventArgs>();
        var secondCountEvents = new List<NodeRefreshRequestedEventArgs>();
        var nameEventCount = 0;
        EventHandler<NodeRefreshRequestedEventArgs> detachedHandler = (_, _) =>
            throw new InvalidOperationException("A detached handler was invoked.");
        firstCount.RefreshRequested += (_, eventArgs) => firstCountEvents.Add(eventArgs);
        secondCount.RefreshRequested += (_, eventArgs) => secondCountEvents.Add(eventArgs);
        firstName.RefreshRequested += (_, _) => nameEventCount++;
        firstCount.RefreshRequested += detachedHandler;
        firstCount.RefreshRequested -= detachedHandler;

        model.SetCount(1);

        var firstEvent = Assert.Single(firstCountEvents);
        var secondEvent = Assert.Single(secondCountEvents);
        Assert.Same(firstCount, firstEvent.Node);
        Assert.Same(secondCount, secondEvent.Node);
        Assert.Equal(NodeRefreshKind.PROPERTY_CHANGED, firstEvent.Kind);
        Assert.Equal(NodeRefreshKind.PROPERTY_CHANGED, secondEvent.Kind);
        Assert.Equal(0, nameEventCount);

        model.RequestRefreshForAll();

        Assert.Equal(2, firstCountEvents.Count);
        Assert.Equal(2, secondCountEvents.Count);
        Assert.Equal(1, nameEventCount);
    }

    [Fact]
    public void CollectionNotificationsFollowReplacementAndStopAtHostDisposal()
    {
        var model = new _NotifyingModel();
        var original = model.Items;
        var host = new UIEngineHost();
        host.SetRoot("model", model);
        var first = _ResolveMembers(host)[nameof(_NotifyingModel.Items)];
        var second = _ResolveMembers(host)[nameof(_NotifyingModel.Items)];
        var firstKinds = new List<NodeRefreshKind>();
        var secondKinds = new List<NodeRefreshKind>();
        first.RefreshRequested += (_, eventArgs) => firstKinds.Add(eventArgs.Kind);
        second.RefreshRequested += (_, eventArgs) => secondKinds.Add(eventArgs.Kind);

        original.Add("first");
        model.ReplaceItems(new ObservableCollection<string>());
        original.Add("detached");
        model.Items.Add("replacement");

        Assert.Equal(
            [
                NodeRefreshKind.COLLECTION_CHANGED,
                NodeRefreshKind.PROPERTY_CHANGED,
                NodeRefreshKind.COLLECTION_CHANGED,
            ],
            firstKinds);
        Assert.Equal(firstKinds, secondKinds);

        host.Dispose();
        model.Items.Add("after disposal");

        Assert.Equal(3, firstKinds.Count);
        Assert.Equal(3, secondKinds.Count);
    }

    [Fact]
    public void ProgrammaticValuesCanSupplyFilteredRefreshSources()
    {
        var model = new _ProgrammaticModel();
        var exposure = new ValueExposure<_ProgrammaticModel, decimal>(
            "DisplayedAmount",
            static value => value.Amount,
            refreshSource: static value => value.Notifications,
            refreshPropertyName: nameof(_ProgrammaticModel.Amount));
        using var host = new UIEngineHost(new UIEngineHostOptions
        {
            Exposures = [new TypeExposure<_ProgrammaticModel>([exposure])],
        });
        host.SetRoot("model", model);
        var first = Assert.Single(_ResolveMembers(host).Values);
        var second = Assert.Single(_ResolveMembers(host).Values);
        var firstEvents = new List<NodeRefreshRequestedEventArgs>();
        var secondEventCount = 0;
        first.RefreshRequested += (_, eventArgs) => firstEvents.Add(eventArgs);
        second.RefreshRequested += (_, _) => secondEventCount++;

        model.Notifications.Raise("Unrelated");
        model.Notifications.Raise(nameof(_ProgrammaticModel.Amount));
        model.Notifications.Raise(null);

        Assert.Equal(2, firstEvents.Count);
        Assert.Equal(2, secondEventCount);
        Assert.All(firstEvents, eventArgs =>
        {
            Assert.Same(first, eventArgs.Node);
            Assert.Equal(NodeRefreshKind.PROPERTY_CHANGED, eventArgs.Kind);
        });
    }

    [Fact]
    public void ProgrammaticRefreshConfigurationRejectsAmbiguousFilters()
    {
        Assert.Throws<ArgumentException>(() =>
            new ValueExposure<_ProgrammaticModel, decimal>(
                "DisplayedAmount",
                static value => value.Amount,
                refreshPropertyName: nameof(_ProgrammaticModel.Amount)));
        Assert.Throws<ArgumentException>(() =>
            new ValueExposure<_ProgrammaticModel, decimal>(
                "DisplayedAmount",
                static value => value.Amount,
                refreshSource: static value => value.Notifications,
                refreshPropertyName: " "));
    }

    private static Dictionary<string, BaseNode> _ResolveMembers(UIEngineHost host) =>
        ((IObjectNode)host.ResolveRootNode("model").RightValue().Node).Members.ToDictionary(
            static node => node.Name,
            StringComparer.Ordinal);

    private sealed class _NotifyingModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        [Expose]
        public int Count { get; private set; }

        [Expose]
        public string Name { get; private set; } = "model";

        [Children]
        public ObservableCollection<string> Items { get; private set; } = [];

        public void SetCount(int value)
        {
            Count = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Count)));
        }

        public void ReplaceItems(ObservableCollection<string> value)
        {
            Items = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Items)));
        }

        public void RequestRefreshForAll() =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }

    private sealed class _ProgrammaticModel
    {
        public decimal Amount { get; set; }

        public _NotificationSource Notifications { get; } = new();
    }

    private sealed class _NotificationSource : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public void Raise(string? propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
