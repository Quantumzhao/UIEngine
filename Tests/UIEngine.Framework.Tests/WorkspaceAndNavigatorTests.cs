using System.ComponentModel.DataAnnotations;
using System.Reflection;
using UIEngine.Core;
using UIEngine.Core.Attributes;
using Xunit;

namespace UIEngine.Framework.Tests;

public sealed class WorkspaceAndNavigatorTests
{
    [Fact]
    public void NavigatorViewIsReadOnlyAndEveryMutationPublishesOneChange()
    {
        using var host = _CreateHost(new _Model());
        var workspace = new UIEngineWorkspace();
        var mutableView = Assert.IsAssignableFrom<ICollection<Navigator>>(workspace.Navigators);
        var changes = new List<WorkspaceChange>();
        workspace.Changed += (_, eventArgs) => changes.Add(eventArgs.Change);

        var added = workspace.AddNavigator(TestRoots.Id("model")).RightValue();
        Assert.Throws<NotSupportedException>(() => mutableView.Add(workspace.Navigators[0]));
        var duplicated = workspace.DuplicateNavigator(added.NavigatorId).RightValue();
        var reordered = workspace.ReorderNavigator(duplicated.NavigatorId, 0).RightValue();
        var restoredSnapshotId = Guid.NewGuid();
        var restored = workspace.RestoreSnapshot(new WorkspaceSnapshot(
            [new NavigatorSnapshot(restoredSnapshotId, [TestRoots.Snapshot("model")])],
            restoredSnapshotId));
        var restoredId = Assert.Single(workspace.Navigators).Id;
        var removed = workspace.RemoveNavigator(restoredId).RightValue();
        workspace.AddNavigator(TestRoots.Id("model"));
        var disposedId = Assert.Single(workspace.Navigators).Id;

        workspace.Dispose();

        Assert.Empty(restored.Issues);
        Assert.Same(added, changes[0]);
        Assert.Same(duplicated, changes[1]);
        Assert.Same(reordered, changes[2]);
        Assert.Collection(
            changes.Skip(3),
            change => Assert.IsType<NavigatorRemovedChange>(change),
            change => Assert.IsType<NavigatorRemovedChange>(change),
            change => Assert.IsType<NavigatorAddedChange>(change),
            change => Assert.Same(removed, change),
            change => Assert.IsType<NavigatorAddedChange>(change),
            change => Assert.Equal(disposedId, Assert.IsType<NavigatorRemovedChange>(change).NavigatorId));
    }

    [Fact]
    public void NavigatorHasNoWorkspaceReferenceAndWorkspaceRelaysItsChanges()
    {
        var fields = typeof(Navigator).GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.DoesNotContain(fields, static field => field.FieldType == typeof(UIEngineWorkspace));
        Assert.DoesNotContain(fields, static field => field.FieldType == typeof(UIEngineHost));

        using var host = _CreateHost(new _Model());
        using var workspace = new UIEngineWorkspace();
        workspace.AddNavigator(TestRoots.Id("model"));
        var navigator = Assert.Single(workspace.Navigators);
        var changes = new List<WorkspaceChange>();
        workspace.Changed += (_, eventArgs) => changes.Add(eventArgs.Change);

        var pushed = navigator.Navigate(_Member("Child")).RightValue();
        var popped = navigator.GoBack().RightValue();

        Assert.Equal([pushed, popped], changes);
    }

    [Fact]
    public void NavigatorCanStartFromRootPathAndResolvedOccurrenceWithFreshStacks()
    {
        var model = new _Model();
        using var host = _CreateHost(model);
        using var workspace = new UIEngineWorkspace();
        var path = _Path(_Member("model"), _Member("Child"), _Member("Value"));
        var supplied = PathResolution.Resolve(path).RightValue().ResolutionChain[^1];
        var changes = new List<WorkspaceChange>();
        workspace.Changed += (_, eventArgs) => changes.Add(eventArgs.Change);

        var rootAdded = workspace.AddNavigator(TestRoots.Id("model"));
        var pathAdded = workspace.AddNavigator(path);
        var occurrenceAdded = workspace.AddNavigator(supplied);

        Assert.True(rootAdded.IsRight);
        Assert.True(pathAdded.IsRight);
        Assert.True(occurrenceAdded.IsRight);
        var rootNavigator = workspace.Navigators[0];
        var pathNavigator = workspace.Navigators[1];
        var occurrenceNavigator = workspace.Navigators[2];
        Assert.NotEqual(Guid.Empty, rootNavigator.Id);
        Assert.Single(rootNavigator.Entries);
        Assert.Equal(rootNavigator.Paths.Count, rootNavigator.Entries.Count);
        Assert.Equal(3, pathNavigator.Entries.Count);
        Assert.Equal(pathNavigator.Paths.Count, pathNavigator.Entries.Count);
        Assert.Equal(
            ["/model", "/model/Child", "/model/Child/Value"],
            pathNavigator.Paths.Select(static path => path.ToDisplayString().RightValue()));
        Assert.NotSame(
            supplied.Node,
            occurrenceNavigator.CurrentEntry.RightValue().SomeValue());
        Assert.NotSame(
            pathNavigator.CurrentEntry.RightValue().SomeValue(),
            occurrenceNavigator.CurrentEntry.RightValue().SomeValue());
        Assert.Same(rootAdded.RightValue(), changes[0]);
        Assert.Same(pathAdded.RightValue(), changes[1]);
        Assert.Same(occurrenceAdded.RightValue(), changes[2]);
    }

