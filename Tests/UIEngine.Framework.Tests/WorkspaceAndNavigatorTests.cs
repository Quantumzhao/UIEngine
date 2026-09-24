using System.ComponentModel.DataAnnotations;
using UIEngine.Core;
using UIEngine.Core.Attributes;
using Xunit;

namespace UIEngine.Framework.Tests;

public sealed class WorkspaceAndNavigatorTests
{
    [Fact]
    public void NavigatorCanStartFromRootPathAndResolvedOccurrenceWithFreshStacks()
    {
        var model = new _Model();
        using var host = _CreateHost(model);
        using var workspace = new UIEngineWorkspace(host);
        var path = _Path(_Member("model"), _Member("Child"), _Member("Value"));
        var supplied = host.ResolvePath(path).RightValue().ResolutionChain[^1];
        var changes = new List<WorkspaceChange>();
        workspace.Changed += (_, eventArgs) => changes.Add(eventArgs.Change);

        var rootAdded = workspace.AddNavigator("root", "model");
        var pathAdded = workspace.AddNavigator("path", path);
        var occurrenceAdded = workspace.AddNavigator("occurrence", supplied);

        Assert.True(rootAdded.IsRight);
        Assert.True(pathAdded.IsRight);
        Assert.True(occurrenceAdded.IsRight);
        var rootNavigator = workspace.Navigators[0];
        var pathNavigator = workspace.Navigators[1];
        var occurrenceNavigator = workspace.Navigators[2];
        Assert.Equal("root", rootNavigator.Name);
        Assert.Single(rootNavigator.Entries);
        Assert.Equal(rootNavigator.Paths.Count, rootNavigator.Entries.Count);
        Assert.Equal(3, pathNavigator.Entries.Count);
        Assert.Equal(pathNavigator.Paths.Count, pathNavigator.Entries.Count);
        Assert.Equal(
            ["/model", "/model/Child", "/model/Child/Value"],
            pathNavigator.Paths.Select(static path => path.ToString()));
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
        using var workspace = new UIEngineWorkspace(host);
        workspace.AddNavigator("main", "model");
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
        Assert.Equal("/model/Missing", navigator.CurrentPath.ToString());

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
        using var workspace = new UIEngineWorkspace(host);
        workspace.AddNavigator("first", "model");
        var first = Assert.Single(workspace.Navigators);

        var duplicated = workspace.DuplicateNavigator(first.Id, "second");
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
        var workspace = new UIEngineWorkspace(host);
        workspace.AddNavigator("one", "model");
        workspace.AddNavigator("two", "model");
        workspace.AddNavigator("three", "model");
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
        Assert.True(host.ResolveRootNode("model").IsRight);
    }

    [Fact]
    public void InvalidStartIsVisibleAndNamesAndHostOwnershipAreEnforced()
    {
        using var host = _CreateHost(new _Model());
        using var otherHost = _CreateHost(new _Model());
        using var workspace = new UIEngineWorkspace(host);

        var broken = workspace.AddNavigator(
            "broken",
            _Path(_Member("model"), _Member("Missing")));
        var duplicateName = workspace.AddNavigator("broken", "model");

        Assert.True(broken.IsRight);
        var navigator = Assert.Single(workspace.Navigators);
        Assert.Equal(2, navigator.Entries.Count);
        Assert.False(navigator.CurrentEntry.IsRight);
        Assert.Equal(
            InteractionErrorCode.NOT_FOUND,
            navigator.CurrentEntry.LeftValue().Code);
        Assert.True(navigator.GoBack().IsRight);
        Assert.True(navigator.CurrentEntry.RightValue().IsSome);
        Assert.Equal(InteractionErrorCode.INVALID_INPUT, duplicateName.LeftValue().Code);
        Assert.Single(workspace.Navigators);
    }

    [Fact]
    public async Task WorkspaceDisposalLeavesHostAndStartedInvocationAlive()
    {
        var model = new _Model();
        using var host = _CreateHost(model);
        var workspace = new UIEngineWorkspace(host);
        workspace.AddNavigator(
            "work",
            _Path(_Member("model"), _Member("WorkAsync")));
        var navigator = Assert.Single(workspace.Navigators);
        var method = Assert.IsAssignableFrom<IMethodNode>(
            navigator.CurrentEntry.RightValue().SomeValue());
        var started = method.Invoke(new Dictionary<string, object?>());
        var resultTask = method.ResultTask!;

        workspace.Dispose();

        Assert.Equal(InvocationStatus.RUNNING, started.RightValue());
        Assert.False(resultTask.IsCompleted);
        Assert.Empty(navigator.Entries);
        Assert.True(host.ResolveRootNode("model").IsRight);

        model.CompleteWork(42);
        var completed = await resultTask;
        Assert.Equal(42, completed.RightValue());
        Assert.Equal(InvocationStatus.SUCCEEDED, method.Status);
    }

    private static UIEngineHost _CreateHost(_Model model)
    {
        var host = new UIEngineHost();
        host.SetRoot("model", model);
        return host;
    }

    private static LogicalPath _Path(params ILogicalPathSegment[] segments)
    {
        var path = LogicalPath.Root;
        foreach (var segment in segments)
        {
            path = path.Append(segment);
        }

        return path;
    }

    private static MemberLogicalPathSegment _Member(string name) => new(name);

    private sealed class _Model
    {
        private readonly TaskCompletionSource<int> _Work =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

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
