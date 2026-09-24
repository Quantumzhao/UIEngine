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
        var supplied = host.ResolvePath(path).Value.ResolutionChain[^1];
        var changes = new List<WorkspaceChange>();
        workspace.Changed += (_, eventArgs) => changes.Add(eventArgs.Change);

        var rootAdded = workspace.AddNavigator("root", "model");
        var pathAdded = workspace.AddNavigator("path", path);
        var occurrenceAdded = workspace.AddNavigator("occurrence", supplied);

        Assert.True(rootAdded.IsSuccess);
        Assert.True(pathAdded.IsSuccess);
        Assert.True(occurrenceAdded.IsSuccess);
        var rootNavigator = workspace.Navigators[0];
        var pathNavigator = workspace.Navigators[1];
        var occurrenceNavigator = workspace.Navigators[2];
        Assert.Equal("root", rootNavigator.Name);
        Assert.Single(rootNavigator.Entries);
        Assert.Equal(3, pathNavigator.Entries.Count);
        Assert.Equal(
            ["/model", "/model/Child", "/model/Child/Value"],
            pathNavigator.Entries.Select(static entry => entry.Path.ToString()));
        Assert.NotSame(supplied.Node, occurrenceNavigator.CurrentEntry.Node);
        Assert.NotSame(pathNavigator.CurrentEntry.Node, occurrenceNavigator.CurrentEntry.Node);
        Assert.Same(rootAdded.Value, changes[0]);
        Assert.Same(pathAdded.Value, changes[1]);
        Assert.Same(occurrenceAdded.Value, changes[2]);
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

        Assert.True(missing.IsSuccess);
        Assert.Same(missing.Value, Assert.Single(changes));
        Assert.False(navigator.CurrentEntry.IsResolved);
        Assert.Equal(InteractionErrorCode.NOT_FOUND, navigator.CurrentEntry.Error?.Code);
        Assert.Equal("/model/Missing", navigator.CurrentEntry.Path.ToString());

        var fromBroken = navigator.Navigate(_Member("Anything"));
        Assert.False(fromBroken.IsSuccess);
        Assert.Equal(InteractionErrorCode.INVALID_INPUT, fromBroken.Error?.Code);
        Assert.Single(changes);

        var back = navigator.GoBack();
        Assert.True(back.IsSuccess);
        Assert.Same(missing.Value.Entry, back.Value.RemovedEntry);
        Assert.Single(navigator.Entries);
        Assert.True(navigator.CurrentEntry.IsResolved);

        var name = navigator.Navigate(_Member("Name"));
        var terminalEntry = navigator.CurrentEntry;
        var terminalRejection = navigator.Navigate(_Member("Length"));
        Assert.True(name.IsSuccess);
        Assert.False(terminalRejection.IsSuccess);
        Assert.Equal(InteractionErrorCode.UNSUPPORTED, terminalRejection.Error?.Code);
        Assert.Same(terminalEntry, navigator.CurrentEntry);
        Assert.Equal(2, navigator.Entries.Count);

        navigator.GoBack();
        var rootBack = navigator.GoBack();
        Assert.False(rootBack.IsSuccess);
        Assert.Equal(InteractionErrorCode.INVALID_INPUT, rootBack.Error?.Code);
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

        Assert.True(duplicated.IsSuccess);
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotSame(first.CurrentEntry.Node, second.CurrentEntry.Node);

        first.Navigate(_Member("Count"));
        second.Navigate(_Member("Count"));
        Assert.NotSame(first.CurrentEntry.Node, second.CurrentEntry.Node);
        var written = ((IWritableValueNode)first.CurrentEntry.Node!).WriteValue(7);
        var read = ((IReadableValueNode)second.CurrentEntry.Node!).ReadValue();

        Assert.Equal(7, written.Value);
        Assert.Equal(7, read.Value);
        first.GoBack();
        Assert.Single(first.Entries);
        Assert.Equal(2, second.Entries.Count);

        workspace.RemoveNavigator(first.Id);
        Assert.Single(workspace.Navigators);
        Assert.Same(second, workspace.Navigators[0]);
        Assert.Equal(7, ((IReadableValueNode)second.CurrentEntry.Node!).ReadValue().Value);
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

        Assert.True(reordered.IsSuccess);
        Assert.Same(reordered.Value, changes[0]);
        Assert.Equal([three.Id, one.Id], workspace.Navigators.Select(static item => item.Id));
        Assert.True(removed.IsSuccess);
        Assert.Same(removed.Value, changes[1]);
        Assert.Equal(0, removed.Value.PreviousIndex);
        Assert.Single(removed.Value.RemovedEntries);
        Assert.Empty(two.Entries);
        Assert.Equal(
            InteractionErrorCode.DISPOSED,
            two.GoBack().Error?.Code);

        workspace.Dispose();

        var disposalChanges = changes.OfType<NavigatorRemovedChange>().Skip(1).ToArray();
        Assert.Equal([one.Id, three.Id], disposalChanges.Select(static change => change.NavigatorId));
        Assert.All(disposalChanges, static change => Assert.Single(change.RemovedEntries));
        Assert.Empty(workspace.Navigators);
        Assert.Empty(one.Entries);
        Assert.Empty(three.Entries);
        Assert.Equal(
            InteractionErrorCode.DISPOSED,
            workspace.AddNavigator("later", "model").Error?.Code);
        Assert.True(host.ResolveRootNode("model").IsSuccess);
    }

    [Fact]
    public void InvalidStartIsVisibleAndNamesAndHostOwnershipAreEnforced()
    {
        using var host = _CreateHost(new _Model());
        using var otherHost = _CreateHost(new _Model());
        using var workspace = new UIEngineWorkspace(host);
        var foreign = otherHost.ResolveRootNode("model").Value;

        var broken = workspace.AddNavigator(
            "broken",
            _Path(_Member("model"), _Member("Missing")));
        var duplicateName = workspace.AddNavigator("broken", "model");
        var foreignNode = workspace.AddNavigator("foreign", foreign);

        Assert.True(broken.IsSuccess);
        var navigator = Assert.Single(workspace.Navigators);
        Assert.Equal(2, navigator.Entries.Count);
        Assert.False(navigator.CurrentEntry.IsResolved);
        Assert.Equal(InteractionErrorCode.NOT_FOUND, navigator.CurrentEntry.Error?.Code);
        Assert.True(navigator.GoBack().IsSuccess);
        Assert.True(navigator.CurrentEntry.IsResolved);
        Assert.Equal(InteractionErrorCode.INVALID_INPUT, duplicateName.Error?.Code);
        Assert.Equal(InteractionErrorCode.INVALID_INPUT, foreignNode.Error?.Code);
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
        var method = Assert.IsAssignableFrom<IMethodNode>(navigator.CurrentEntry.Node);
        var started = method.Invoke(new Dictionary<string, object?>());
        var resultTask = method.ResultTask!;

        workspace.Dispose();

        Assert.Equal(InvocationStatus.RUNNING, started.Value);
        Assert.False(resultTask.IsCompleted);
        Assert.Empty(navigator.Entries);
        Assert.True(host.ResolveRootNode("model").IsSuccess);

        model.CompleteWork(42);
        var completed = await resultTask;
        Assert.Equal(42, completed.Value);
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