    [Fact]
    public void NavigatorPushesBrokenEntriesAndBackIsDestructive()
    {
        using var host = _CreateHost(new _Model());
        using var workspace = new UIEngineWorkspace();
        workspace.AddNavigator(TestRoots.Id("model"));
        var navigator = Assert.Single(workspace.Navigators);
        var changes = new List<WorkspaceChange>();
        workspace.Changed += (_, eventArgs) => changes.Add(eventArgs.Change);

        var missing = navigator.Navigate(_Member("Missing"));

        Assert.True(missing.IsRight);
        Assert.Same(missing.RightValue(), Assert.Single(changes));
        Assert.False(navigator.CurrentEntry.IsRight);
        Assert.Equal(
            InteractionErrorCode.NOT_FOUND,
            navigator.CurrentEntry.LeftValue().Code);
        Assert.Equal("/model/Missing", navigator.CurrentPath.ToDisplayString().RightValue());

        var fromBroken = navigator.Navigate(_Member("Anything"));
        Assert.False(fromBroken.IsRight);
        Assert.Equal(InteractionErrorCode.INVALID_INPUT, fromBroken.LeftValue().Code);
        Assert.Single(changes);

        var back = navigator.GoBack();
        Assert.True(back.IsRight);
        Assert.Same(
            missing.RightValue().Entry.LeftValue(),
            back.RightValue().RemovedEntry.LeftValue());
        Assert.Equal(missing.RightValue().Path, back.RightValue().RemovedPath);
        Assert.Single(navigator.Entries);
        Assert.Single(navigator.Paths);
        Assert.True(navigator.CurrentEntry.RightValue().IsSome);

        var name = navigator.Navigate(_Member("Name"));
        var terminalNode = navigator.CurrentEntry.RightValue().SomeValue();
        var terminalRejection = navigator.Navigate(_Member("Length"));
        Assert.True(name.IsRight);
        Assert.False(terminalRejection.IsRight);
        Assert.Equal(InteractionErrorCode.UNSUPPORTED, terminalRejection.LeftValue().Code);
        Assert.Same(terminalNode, navigator.CurrentEntry.RightValue().SomeValue());
        Assert.Equal(2, navigator.Entries.Count);

        navigator.GoBack();
        var rootBack = navigator.GoBack();
        Assert.False(rootBack.IsRight);
        Assert.Equal(InteractionErrorCode.INVALID_INPUT, rootBack.LeftValue().Code);
        Assert.Single(navigator.Entries);
    }

    [Fact]
    public void DuplicateHasIndependentNodesAndStackButSharesLiveDomainState()
    {
        var model = new _Model();
        using var host = _CreateHost(model);
        using var workspace = new UIEngineWorkspace();
        workspace.AddNavigator(TestRoots.Id("model"));
        var first = Assert.Single(workspace.Navigators);

        var duplicated = workspace.DuplicateNavigator(first.Id);
        var second = workspace.Navigators[1];

        Assert.True(duplicated.IsRight);
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotSame(
            first.CurrentEntry.RightValue().SomeValue(),
            second.CurrentEntry.RightValue().SomeValue());

        first.Navigate(_Member("Count"));
        second.Navigate(_Member("Count"));
        Assert.NotSame(
            first.CurrentEntry.RightValue().SomeValue(),
            second.CurrentEntry.RightValue().SomeValue());
        var written = ((IWritableValueNode)first.CurrentEntry
            .RightValue()
            .SomeValue()).WriteValue(7);
        var read = ((IReadableValueNode)second.CurrentEntry
            .RightValue()
            .SomeValue()).ReadValue();

        Assert.Equal(7, written.RightValue());
        Assert.Equal(7, read.RightValue());
        first.GoBack();
        Assert.Single(first.Entries);
        Assert.Equal(2, second.Entries.Count);

        workspace.RemoveNavigator(first.Id);
        Assert.Single(workspace.Navigators);
        Assert.Same(second, workspace.Navigators[0]);
        Assert.Equal(
            7,
            ((IReadableValueNode)second.CurrentEntry.RightValue().SomeValue())
                .ReadValue()
                .RightValue());
    }

