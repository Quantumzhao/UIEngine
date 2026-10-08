using LanguageExt;
using UIEngine.Core;
using UIEngine.Frontend.Tui;
using Xunit;
using static LanguageExt.Prelude;

namespace UIEngine.Frontend.Tui.Tests;

[Collection("Terminal application")]
public sealed class TuiWorkspaceTests
{
    [Fact]
    public void EmptyAndAddNavigatorStartupModesAreExplicit()
    {
        using var host = TuiTestModel.CreateHost();
        using var empty = TuiFrontend.CreateWorkspace(host);

        Assert.Empty(empty.Workspace.Navigators);
        Assert.Empty(empty.Presentations);
        Assert.Null(empty.SelectedNavigatorName);

        using var healthy = TuiFrontend.CreateWorkspace(host, new TuiFrontendOptions
        {
            Startup = new AddNavigator("healthy", TuiTestModel.Path("model")),
        });
        using var broken = TuiFrontend.CreateWorkspace(host, new TuiFrontendOptions
        {
            Startup = new AddNavigator("broken", TuiTestModel.Path("model", "Missing")),
        });

        Assert.Equal(PlaceholderNodeState.Healthy, Assert.Single(healthy.Presentations).CurrentControl.State);
        Assert.Equal(PlaceholderNodeState.Broken, Assert.Single(broken.Presentations).CurrentControl.State);
    }

    [Fact]
    public void RestoredStartupBuildsWorkspaceBeforeItIsReturned()
    {
        using var host = TuiTestModel.CreateHost();
        var layout = new TuiLayoutSnapshot(
            new WorkspaceSnapshot(
                [
                    new NavigatorSnapshot("first", [new MemberPathSegmentSnapshot("model")]),
                    new NavigatorSnapshot("broken", [
                        new MemberPathSegmentSnapshot("model"),
                        new MemberPathSegmentSnapshot("Missing"),
                    ]),
                ],
                "broken"),
            new Dictionary<string, NavigatorPresentationConfiguration>());

        using var tui = TuiFrontend.CreateWorkspace(host, new TuiFrontendOptions
        {
            Startup = new RestoreLayout(layout),
        });

        Assert.Equal(["first", "broken"], tui.Workspace.Navigators.Select(static item => item.Name));
        Assert.Equal("broken", tui.SelectedNavigatorName);
        Assert.Equal(PlaceholderNodeState.Broken, tui.Presentations.ElementAt(1).CurrentControl.State);
    }

    [Fact]
    public void InvalidOptionsDoNotDamageCallerOwnedHost()
    {
        using var host = TuiTestModel.CreateHost();

        Assert.Throws<ArgumentException>(() => TuiFrontend.CreateWorkspace(host, new TuiFrontendOptions
        {
            ApplicationTitle = " ",
        }));
        Assert.Throws<ArgumentException>(() => new AddNavigator(" ", TuiTestModel.Path("model")));
        Assert.Throws<ArgumentNullException>(() => new AddNavigator("main", null!));
        Assert.Throws<ArgumentNullException>(() => new RestoreLayout(null!));
        Assert.Throws<ArgumentNullException>(() => TuiFrontend.CreateWorkspace(
            host,
            new TuiFrontendOptions { Startup = null! }));

        Assert.True(host.ResolveRootNode("model").IsRight);
    }

    [Fact]
    public void AddDuplicatePushPopReorderAndRemoveHaveAtomicLifetimes()
    {
        using var host = TuiTestModel.CreateHost();
        using var tui = TuiFrontend.CreateWorkspace(host, new TuiFrontendOptions
        {
            Startup = new AddNavigator("first", TuiTestModel.Path("model")),
        });
        var coreWorkspace = tui.Workspace;
        var firstPresentation = Assert.Single(tui.Presentations);
        var firstControl = firstPresentation.CurrentControl;

        coreWorkspace.AddNavigator("second", "model");
        var secondPresentation = tui.Presentations.ElementAt(1);
        coreWorkspace.DuplicateNavigator(coreWorkspace.Navigators[0].Id, "duplicate");
        var duplicatePresentation = tui.Presentations.ElementAt(1);

        Assert.Equal(3, tui.Presentations.Count);
        Assert.NotSame(firstPresentation, duplicatePresentation);
        Assert.NotSame(firstControl, duplicatePresentation.CurrentControl);
        Assert.NotSame(secondPresentation.CurrentControl, duplicatePresentation.CurrentControl);

        var firstNavigator = coreWorkspace.Navigators[0];
        firstNavigator.Navigate(new MemberLogicalPathSegment("Child"));
        var pushedControl = firstPresentation.CurrentControl;
        Assert.True(firstControl.IsDisposed);
        Assert.NotSame(firstControl, pushedControl);
        firstNavigator.GoBack();
        var poppedControl = firstPresentation.CurrentControl;
        Assert.True(pushedControl.IsDisposed);
        Assert.NotSame(pushedControl, poppedControl);

        coreWorkspace.ReorderNavigator(firstNavigator.Id, 2);
        Assert.Same(firstPresentation, tui.Presentations.ElementAt(2));
        Assert.Same(poppedControl, firstPresentation.CurrentControl);

        coreWorkspace.RemoveNavigator(duplicatePresentation.Navigator.Id);
        Assert.True(duplicatePresentation.IsDisposed);
        Assert.True(duplicatePresentation.CurrentControl.IsDisposed);
        Assert.False(secondPresentation.IsDisposed);
        Assert.Same(
            secondPresentation.CurrentControl,
            tui.Presentations.ElementAt(0).CurrentControl);

        coreWorkspace.Dispose();
        Assert.Empty(tui.Presentations);
        Assert.True(firstPresentation.IsDisposed);
        Assert.True(secondPresentation.IsDisposed);
    }

    [Fact]
    public void PlaceholderDistinguishesHealthyEmptyAndBrokenEntries()
    {
        using var host = TuiTestModel.CreateHost();
        var healthyNode = ((ResolvedNode)host.ResolveRootNode("model")).Node;
        using var healthy = new PlaceholderNodeControl(Right(Some(healthyNode)));
        using var empty = new PlaceholderNodeControl(Right<InteractionError, Option<BaseNode>>(None));
        using var broken = new PlaceholderNodeControl(Left<InteractionError, Option<BaseNode>>(
            new InteractionError(InteractionErrorCode.NOT_FOUND, "missing")));

        Assert.Equal(PlaceholderNodeState.Healthy, healthy.State);
        Assert.Equal(PlaceholderNodeState.Empty, empty.State);
        Assert.Equal(PlaceholderNodeState.Broken, broken.State);
    }
}
