# Step 7 Plan — Workspace-Backed, Node-Driven TUI Architecture

## Goal

Rebase the TUI on `UIEngineWorkspace` while preserving UIEngine's central advantage: the frontend
defines controls for interesting node occurrences, and UIEngine supplies the lifecycle and
invalidation signals needed to create, replace, hide, and update them.

The workspace arranges navigators as atomic units. It remains agnostic about the controls and
nested composition inside each navigator.

## Architectural Model

```text
TuiWorkspace
└── NavigatorPresentation[name]
    └── NavigatorControl
        └── NodeControl ↔ BaseNode occurrence
            └── optional nested node controls
```

Responsibilities are divided as follows:

| Component | Responsibility |
|---|---|
| `UIEngineHost` | Live domain access and routing domain changes to affected node occurrences |
| `UIEngineWorkspace` | Navigator identity, ordering, navigation stacks, snapshots, and lifecycle notifications |
| `NavigatorPresentation` | Placement, size, visibility, and ownership of one atomic navigator control |
| `NavigatorControl` | Header and current-node presentation; replaces its current node control after navigation |
| `NodeControl` | Presents one node occurrence, owns nested controls and local state, and reacts to node invalidation |
| `TuiWorkspace` | Arranges atomic navigator presentations without inspecting their internal control trees |

## Core Principles

### One node occurrence, one control occurrence

Every presented `BaseNode` occurrence has one corresponding control instance. Two navigators at the
same path receive distinct nodes and distinct controls, even though their operations affect the same
domain object.

Multiple nodes may use the same control implementation. A simple application might map every
numeric node to one generic number-box implementation. A specialized application can select
different implementations based on semantic facets, runtime type, metadata, or caller-provided
rules.

### Atomic navigator presentation

`TuiWorkspace` does not reconcile the descendants of a navigator control. It only:

- creates and removes navigator presentations;
- orders, selects, places, shows, and hides them; and
- forwards navigation changes to the affected navigator control.

Nested controls are entirely owned by their parent control. If a control recursively presents other
nodes, it must do so lazily or within explicit bounds so cyclic graphs cannot cause unbounded
materialization.

### Notifications are clues, not copied state

Domain notifications should invalidate affected nodes rather than carry persistent copies of domain
values.

The update flow is:

```text
domain notification
→ UIEngine identifies affected node occurrences
→ node publishes a frontend-neutral invalidation
→ bound control re-reads authoritative state through the node
→ control updates its visual state
```

Core never references a XenoAtom control. A node "asks its control to update" through an event or
detachable subscription understood by any frontend.

### Hierarchical lifetime ownership

Ownership follows the control hierarchy:

- `TuiWorkspace` owns navigator presentations.
- A navigator presentation owns its navigator control.
- A navigator control owns its current node control.
- A node control owns its nested controls, drafts, subscriptions, and asynchronous observation.
- Removing a parent disposes everything below it exactly once.

Already-started domain tasks remain independent and are not cancelled by control disposal.

### Explicit thread boundary

Domain reads and mutations stay synchronous on the domain model's owning thread. Visual mutations
run on the XenoAtom dispatcher.

When a domain event arrives on the domain thread, the binding may read authoritative state there and
then post the resulting presentation update to the UI dispatcher. If reads must originate elsewhere,
the embedding application supplies the domain-thread request boundary; Core does not provide thread
marshalling.

Detached controls must reject any delayed or posted update.

## Implementation Steps

### 1. Add the missing node-invalidation foundation

The current Core API exposes workspace navigation notifications but no node-level invalidation
mechanism. Introduce a frontend-neutral contract before building the control catalogue.

- Define a small invalidation event or detachable subscription on node occurrences.
- Carry identity and change category, not copied domain values or controls.
- Route `INotifyPropertyChanged` and `INotifyCollectionChanged` where applicable.
- Allow programmatic exposures to supply equivalent invalidation sources.
- Route changes by runtime handle and, where available, member identity.
- Notify every live occurrence representing the affected location.
- Use weak routing or explicit unsubscription so removed controls and nodes are collectible.
- Test multiple occurrences, broad property invalidation, collection changes, and detachment.

### 2. Define TUI ownership entry points

Provide symmetric creation and fullscreen APIs:

