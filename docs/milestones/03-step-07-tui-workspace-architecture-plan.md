# Step 7 Plan — Rebase the TUI on `UIEngineWorkspace`

## Goal

Replace the current host-only placeholder with a reusable TUI boundary backed by
`UIEngineWorkspace`. The completed step must establish ownership, startup, layout persistence,
navigator presentation lifetime, and UI-thread dispatching so Step 8 can add workspace chrome
without changing those foundations.

The node-refresh routing added in commit `6e87620` is treated as completed prerequisite work. This
step consumes the existing workspace notifications; it does not build the semantic node-control
catalogue planned for Step 9.

## Settled Design

### Ownership entry points

Expose creation and fullscreen APIs over a caller-owned host:

```csharp
TuiWorkspace TuiFrontend.CreateWorkspace(
    UIEngineHost host,
    TuiFrontendOptions? options = null);

Task TuiFrontend.RunAsync(
    UIEngineHost host,
    TuiFrontendOptions? options = null);
```

- The frontend creates a new `UIEngineWorkspace`; `TuiWorkspace` owns and disposes it.
- Neither entry point disposes the caller-owned host.
- `TuiWorkspace.Workspace` exposes the Core workspace used by the presentation.
- Fullscreen and embedded callers use the same `TuiWorkspace.Visual` tree.
- All entry points validate and copy `TuiFrontendOptions`; remove the current unused validation
  path.

### Startup configuration

Replace `TuiFrontendOptions.InitialPath` with one explicit startup value. Model the three valid
states with a closed startup configuration hierarchy rather than nullable, mutually exclusive
properties:

1. `PresentWorkspace` — leave the newly created workspace empty.
2. `AddNavigator(name, path)` — add one initial navigator.
3. `RestoreLayout(layout)` — replace the workspace contents from a TUI layout snapshot.

Defaults:

- A host-created workspace defaults to `PresentWorkspace`, producing a valid empty TUI.
- The cyclic-world example explicitly requests `AddNavigator("world", /world)`.
- `/` is never converted into an object node or an implicit navigator.

Startup validation rejects an empty navigator name or a null path/layout. Core remains responsible
for returning structured path resolution failures; a broken initial path is still a valid
navigator.

### Frontend layout contract

Add serializable frontend-owned contracts:

```csharp
public sealed record TuiLayoutSnapshot(
    WorkspaceSnapshot Workspace,
    IReadOnlyDictionary<string, NavigatorPresentationConfiguration> Navigators);

public sealed record NavigatorPresentationConfiguration(
    int Row,
    int Column,
    int Width,
    int Height);
```

- Navigator configuration is keyed by the stable, unique navigator name.
- `Row` and `Column` are valid from `0` through `ushort.MaxValue`.
- `Width` and `Height` are valid from `1` through `ushort.MaxValue`.
- Orphaned presentation records are ignored. Missing or invalid records receive deterministic
  defaults; none of these conditions prevents Core navigators from restoring.
- `Row` and `Column` are logical grid slots rather than terminal-cell coordinates. Default
  placement follows Core navigator order at row `0`, columns `0..n`; default preferred size is
  `40` columns by `12` rows.
- The snapshot contains no node, handle, control, draft, focus, live value, invocation, or toolkit
  geometry object.
- `TuiWorkspace.CreateLayoutSnapshot()` combines the current Core snapshot with each navigator's
  stable presentation configuration.
- `TuiWorkspace.RestoreLayout()` calls `UIEngineWorkspace.RestoreSnapshot`, applies configuration to
  every restored navigator by name, selects the Core restore result's selected navigator, and
  returns that `WorkspaceRestoreResult` to the caller.
- JSON/file I/O remains outside the TUI library; this step only defines serializable data and
  create/restore operations.

## Implementation Sequence

### 1. Close the Core notification invariant

Change `UIEngineWorkspace.Navigators` from a publicly mutable `List<Navigator>` to an
`IReadOnlyList<Navigator>` backed by a private list. All mutations must continue through workspace
methods so every change publishes the corresponding `WorkspaceChange` exactly once.

Add a Core regression test proving callers cannot mutate the collection and that add, duplicate,
reorder, restore, remove, and disposal still publish deterministic notifications.

### 2. Introduce the new TUI entry points and startup model

- Add the `CreateWorkspace` and `RunAsync` host entry points.
- Remove `InitialPath` and update the cyclic-world composition root to use `AddNavigator` startup.
- Apply startup synchronously before the initial visual tree is exposed.
- On construction failure, dispose the internally created workspace but never dispose the supplied
  host.
- Make `TuiWorkspace.Dispose()` idempotent and unsubscribe frontend handlers before disposing its
  Core workspace.

### 3. Add layout snapshot creation and restore

- Add the two frontend snapshot records and validation/defaulting logic described above.
- Track the selected navigator name in `TuiWorkspace`; use the first navigator when Core restore
  adjusts an invalid selection, and use `null` for an empty workspace.
- During restore, let Core removal/addition notifications drive presentation retirement and
  recreation. Apply saved geometry after the Core restore completes so no restored presentation
  is created twice.
- Preserve unknown JSON fields through normal `System.Text.Json` tolerance; do not add a schema
  version gate.

