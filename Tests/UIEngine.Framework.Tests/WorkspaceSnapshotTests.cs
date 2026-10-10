using System.Text.Json;
using UIEngine.Core;
using UIEngine.Core.Attributes;
using Xunit;

namespace UIEngine.Framework.Tests;

public sealed class WorkspaceSnapshotTests
{
    [Fact]
    public void SnapshotRoundTripsOrderedStructuredPathsAndSelectionThroughJson()
    {
        using var host = _CreateHost(new _SnapshotModel());
        using var workspace = new UIEngineWorkspace(host);
        workspace.AddNavigator(_Path(_Member("model"), _Member("Child"), _Member("Name")));
        workspace.AddNavigator(_Path(
            _Member("model"),
            _Member("Items"),
            new ListLogicalPathSegment(0),
            _Member("Name")));
        workspace.AddNavigator(_Path(
            _Member("model"),
            _Member("ByKey"),
            new DictLogicalPathSegment("key/one"),
            _Member("Name")));
        var selectedId = workspace.Navigators[2].Id;
        workspace.ReorderNavigator(workspace.Navigators[0].Id, 2);

        var created = workspace.CreateSnapshot(selectedId);
        var json = JsonSerializer.Serialize(created.RightValue());
        var deserialized = JsonSerializer.Deserialize<WorkspaceSnapshot>(json)!;
        var restored = workspace.RestoreSnapshot(deserialized);

        Assert.Equal(selectedId, restored.SelectedNavigatorId);
        Assert.Empty(restored.Issues);
        Assert.Equal(
            created.RightValue().Navigators.Select(static navigator => navigator.Id),
            workspace.Navigators.Select(static navigator => navigator.Id));
        Assert.Equal(
            [
                "/model/Items[index=0]/Name",
                "/model/ByKey[key=key/one]/Name",
                "/model/Child/Name",
            ],
            workspace.Navigators.Select(static navigator => navigator.CurrentPath.ToString()));
        Assert.DoesNotContain("Version", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Presentation", json, StringComparison.OrdinalIgnoreCase);
        Assert.Null(typeof(WorkspaceSnapshot).GetProperty("Version"));
    }

    [Fact]
    public void RestoreKeepsBrokenDescendantsUntilBackReachesValidCollection()
    {
        var model = new _SnapshotModel();
        using var host = _CreateHost(model);
        using var workspace = new UIEngineWorkspace(host);
        workspace.AddNavigator(_Path(
            _Member("model"),
            _Member("Items"),
            new ListLogicalPathSegment(0),
            _Member("Name")));
        var selectedId = Assert.Single(workspace.Navigators).Id;
        var snapshot = workspace.CreateSnapshot(selectedId).RightValue();
        model.Items.Clear();

        var restored = workspace.RestoreSnapshot(snapshot);

        Assert.Equal(selectedId, restored.SelectedNavigatorId);
        var navigator = Assert.Single(workspace.Navigators);
        Assert.Equal(4, navigator.Entries.Count);
        Assert.True(navigator.Entries[0].IsRight);
        Assert.True(navigator.Entries[1].IsRight);
        Assert.False(navigator.Entries[2].IsRight);
        Assert.False(navigator.Entries[3].IsRight);

        Assert.True(navigator.GoBack().IsRight);
        Assert.False(navigator.CurrentEntry.IsRight);
        Assert.True(navigator.GoBack().IsRight);
        Assert.True(navigator.CurrentEntry.IsRight);
        Assert.Equal("/model/Items", navigator.CurrentPath.ToString());
    }

    [Fact]
    public void RestoreSalvagesRecordsAndUsesFirstOccurrenceOfEachId()
    {
        using var host = _CreateHost(new _SnapshotModel());
        using var workspace = new UIEngineWorkspace(host);
        var firstId = Guid.NewGuid();
        var brokenId = Guid.NewGuid();
        var snapshot = new WorkspaceSnapshot(
            [
                new NavigatorSnapshot(firstId, [_SavedMember("model")]),
                new NavigatorSnapshot(Guid.Empty, [_SavedMember("model")]),
                new NavigatorSnapshot(firstId, [
                    _SavedMember("model"),
                    _SavedMember("Child"),
                ]),
                new NavigatorSnapshot(Guid.NewGuid(), [
                    new _UnknownPathSegmentSnapshot(),
                ]),
                new NavigatorSnapshot(brokenId, [
                    _SavedMember("model"),
                    _SavedMember("Missing"),
                    _SavedMember("Descendant"),
                ]),
            ],
            Guid.NewGuid());

        var restored = workspace.RestoreSnapshot(snapshot);

        Assert.Equal([firstId, brokenId], workspace.Navigators.Select(static item => item.Id));
        Assert.Equal(firstId, restored.SelectedNavigatorId);
        Assert.False(workspace.Navigators[1].Entries[1].IsRight);
        Assert.False(workspace.Navigators[1].Entries[2].IsRight);
        Assert.Equal(
            [
                SnapshotRestoreIssueCode.INVALID_NAVIGATOR_RECORD,
                SnapshotRestoreIssueCode.DUPLICATE_NAVIGATOR_ID,
                SnapshotRestoreIssueCode.INVALID_NAVIGATOR_PATH,
                SnapshotRestoreIssueCode.SELECTION_ADJUSTED,
            ],
            restored.Issues.Select(static issue => issue.Code));
        Assert.Equal([1, 2, 3, null], restored.Issues.Select(static issue => issue.NavigatorIndex));
    }

    [Fact]
    public void RestoreSkipsEveryMalformedConcretePathShapeIndependently()
    {
        using var host = _CreateHost(new _SnapshotModel());
        using var workspace = new UIEngineWorkspace(host);
        var validId = Guid.NewGuid();
        var snapshot = new WorkspaceSnapshot(
            [
                new NavigatorSnapshot(Guid.NewGuid(), []),
                new NavigatorSnapshot(Guid.NewGuid(), [new ListPathSegmentSnapshot(0)]),
                new NavigatorSnapshot(Guid.NewGuid(), [new MemberPathSegmentSnapshot("")]),
                new NavigatorSnapshot(Guid.NewGuid(), [
                    _SavedMember("model"),
                    new ListPathSegmentSnapshot(-1),
                ]),
                new NavigatorSnapshot(Guid.NewGuid(), [
                    _SavedMember("model"),
                    new DictionaryPathSegmentSnapshot(""),
                ]),
                new NavigatorSnapshot(validId, [_SavedMember("model")]),
            ],
            validId);

        var restored = workspace.RestoreSnapshot(snapshot);

        Assert.Equal(validId, restored.SelectedNavigatorId);
        Assert.Equal(validId, Assert.Single(workspace.Navigators).Id);
        Assert.Equal(5, restored.Issues.Count);
        Assert.All(restored.Issues, static issue =>
            Assert.Equal(SnapshotRestoreIssueCode.INVALID_NAVIGATOR_PATH, issue.Code));
        Assert.Equal([0, 1, 2, 3, 4],
            restored.Issues.Select(static issue => issue.NavigatorIndex));
    }

    [Fact]
    public void FirstMalformedDuplicateStillClaimsItsId()
    {
        using var host = _CreateHost(new _SnapshotModel());
        using var workspace = new UIEngineWorkspace(host);
        var claimedId = Guid.NewGuid();
        var fallbackId = Guid.NewGuid();
        var snapshot = new WorkspaceSnapshot(
            [
                new NavigatorSnapshot(claimedId, [new _UnknownPathSegmentSnapshot()]),
                new NavigatorSnapshot(claimedId, [_SavedMember("model")]),
                new NavigatorSnapshot(fallbackId, [_SavedMember("model")]),
            ],
            claimedId);

        var restored = workspace.RestoreSnapshot(snapshot);

        Assert.Equal(fallbackId, Assert.Single(workspace.Navigators).Id);
        Assert.Equal(fallbackId, restored.SelectedNavigatorId);
        Assert.Equal(
            [
                SnapshotRestoreIssueCode.INVALID_NAVIGATOR_PATH,
                SnapshotRestoreIssueCode.DUPLICATE_NAVIGATOR_ID,
                SnapshotRestoreIssueCode.SELECTION_ADJUSTED,
            ],
            restored.Issues.Select(static issue => issue.Code));
    }

    [Fact]
    public void MissingNavigatorCollectionOptimisticallyReplacesWorkspaceWithEmptySnapshot()
    {
        using var host = _CreateHost(new _SnapshotModel());
        using var workspace = new UIEngineWorkspace(host);
        workspace.AddNavigator("model");
        var existingId = workspace.Navigators[0].Id;
        var changes = new List<WorkspaceChange>();
        workspace.Changed += (_, eventArgs) => changes.Add(eventArgs.Change);
        var malformed = new WorkspaceSnapshot(null!, existingId);

        var restored = workspace.RestoreSnapshot(malformed);

        Assert.Empty(workspace.Navigators);
        Assert.Null(restored.SelectedNavigatorId);
        Assert.Equal(
            [
                SnapshotRestoreIssueCode.INVALID_NAVIGATOR_COLLECTION,
                SnapshotRestoreIssueCode.SELECTION_ADJUSTED,
            ],
            restored.Issues.Select(static issue => issue.Code));
        var removed = Assert.IsType<NavigatorRemovedChange>(Assert.Single(changes));
        Assert.Equal(existingId, removed.NavigatorId);
    }

    [Fact]
    public void RestorePublishesReverseRemovalsThenOrderedAdditions()
    {
        using var host = _CreateHost(new _SnapshotModel());
        using var workspace = new UIEngineWorkspace(host);
        workspace.AddNavigator("model");
        workspace.AddNavigator("model");
        var oldIds = workspace.Navigators.Select(static navigator => navigator.Id).ToArray();
        var changes = new List<WorkspaceChange>();
        workspace.Changed += (_, eventArgs) => changes.Add(eventArgs.Change);
        var newOneId = Guid.NewGuid();
        var newTwoId = Guid.NewGuid();
        var snapshot = new WorkspaceSnapshot(
            [
                new NavigatorSnapshot(newOneId, [_SavedMember("model")]),
                new NavigatorSnapshot(newTwoId, [
                    _SavedMember("model"),
                    _SavedMember("Child"),
                ]),
            ],
            newTwoId);

        var restored = workspace.RestoreSnapshot(snapshot);

        Assert.Empty(restored.Issues);
        Assert.Equal(4, changes.Count);
        Assert.Equal(
            [oldIds[1], oldIds[0]],
            changes.OfType<NavigatorRemovedChange>().Select(static change => change.NavigatorId));
        Assert.Equal(
            [newOneId, newTwoId],
            workspace.Navigators.Select(static navigator => navigator.Id));
        Assert.Equal(
            workspace.Navigators.Select(static navigator => navigator.Id),
            changes.OfType<NavigatorAddedChange>().Select(static change => change.NavigatorId));
    }

    [Fact]
    public void EmptySnapshotAndInvalidSelectionUseDeterministicSelection()
    {
        using var host = _CreateHost(new _SnapshotModel());
        using var workspace = new UIEngineWorkspace(host);
        workspace.AddNavigator("model");

        var empty = workspace.RestoreSnapshot(new WorkspaceSnapshot([], null));
        var invalidEmptySelection = workspace.CreateSnapshot(Guid.NewGuid());
        var emptySnapshot = workspace.CreateSnapshot(null);

        Assert.Empty(workspace.Navigators);
        Assert.Null(empty.SelectedNavigatorId);
        Assert.Empty(empty.Issues);
        Assert.False(invalidEmptySelection.IsRight);
        Assert.Equal(InteractionErrorCode.INVALID_INPUT, invalidEmptySelection.LeftValue().Code);
        Assert.True(emptySnapshot.IsRight);
        Assert.Empty(emptySnapshot.RightValue().Navigators);
        Assert.Null(emptySnapshot.RightValue().SelectedNavigatorId);
    }

    [Fact]
    public void JsonWithUnknownFieldsRestoresWithoutSchemaGate()
    {
        var id = Guid.NewGuid();
        var json = $$"""
            {
              "Navigators": [
                {
                  "Id": "{{id}}",
                  "CurrentPath": [
                    { "segment": "member", "Name": "model", "FutureSegmentData": true }
                  ],
                  "FutureNavigatorData": "ignored"
                }
              ],
              "SelectedNavigatorId": "{{id}}",
              "FutureWorkspaceData": { "revision": 12 }
            }
            """;
        using var host = _CreateHost(new _SnapshotModel());
        using var workspace = new UIEngineWorkspace(host);
        var snapshot = JsonSerializer.Deserialize<WorkspaceSnapshot>(json)!;

        var restored = workspace.RestoreSnapshot(snapshot);

        Assert.Empty(restored.Issues);
        Assert.Equal(id, restored.SelectedNavigatorId);
        Assert.Equal("/model", Assert.Single(workspace.Navigators).CurrentPath.ToString());
    }

    [Fact]
    public async Task SnapshotExcludesLiveValuesHandlesNodesAndInvocationState()
    {
        var model = new _SnapshotModel { Secret = "domain-secret-9217" };
        using var host = _CreateHost(model);
        using var workspace = new UIEngineWorkspace(host);
        workspace.AddNavigator(_Path(_Member("model"), _Member("WorkAsync")));
        var method = Assert.IsAssignableFrom<IMethodNode>(
            workspace.Navigators[0].CurrentEntry.RightValue().SomeValue());
        method.Invoke(new Dictionary<string, object?>());

        var snapshot = workspace.CreateSnapshot(workspace.Navigators[0].Id).RightValue();
        var json = JsonSerializer.Serialize(snapshot);

        Assert.DoesNotContain(model.Secret, json, StringComparison.Ordinal);
        Assert.DoesNotContain("Handle", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Node", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Draft", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Invocation", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ResultTask", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Presentation", json, StringComparison.OrdinalIgnoreCase);

        model.CompleteWork(7);
        Assert.Equal(7, (await method.ResultTask!).RightValue().SomeValue());
    }

    [Fact]
    public void SamePathNavigatorsHaveDistinctPersistentIds()
    {
        using var host = _CreateHost(new _SnapshotModel());
        using var workspace = new UIEngineWorkspace(host);

        var first = workspace.AddNavigator("model");
        var second = workspace.AddNavigator("model");

        Assert.True(first.IsRight);
        Assert.True(second.IsRight);
        Assert.NotEqual(first.RightValue().NavigatorId, second.RightValue().NavigatorId);
    }

    [Fact]
    public void SnapshotRejectsAnUnsupportedPathSegmentAsAStructuredError()
    {
        using var host = _CreateHost(new _SnapshotModel());
        using var workspace = new UIEngineWorkspace(host);
        var path = _Path(_Member("model"), new _UnknownPathSegment());
        workspace.AddNavigator(path);

        var snapshot = workspace.CreateSnapshot(workspace.Navigators[0].Id);

        Assert.False(snapshot.IsRight);
        Assert.Equal(InteractionErrorCode.UNSUPPORTED, snapshot.LeftValue().Code);
    }

    private static UIEngineHost _CreateHost(_SnapshotModel model)
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

    private static MemberPathSegmentSnapshot _SavedMember(string name) => new(name);

    private sealed class _SnapshotModel
    {
        private readonly TaskCompletionSource<int> _Work =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        [Expose]
        public string Secret { get; set; } = "secret";

        [Children]
        public _SnapshotItem Child { get; } = new("child");

        [Children]
        public List<_SnapshotItem> Items { get; } = [new("list item")];

        [Children]
        public Dictionary<string, _SnapshotItem> ByKey { get; } = new()
        {
            ["key/one"] = new("dictionary item"),
        };

        [Action]
        public async Task<int> WorkAsync() => await _Work.Task;

        public void CompleteWork(int value) => _Work.SetResult(value);
    }

    private sealed class _SnapshotItem(string name)
    {
        [Expose]
        public string Name { get; set; } = name;
    }

    private sealed class _UnknownPathSegment : ILogicalPathSegment;

    private sealed class _UnknownPathSegmentSnapshot : IPathSegmentSnapshot;
}
