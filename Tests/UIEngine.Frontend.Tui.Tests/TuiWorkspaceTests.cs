using LanguageExt;
using UIEngine.Core;
using UIEngine.Frontend.Tui;
using XenoAtom.Terminal.UI;
using XenoAtom.Terminal.UI.Commands;
using XenoAtom.Terminal.UI.Controls;
using XenoAtom.Terminal.UI.Styling;
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
        using var empty = TuiFrontend.CreateWorkspace();

        Assert.Empty(empty.Workspace.Navigators);
        Assert.Empty(empty.Presentations);
        Assert.Null(empty.SelectedNavigatorId);

        using var healthy = TuiFrontend.CreateWorkspace(new TuiFrontendOptions
        {
            Startup = new AddNavigator(TuiTestModel.Path("model")),
        });
        using var broken = TuiFrontend.CreateWorkspace(new TuiFrontendOptions
        {
            Startup = new AddNavigator(TuiTestModel.Path("model", "Missing")),
        });

        Assert.Equal(PlaceholderNodeState.Healthy, Assert.Single(healthy.Presentations).CurrentControl.State);
        Assert.Equal(PlaceholderNodeState.Invalid, Assert.Single(broken.Presentations).CurrentControl.State);
    }

    [Fact]
    public void RestoredStartupBuildsWorkspaceBeforeItIsReturned()
    {
        using var host = TuiTestModel.CreateHost();
        var firstId = Guid.NewGuid();
        var brokenId = Guid.NewGuid();
        var layout = new TuiLayoutSnapshot(
            new WorkspaceSnapshot(
                [
                    new NavigatorSnapshot(firstId, [TuiTestRoots.Snapshot]),
                    new NavigatorSnapshot(brokenId, [
                        TuiTestRoots.Snapshot,
                        new MemberPathSegmentSnapshot("Missing"),
                    ]),
                ],
                brokenId),
            new Dictionary<Guid, NavigatorPresentationConfiguration>());

        using var tui = TuiFrontend.CreateWorkspace(new TuiFrontendOptions
        {
            Startup = new RestoreLayout(layout),
        });

        Assert.Equal([firstId, brokenId], tui.Workspace.Navigators.Select(static item => item.Id));
        Assert.Equal(brokenId, tui.SelectedNavigatorId);
        Assert.Equal(PlaceholderNodeState.Invalid, tui.Presentations.ElementAt(1).CurrentControl.State);
    }

    [Fact]
    public void InvalidOptionsDoNotDamageCallerOwnedHost()
    {
        using var host = TuiTestModel.CreateHost();

        Assert.Throws<ArgumentException>(() => TuiFrontend.CreateWorkspace(new TuiFrontendOptions
        {
            ApplicationTitle = " ",
        }));
        Assert.Throws<ArgumentNullException>(() => new AddNavigator(null!));
        Assert.Throws<ArgumentNullException>(() => new RestoreLayout(null!));
        Assert.Throws<ArgumentNullException>(() => TuiFrontend.CreateWorkspace(
            new TuiFrontendOptions { Startup = null! }));

        Assert.True(host.ResolveRootNode(TuiTestModel.RootId).IsRight);
    }

    [Fact]
    public void AddDuplicatePushPopReorderAndRemoveHaveAtomicLifetimes()
    {
        using var host = TuiTestModel.CreateHost();
        using var tui = TuiFrontend.CreateWorkspace(new TuiFrontendOptions
        {
            Startup = new AddNavigator(TuiTestModel.Path("model")),
        });
        var coreWorkspace = tui.Workspace;
        var firstPresentation = Assert.Single(tui.Presentations);
        var firstControl = firstPresentation.CurrentControl;

        coreWorkspace.AddNavigator(TuiTestModel.RootId);
        var secondPresentation = tui.Presentations.ElementAt(1);
        coreWorkspace.DuplicateNavigator(coreWorkspace.Navigators[0].Id);
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
        var healthyNode = ((ResolvedNode)host.ResolveRootNode(TuiTestModel.RootId)).Node;
        using var healthy = new PlaceholderNodeControl(Right(Some(healthyNode)));
        using var empty = new PlaceholderNodeControl(Right<InteractionError, Option<BaseNode>>(None));
        using var broken = new PlaceholderNodeControl(Left<InteractionError, Option<BaseNode>>(
            new InteractionError(InteractionErrorCode.NOT_FOUND, "missing")));

        Assert.Equal(PlaceholderNodeState.Healthy, healthy.State);
        Assert.Equal(PlaceholderNodeState.Empty, empty.State);
        Assert.Equal(PlaceholderNodeState.Invalid, broken.State);
    }

    [Fact]
    public void SuspensionIsPresentationOnlyAndRestoresExactLiveObjects()
    {
        using var host = TuiTestModel.CreateHost();
        using var tui = TuiFrontend.CreateWorkspace(new TuiFrontendOptions
        {
            Startup = new AddNavigator(TuiTestModel.Path("model")),
        });
        tui.Workspace.AddNavigator(TuiTestModel.Path("model", "Child"));
        var firstNavigator = tui.Workspace.Navigators[0];
        var firstNode = ((Option<BaseNode>)firstNavigator.CurrentEntry)
            .IfNoneUnsafe((BaseNode?)null)!;
        var firstPresentation = tui.Presentations.ElementAt(0);
        var firstControl = firstPresentation.CurrentControl;

        _Execute(tui, TuiCommandIds.Suspend);

        Assert.Equal(2, tui.Workspace.Navigators.Count);
        Assert.Same(firstNavigator, tui.Workspace.Navigators[0]);
        Assert.Same(
            firstNode,
            ((Option<BaseNode>)firstNavigator.CurrentEntry).IfNoneUnsafe((BaseNode?)null));
        Assert.Same(firstPresentation, tui.SuspendedPresentation);
        Assert.Same(
            firstControl,
            Assert.IsType<NavigatorPresentation>(tui.SuspendedPresentation).CurrentControl);
        Assert.False(firstPresentation.IsDisposed);
        Assert.DoesNotContain(firstPresentation, tui.Presentations);
        var hiddenSnapshot = (TuiLayoutSnapshot)tui.CreateLayoutSnapshot();
        Assert.DoesNotContain(hiddenSnapshot.Workspace.Navigators, item => item.Id == firstNavigator.Id);
        Assert.DoesNotContain(firstNavigator.Id, hiddenSnapshot.Navigators.Keys);

        _Execute(tui, TuiCommandIds.Restore);

        Assert.Null(tui.SuspendedPresentation);
        Assert.Same(firstNavigator, tui.Workspace.Navigators[0]);
        Assert.Same(firstPresentation, tui.Presentations.ElementAt(0));
        Assert.Same(firstControl, firstPresentation.CurrentControl);

        _Execute(tui, TuiCommandIds.Suspend);
        var secondNavigator = tui.Workspace.Navigators[1];
        var secondPresentation = Assert.Single(tui.Presentations);
        _Execute(tui, TuiCommandIds.Suspend);

        Assert.Empty(firstNavigator.Entries);
        Assert.True(firstPresentation.IsDisposed);
        Assert.Same(secondNavigator, Assert.Single(tui.Workspace.Navigators));
        Assert.Same(secondPresentation, tui.SuspendedPresentation);
        Assert.Empty(tui.Presentations);
        Assert.Null(tui.SelectedNavigatorId);
    }

    [Fact]
    public void PaneTitlesAndSharedStatusUseTextAndSemanticColors()
    {
        using var host = TuiTestModel.CreateHost();
        using var healthy = TuiFrontend.CreateWorkspace(new TuiFrontendOptions
        {
            Startup = new AddNavigator(TuiTestModel.Path("model")),
        });
        using var broken = TuiFrontend.CreateWorkspace(new TuiFrontendOptions
        {
            Startup = new AddNavigator(TuiTestModel.Path("model", "Missing")),
        });
        var healthyPresentation = Assert.Single(healthy.Presentations);
        var brokenPresentation = Assert.Single(broken.Presentations);
        var healthyTitle = Assert.IsType<TextBlock>(healthyPresentation.Header.Content);
        var brokenTitle = Assert.IsType<TextBlock>(brokenPresentation.Header.Content);

        Assert.Equal("Model", healthyTitle.Text);
        Assert.Equal("Invalid", brokenTitle.Text);
        Assert.Equal(Colors.Green, healthyTitle.GetStyle<TextBlockStyle>().Foreground);
        Assert.Equal(Colors.Red, brokenTitle.GetStyle<TextBlockStyle>().Foreground);
        Assert.Equal("Healthy", healthy.StateText);
        Assert.Equal("Invalid", broken.StateText);
        Assert.Equal("/Model/Missing", broken.AddressText);
        Assert.DoesNotContain(healthyPresentation.Navigator.Id.ToString(), healthyTitle.Text);
        Assert.DoesNotContain("1", healthyTitle.Text);
        var border = healthyPresentation.Container
            .GetStyle<GroupStyle>()
            .BorderCellStyle;
        Assert.True(border.HasValue);
        Assert.True(border.Value.TryGetForeground(out var selectedBorder));
        Assert.Equal(Colors.Cyan, selectedBorder);

        using var empty = new PlaceholderNodeControl(
            Right<InteractionError, Option<BaseNode>>(None));
        Assert.Equal("Empty", empty.DisplayName);
        Assert.Equal(Colors.Yellow, empty.Visual.GetStyle<TextBlockStyle>().Foreground);
    }

    private static void _Execute(TuiWorkspace tui, string commandId)
    {
        var command = Assert.Single(tui.Visual.Commands, command => command.Id == commandId);
        Assert.True(command.CanExecuteFor(tui.Visual));
        command.Execute(tui.Visual);
    }
}