    [Fact]
    public void ReorderRemoveAndDisposePublishExactDeterministicChanges()
    {
        using var host = _CreateHost(new _Model());
        var workspace = new UIEngineWorkspace();
        workspace.AddNavigator(TestRoots.Id("model"));
        workspace.AddNavigator(TestRoots.Id("model"));
        workspace.AddNavigator(TestRoots.Id("model"));
        var one = workspace.Navigators[0];
        var two = workspace.Navigators[1];
        var three = workspace.Navigators[2];
        var changes = new List<WorkspaceChange>();
        workspace.Changed += (_, eventArgs) => changes.Add(eventArgs.Change);

        var reordered = workspace.ReorderNavigator(one.Id, 2);
        var removed = workspace.RemoveNavigator(two.Id);

        Assert.True(reordered.IsRight);
        Assert.Same(reordered.RightValue(), changes[0]);
        Assert.Equal([three.Id, one.Id], workspace.Navigators.Select(static item => item.Id));
        Assert.True(removed.IsRight);
        Assert.Same(removed.RightValue(), changes[1]);
        Assert.Equal(0, removed.RightValue().PreviousIndex);
        Assert.Single(removed.RightValue().RemovedEntries);
        Assert.Single(removed.RightValue().RemovedPaths);
        Assert.Empty(two.Entries);
        Assert.Empty(two.Paths);

        workspace.Dispose();

        var disposalChanges = changes.OfType<NavigatorRemovedChange>().Skip(1).ToArray();
        Assert.Equal([one.Id, three.Id], disposalChanges.Select(static change => change.NavigatorId));
        Assert.All(disposalChanges, static change => Assert.Single(change.RemovedEntries));
        Assert.All(disposalChanges, static change => Assert.Single(change.RemovedPaths));
        Assert.Empty(workspace.Navigators);
        Assert.Empty(one.Entries);
        Assert.Empty(one.Paths);
        Assert.Empty(three.Entries);
        Assert.Empty(three.Paths);
        Assert.True(host.ResolveRootNode(TestRoots.Id("model")).IsRight);
    }

    [Fact]
    public void InvalidStartIsVisibleAndSamePathNavigatorsHaveDistinctIds()
    {
        using var host = _CreateHost(new _Model());
        using var workspace = new UIEngineWorkspace();

        var broken = workspace.AddNavigator(
            _Path(_Member("model"), _Member("Missing")));
        var samePath = workspace.AddNavigator(TestRoots.Id("model"));

        Assert.True(broken.IsRight);
        var navigator = workspace.Navigators[0];
        Assert.Equal(2, navigator.Entries.Count);
        Assert.False(navigator.CurrentEntry.IsRight);
        Assert.Equal(
            InteractionErrorCode.NOT_FOUND,
            navigator.CurrentEntry.LeftValue().Code);
        Assert.True(navigator.GoBack().IsRight);
        Assert.True(navigator.CurrentEntry.RightValue().IsSome);
        Assert.True(samePath.IsRight);
        Assert.Equal(2, workspace.Navigators.Count);
        Assert.NotEqual(workspace.Navigators[0].Id, workspace.Navigators[1].Id);
    }

    [Fact]
    public async Task WorkspaceDisposalLeavesHostAndStartedInvocationAlive()
    {
        var model = new _Model();
        using var host = _CreateHost(model);
        var workspace = new UIEngineWorkspace();
        workspace.AddNavigator(_Path(_Member("model"), _Member("WorkAsync")));
        var navigator = Assert.Single(workspace.Navigators);
        var method = Assert.IsAssignableFrom<IMethodNode>(
            navigator.CurrentEntry.RightValue().SomeValue());
        var started = method.Invoke(new Dictionary<string, object?>());
        var resultTask = method.ResultTask!;

        workspace.Dispose();

        Assert.Equal(InvocationStatus.RUNNING, started.RightValue());
        Assert.False(resultTask.IsCompleted);
        Assert.Empty(navigator.Entries);
        Assert.True(host.ResolveRootNode(TestRoots.Id("model")).IsRight);

        model.CompleteWork(42);
        var completed = await resultTask;
        Assert.Equal(42, completed.RightValue());
        Assert.Equal(InvocationStatus.SUCCEEDED, method.Status);
    }

    private static UIEngineHost _CreateHost(_Model model)
    {
        var host = new UIEngineHost();
        TestRoots.Set("model", model);
        return host;
    }

    private static LogicalPath _Path(params ILogicalPathSegment[] segments)
    {
        var path = LogicalPath.Empty;
        for (var index = 0; index < segments.Length; index++)
        {
            var segment = index == 0 && segments[index] is MemberLogicalPathSegment root
                ? TestRoots.Segment(root.Name)
                : segments[index];
            path = path.Append(segment);
        }

        return path;
    }

    private static MemberLogicalPathSegment _Member(string name) => new(name);

    private sealed class _Model
    {
        private readonly TaskCompletionSource<int> _Work = new();

        [Expose]
        [Range(0, 10)]
        public int Count { get; set; }

        [Expose]
        public string Name { get; set; } = "model";

        [Children]
        public _Child Child { get; } = new();

        [Action]
        public async Task<int> WorkAsync() => await _Work.Task;

        public void CompleteWork(int value) => _Work.SetResult(value);
    }

    private sealed class _Child
    {
        [Expose]
        public string Value { get; set; } = "child";
    }
}