### 4. Create atomic navigator presentations

Maintain a private `Dictionary<string, NavigatorPresentation>` plus an ordered presentation list.
Each presentation owns:

- the current Core navigator identity;
- its placement, size, selection, and visibility state;
- one stable navigator container visual; and
- exactly one current placeholder node control.

The placeholder control distinguishes a healthy node, an empty entry, and a structured broken
entry. It is intentionally minimal: Step 8 supplies headers and commands, and Step 9 replaces it
with semantic node controls.

Initial construction creates one presentation for every existing navigator. Duplication creates a
separate presentation and placeholder even when both navigators resolve the same path.

### 5. Bind workspace changes to presentation lifetime

Subscribe once to `UIEngineWorkspace.Changed` and handle every concrete change:

- `NavigatorAddedChange` and `NavigatorDuplicatedChange`: insert one new presentation at the
  reported index.
- `NavigationPushedChange`: dispose the previous placeholder and install one for the pushed entry.
- `NavigationPoppedChange`: dispose the departed placeholder and install one for the revealed
  entry.
- `NavigatorReorderedChange`: move the existing presentation without recreating its container or
  current placeholder.
- `NavigatorRemovedChange`: remove and dispose the presentation and all descendants exactly once.

Presentation lookup is by navigator name for persisted configuration and by navigator ID while
processing runtime notifications. Assert internally that the two indexes remain consistent.
Unknown navigator IDs or duplicate add notifications are programming errors, not recoverable UI
states.

### 6. Enforce the UI-thread boundary

- Capture XenoAtom's `Dispatcher.Current` when `TuiWorkspace` is created.
- If a workspace notification arrives with dispatcher access, apply the visual mutation
  immediately; otherwise post it with `Dispatcher.Post`.
- Do not move Core resolution, navigation, reads, or writes onto the UI dispatcher. Those calls
  remain synchronous on the caller/domain-owning thread.
- Every posted callback checks the workspace and target presentation lifetime before touching a
  visual. Disposal invalidates queued callbacks.
- Keep the concrete XenoAtom dispatcher boundary; do not introduce a parallel public dispatcher or
  queue abstraction.

### 7. Update documentation and milestone state

- Update `PROJECT_CONTEXT.md` so the delivery state says the TUI is workspace-backed and Step 8 is
  next.
- Mark every Step 7 implementation and verification item complete in
  `03-product-mvp-tui.md` only after the tests below pass.
- Keep Step 8 chrome, keyboard commands, responsive layout switching, and dialogs out of this
  change.

## Test Plan

### Core regression

- `Navigators` is read-only to callers.
- Every supported workspace mutation still produces one notification with the correct identity and
  index.

### Ownership and startup

- Host entry point creates and owns a Core workspace; TUI disposal removes its entries but leaves
  the host usable.
- Empty startup creates zero navigators and does not resolve `/`.
- Initial-navigator startup creates the requested healthy or broken navigator.
- Restored startup recreates all valid and broken navigators in order.
- Invalid options fail before a terminal application starts, and the caller's objects remain
  usable.

### Presentation lifetime

- Existing, added, and duplicated navigators each receive one presentation and one distinct
  placeholder control.
- Push and pop each dispose one old placeholder and create one replacement.
- Reorder retains the same presentation and placeholder instances.
- Remove and workspace disposal retire each affected presentation once.
- Removal of one navigator does not affect another presentation.
- A queued visual update becomes a no-op after its workspace or presentation is disposed.

### Layout persistence

- Multi-navigator name, order, path, selected name, placement, and size round-trip through
  `System.Text.Json`.
- Missing, invalid, and orphaned presentation configuration receives deterministic defaults without
  dropping restored Core navigators.
- Broken paths restore with their presentation configuration and retain back/remove recovery in
  Core.
- Serialized layout text contains no domain values, runtime handles, nodes, controls, drafts,
  invocation state, or running operations.

### Dispatcher

- A workspace change raised on the UI thread mutates the presentation immediately.
- A change raised off the UI thread posts the visual mutation to XenoAtom's dispatcher.
- Core navigation remains on the invoking thread.
- No posted mutation reaches a retired control.

## Completion Gate

Step 7 is complete, and Step 8 may begin, only when:

1. Frontend workspace ownership, all three startup modes, layout round-trip, notification-driven control
   replacement, and dispatcher behavior are implemented and covered by tests.
2. The example uses the new startup contract and contains composition only.
3. The old `InitialPath` API and caller-supplied workspace overloads are removed.
4. These commands pass with zero build warnings:

```sh
dotnet test Tests/UIEngine.Framework.Tests/UIEngine.Framework.Tests.csproj
dotnet test Tests/UIEngine.Frontend.Tui.Tests/UIEngine.Frontend.Tui.Tests.csproj
dotnet build UIEngine.sln
dotnet test UIEngine.sln --no-build
```

## Explicitly Deferred

- Workspace headers, root picker, commands, keyboard map, focus flow, responsive normal/narrow
  layouts, and save/load dialogs belong to Step 8.
- The semantic node-to-control factory and scalar editors belong to Step 9.
- Object, collection, method, and specialized domain controls remain in their later milestone
  steps.
- File selection and physical layout persistence remain application concerns.
