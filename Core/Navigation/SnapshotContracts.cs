using System.Text.Json.Serialization;

namespace UIEngine.Core;

/// <summary>One serializable snapshot containing only stable workspace navigation state.</summary>
public sealed record WorkspaceSnapshot(
    IReadOnlyList<NavigatorSnapshot> Navigators,
    string? SelectedNavigatorName);

/// <summary>One navigator's stable name and current semantic path.</summary>
public sealed record NavigatorSnapshot(
    string Name,
    IReadOnlyList<IPathSegmentSnapshot> CurrentPath);

/// <summary>One serializable semantic step in a logical path.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "segment")]
[JsonDerivedType(typeof(MemberPathSegmentSnapshot), "member")]
[JsonDerivedType(typeof(ListPathSegmentSnapshot), "list")]
[JsonDerivedType(typeof(DictionaryPathSegmentSnapshot), "dictionary")]
public interface IPathSegmentSnapshot;

/// <summary>Selects a named root or exposed member.</summary>
public sealed record MemberPathSegmentSnapshot(string Name) : IPathSegmentSnapshot;

/// <summary>Selects one element from the list at the parent path.</summary>
public sealed record ListPathSegmentSnapshot(long Index) : IPathSegmentSnapshot;

/// <summary>Selects one value from the dictionary at the parent path.</summary>
public sealed record DictionaryPathSegmentSnapshot(string Key) : IPathSegmentSnapshot;

/// <summary>Stable categories for non-fatal problems found during optimistic snapshot restore.</summary>
public enum SnapshotRestoreIssueCode
{
    INVALID_NAVIGATOR_COLLECTION,
    INVALID_NAVIGATOR_RECORD,
    DUPLICATE_NAVIGATOR_NAME,
    INVALID_NAVIGATOR_PATH,
    SELECTION_ADJUSTED,
}

/// <summary>Describes one record that was skipped or one selection that was adjusted.</summary>
public sealed record SnapshotRestoreIssue(
    SnapshotRestoreIssueCode Code,
    int? NavigatorIndex,
    string? NavigatorName,
    InteractionError Error);

/// <summary>The usable state and non-fatal issues produced by optimistic snapshot restore.</summary>
public sealed record WorkspaceRestoreResult(
    string? SelectedNavigatorName,
    IReadOnlyList<SnapshotRestoreIssue> Issues);
