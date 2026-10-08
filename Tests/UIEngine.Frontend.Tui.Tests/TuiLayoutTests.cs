using System.Text.Json;
using UIEngine.Core;
using UIEngine.Frontend.Tui;
using Xunit;

namespace UIEngine.Frontend.Tui.Tests;

[Collection("Terminal application")]
public sealed class TuiLayoutTests
{
    [Fact]
    public void LayoutRoundTripsNamesOrderPathsSelectionPlacementAndSizeThroughJson()
    {
        using var host = TuiTestModel.CreateHost();
        using var source = TuiFrontend.CreateWorkspace(host, new TuiFrontendOptions
        {
            Startup = new AddNavigator("root", TuiTestModel.Path("model")),
        });
        var sourceWorkspace = source.Workspace;
        sourceWorkspace.AddNavigator("child", TuiTestModel.Path("model", "Child"));
        sourceWorkspace.ReorderNavigator(sourceWorkspace.Navigators[1].Id, 0);
        source.SetPresentationConfiguration("child", new(2, 3, 60, 18));
        source.SetPresentationConfiguration("root", new(4, 5, 30, 9));
        source.RestoreLayout(new TuiLayoutSnapshot(
            (WorkspaceSnapshot)sourceWorkspace.CreateSnapshot("root"),
            source.Presentations.ToDictionary(
                static item => item.Navigator.Name,
                static item => item.Configuration)));

        var layout = (TuiLayoutSnapshot)source.CreateLayoutSnapshot();
        var json = JsonSerializer.Serialize(layout);
        var deserialized = JsonSerializer.Deserialize<TuiLayoutSnapshot>(json)!;
        using var target = TuiFrontend.CreateWorkspace(host, new TuiFrontendOptions
        {
            Startup = new RestoreLayout(deserialized),
        });
        var targetWorkspace = target.Workspace;

        Assert.Equal(["child", "root"], targetWorkspace.Navigators.Select(static item => item.Name));
        Assert.Equal(["/model/Child", "/model"], targetWorkspace.Navigators.Select(static item => item.CurrentPath.ToString()));
        Assert.Equal("root", target.SelectedNavigatorName);
        Assert.Equal(new NavigatorPresentationConfiguration(2, 3, 60, 18), target.Presentations.ElementAt(0).Configuration);
        Assert.Equal(new NavigatorPresentationConfiguration(4, 5, 30, 9), target.Presentations.ElementAt(1).Configuration);
        Assert.DoesNotContain("Version", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingInvalidAndOrphanedConfigurationUseDeterministicDefaults()
    {
        using var host = TuiTestModel.CreateHost();
        var layout = new TuiLayoutSnapshot(
            new WorkspaceSnapshot(
                [
                    new NavigatorSnapshot("missing", [new MemberPathSegmentSnapshot("model")]),
                    new NavigatorSnapshot("invalid", [new MemberPathSegmentSnapshot("model")]),
                    new NavigatorSnapshot("saved", [new MemberPathSegmentSnapshot("model")]),
                ],
                "saved"),
            new Dictionary<string, NavigatorPresentationConfiguration>
            {
                ["invalid"] = new(-1, 0, 0, 12),
                ["saved"] = new(8, 9, 70, 20),
                ["orphan"] = new(1, 1, 1, 1),
            });

        using var tui = TuiFrontend.CreateWorkspace(host, new TuiFrontendOptions
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
        var layout = new TuiLayoutSnapshot(
            new WorkspaceSnapshot(
                [new NavigatorSnapshot("broken", [
                    new MemberPathSegmentSnapshot("model"),
                    new MemberPathSegmentSnapshot("Missing"),
                ])],
                "broken"),
            new Dictionary<string, NavigatorPresentationConfiguration>
            {
                ["broken"] = new(5, 6, 50, 15),
            });
        using var tui = TuiFrontend.CreateWorkspace(host, new TuiFrontendOptions
        {
            Startup = new RestoreLayout(layout),
        });
        var coreWorkspace = tui.Workspace;

        var presentation = Assert.Single(tui.Presentations);
        Assert.Equal(PlaceholderNodeState.Broken, presentation.CurrentControl.State);
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
        using var tui = TuiFrontend.CreateWorkspace(host, new TuiFrontendOptions
        {
            Startup = new AddNavigator("main", TuiTestModel.Path("model")),
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
}