```csharp
TuiFrontend.CreateWorkspace(UIEngineHost host, ...)
TuiFrontend.CreateWorkspace(UIEngineWorkspace workspace, ...)

TuiFrontend.RunAsync(UIEngineHost host, ...)
TuiFrontend.RunAsync(UIEngineWorkspace workspace, ...)
```

- The host overload creates a Core workspace owned by `TuiWorkspace`.
- The workspace overload presents a caller-owned workspace.
- Neither path owns the caller's host.
- Disposing the TUI disposes only an internally created Core workspace.

### 3. Replace `InitialPath` with startup configuration

Support three explicit startup modes:

- present the workspace's current contents;
- add one named initial navigator at a supplied path; or
- restore a supplied TUI layout snapshot.

A new workspace with no startup navigator remains empty. `/` must never be treated as an object
node.

For a supplied workspace, the default mode presents it unchanged. An explicitly supplied layout may
replace its navigators through `RestoreSnapshot`.

### 4. Define frontend-owned layout persistence

Create a TUI layout snapshot combining:

- the Core `WorkspaceSnapshot`; and
- bounded navigator placement and size configuration.

Presentation configuration is keyed by navigator name and uses serializable primitives rather than
XenoAtom geometry types. It must not contain controls, nodes, values, drafts, focus, invocation
state, or running operations.

Unknown configurations are ignored, missing configurations receive deterministic defaults, and
invalid geometry is rejected or normalized predictably.

### 5. Introduce atomic navigator presentations

Maintain one internal presentation record per navigator name. Each record contains:

- the Core navigator identity;
- placement, size, selection, and visibility state; and
- one `NavigatorControl`.

The workspace container treats this control as opaque. Responsive layout may hide or reposition the
atomic unit but must not rebuild its internal node controls.

### 6. Bind navigation lifecycle to `NavigatorControl`

Handle Core workspace notifications as follows:

- add or duplicate: create one navigator presentation;
- navigate forward: tell its navigator control to retire the old node control and create one
  replacement;
- go back: retire the departed node control and create one for the revealed entry;
- remove: dispose the navigator presentation and its descendants once; and
- reorder: move the existing atomic presentation without recreating it.

Broken entries receive their own atomic node presentation and retain back/remove recovery.

### 7. Establish the node-control resolver seam

Define a resolver that chooses a control implementation from node semantics.

- Each resolution creates a fresh control instance.
- Default rules can group compatible nodes under common implementations.
- More-specific rules can distinguish runtime types or metadata.
- Caller-provided rules can precede defaults without adding domain dependencies to the TUI.
- Parent controls use the same resolver when creating nested controls.

Step 7 can initially use a placeholder implementation; the full semantic catalogue belongs to
later milestone steps.

### 8. Implement node-driven updates

Each node control subscribes to its node occurrence and owns its refresh logic.

- Re-read authoritative state after invalidation.
- Preserve dirty drafts instead of overwriting them.
- Coalesce redundant invalidations where useful.
- Marshal only visual mutations to the XenoAtom dispatcher.
- Unsubscribe during disposal.
- Guard queued updates with the control's active lifetime.

The workspace must not participate in these updates.

### 9. Verify the boundaries

Add focused tests proving:

- both Core-workspace ownership paths behave correctly;
- caller-owned hosts and workspaces survive TUI disposal;
- empty, initial-navigator, current-workspace, and restored startup work;
- each navigator creates one atomic presentation;
- each presented node occurrence creates one control occurrence;
- navigation replaces exactly one current node control;
- the workspace never traverses navigator-control descendants;
- duplicated navigators have independent node and control instances;
- domain invalidation reaches all applicable node occurrences;
- detached controls receive no later visual updates;
- visual changes occur on the XenoAtom dispatcher; and
- layout snapshots round-trip only stable workspace and geometry data.

Finally run:

```sh
dotnet test Tests/UIEngine.Frontend.Tui.Tests/UIEngine.Frontend.Tui.Tests.csproj
dotnet build UIEngine.sln
dotnet test UIEngine.sln --no-build
```

This establishes Step 7 as the ownership, lifecycle, notification, and composition foundation.
Subsequent steps can add workspace chrome and richer controls without turning `TuiWorkspace` into a
centralized visual-tree manager.
