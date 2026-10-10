using System.Text.Json;
using UIEngine.Core;
using UIEngine.Frontend.Tui;
using Xunit;

namespace UIEngine.Frontend.Tui.Tests;

[Collection("Terminal application")]
public sealed class TuiLayoutTests
{
    [Fact]
    public void LayoutRoundTripsIdsOrderPathsSelectionPlacementAndSizeThroughJson()
    {
        using var host = TuiTestModel.CreateHost();
        using var source = TuiFrontend.CreateWorkspace(new TuiFrontendOptions
        {
            Startup = new AddNavigator(TuiTestModel.Path("model")),
        });
        var sourceWorkspace = source.Workspace;
        sourceWorkspace.AddNavigator(TuiTestModel.Path("model", "Child"));
        var rootId = sourceWorkspace.Navigators[0].Id;
        var childId = sourceWorkspace.Navigators[1].Id;
        sourceWorkspace.ReorderNavigator(sourceWorkspace.Navigators[1].Id, 0);
        source.SetPresentationConfiguration(childId, new(2, 3, 60, 18));
        source.SetPresentationConfiguration(rootId, new(4, 5, 30, 9));
        source.RestoreLayout(new TuiLayoutSnapshot(
            (WorkspaceSnapshot)sourceWorkspace.CreateSnapshot(rootId),
            source.Presentations.ToDictionary(
                static item => item.Navigator.Id,
                static item => item.Configuration)));

        var layout = (TuiLayoutSnapshot)source.CreateLayoutSnapshot();
        var json = JsonSerializer.Serialize(layout);
        var deserialized = JsonSerializer.Deserialize<TuiLayoutSnapshot>(json)!;
        using var target = TuiFrontend.CreateWorkspace(new TuiFrontendOptions
        {
            Startup = new RestoreLayout(deserialized),
        });
        var targetWorkspace = target.Workspace;

        Assert.Equal([childId, rootId], targetWorkspace.Navigators.Select(static item => item.Id));
        Assert.Equal(
            ["/Model/Child", "/Model"],
            targetWorkspace.Navigators.Select(static item =>
                item.CurrentPath.ToDisplayString().IfLeft(static error => error.Message)));
        Assert.Equal(rootId, target.SelectedNavigatorId);
        Assert.Equal(new NavigatorPresentationConfiguration(2, 3, 60, 18), target.Presentations.ElementAt(0).Configuration);
        Assert.Equal(new NavigatorPresentationConfiguration(4, 5, 30, 9), target.Presentations.ElementAt(1).Configuration);
        Assert.DoesNotContain("Version", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingInvalidAndOrphanedConfigurationUseDeterministicDefaults()
    {
        using var host = TuiTestModel.CreateHost();
        var missingId = Guid.NewGuid();
        var invalidId = Guid.NewGuid();
        var savedId = Guid.NewGuid();
        var layout = new TuiLayoutSnapshot(
            new WorkspaceSnapshot(
                [
                    new NavigatorSnapshot(missingId, [TuiTestRoots.Snapshot]),
                    new NavigatorSnapshot(invalidId, [TuiTestRoots.Snapshot]),
                    new NavigatorSnapshot(savedId, [TuiTestRoots.Snapshot]),
                ],
                savedId),
            new Dictionary<Guid, NavigatorPresentationConfiguration>
            {
                [invalidId] = new(-1, 0, 0, 12),
                [savedId] = new(8, 9, 70, 20),
                [Guid.NewGuid()] = new(1, 1, 1, 1),
            });

        using var tui = TuiFrontend.CreateWorkspace(new TuiFrontendOptions
        {
            Startup = new RestoreLayout(layout),
        });

        Assert.Equal(new NavigatorPresentationConfiguration(0, 0, 40, 12), tui.Presentations.ElementAt(0).Configuration);
        Assert.Equal(new NavigatorPresentationConfiguration(0, 1, 40, 12), tui.Presentations.ElementAt(1).Configuration);
        Assert.Equal(new NavigatorPresentationConfiguration(8, 9, 70, 20), tui.Presentations.ElementAt(2).Configuration);
        Assert.Equal(3, tui.Presentations.Count);
    }

    [Fact]
    public void BrokenPathKeepsConfigurationAndBackRecovery()
    {
        using var host = TuiTestModel.CreateHost();
        var brokenId = Guid.NewGuid();
        var layout = new TuiLayoutSnapshot(
            new WorkspaceSnapshot(
                [new NavigatorSnapshot(brokenId, [
                    TuiTestRoots.Snapshot,
                    new MemberPathSegmentSnapshot("Missing"),
                ])],
                brokenId),
            new Dictionary<Guid, NavigatorPresentationConfiguration>
            {
                [brokenId] = new(5, 6, 50, 15),
            });
        using var tui = TuiFrontend.CreateWorkspace(new TuiFrontendOptions
        {
            Startup = new RestoreLayout(layout),
        });
        var coreWorkspace = tui.Workspace;

        var presentation = Assert.Single(tui.Presentations);
        Assert.Equal(PlaceholderNodeState.Invalid, presentation.CurrentControl.State);
        Assert.Equal(new NavigatorPresentationConfiguration(5, 6, 50, 15), presentation.Configuration);

        Assert.True(Assert.Single(coreWorkspace.Navigators).GoBack().IsRight);
        Assert.Equal(PlaceholderNodeState.Healthy, presentation.CurrentControl.State);
    }

    [Fact]
    public void SerializedLayoutContainsOnlyStableAddressAndPresentationData()
    {
        const string secret = "domain-secret-7729";
        var model = new TuiTestModel { Name = secret };
        using var host = TuiTestModel.CreateHost(model);
        using var tui = TuiFrontend.CreateWorkspace(new TuiFrontendOptions
        {
            Startup = new AddNavigator(TuiTestModel.Path("model")),
        });

        var json = JsonSerializer.Serialize((TuiLayoutSnapshot)tui.CreateLayoutSnapshot());

        Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        Assert.DoesNotContain("Handle", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Node", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Control", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Draft", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invocation", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Task", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Width", json, StringComparison.Ordinal);
        Assert.Contains("CurrentPath", json, StringComparison.Ordinal);
    }

    [Fact]
    public void RestoredGridCollisionsMoveLaterNavigatorsWithoutChangingPreferredSize()
    {
        using var host = TuiTestModel.CreateHost();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var thirdId = Guid.NewGuid();
        var layout = new TuiLayoutSnapshot(
            new WorkspaceSnapshot(
                [
                    new NavigatorSnapshot(firstId, [TuiTestRoots.Snapshot]),
                    new NavigatorSnapshot(secondId, [TuiTestRoots.Snapshot]),
                    new NavigatorSnapshot(thirdId, [TuiTestRoots.Snapshot]),
                ],
                firstId),
            new Dictionary<Guid, NavigatorPresentationConfiguration>
            {
                [firstId] = new(0, 0, 50, 14),
                [secondId] = new(0, 0, 60, 16),
                [thirdId] = new(0, 1, 70, 18),
            });

        using var tui = TuiFrontend.CreateWorkspace(new TuiFrontendOptions
        {
            Startup = new RestoreLayout(layout),
        });

        Assert.Equal(
            [
                new NavigatorPresentationConfiguration(0, 0, 50, 14),
                new NavigatorPresentationConfiguration(0, 1, 60, 16),
                new NavigatorPresentationConfiguration(0, 2, 70, 18),
            ],
            tui.Presentations.Select(static presentation => presentation.Configuration));
    }
}
