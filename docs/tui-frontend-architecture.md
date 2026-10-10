# TUI Frontend Architecture

## Purpose

`UIEngine.Frontend.Tui` presents a `UIEngineWorkspace` with XenoAtom.Terminal.UI. It maps
frontend-neutral node semantics to controls and contains no domain-specific screens.

The frontend is a reusable class library. Applications may run it fullscreen or embed its root
visual in an existing terminal application.

## Workspace Presentation

The TUI presents several independent navigators. Each navigator has:

- a header containing only the current node name or the textual `Invalid`/`Empty` state;
- one control for its current node;
- concise loading, result, and failure state; and
- independent presentation state.

Shared workspace chrome contains root creation, Up, duplication, close/suspend, restore-closed, and
a read-only canonical path and textual state for the selected navigator. Healthy, invalid, and empty
states use green, red, and yellow respectively; selection uses a separate cyan pane border. State is
never communicated by color alone.

Users can choose a registered root, add a navigator, duplicate the current navigator, navigate
deeper, go back, temporarily hide a pane, and restore the last hidden pane. Duplication resolves a
new navigator GUID, node stack, and control at the same path.

Every active pane remains in a logical grid inside a two-axis scroll viewer. Terminal width changes
only the viewport and scroll offsets; it never substitutes a selected-only layout. Resize preserves
navigator identity, paths, controls, drafts, selection, placement, focus memory, and the transient
cache. Step 8 intentionally exposes no navigator-reordering UI.

## Node-to-Control Mapping

Controls are selected from language semantics exposed by the current `BaseNode`:

| Node semantics | TUI presentation |
|---|---|
| Object or reference | Summary and navigable members |
| Read-only scalar | Value display |
| Writable string or character | Text editor |
| Writable boolean | Boolean editor |
| Writable number | Numeric editor with range metadata |
| Enum | Enum editor using declared members |
| Collection | Bounded window with entry navigation |
| Method | Generated parameter form, result, and completion state |
| Invalid navigator entry | No node content; shared status and back/remove commands remain |

An enum property remains an enum property; Core does not describe it as a choice control. Toolkit
selection and styling stay in the TUI.

## Operations

Controls call node operations directly. Core remains responsible for conversion, validation,
bounded reads, and invocation.

- Read a value before presenting or editing it.
- Keep uncommitted drafts frontend-local.
- Submit writes through the node and re-read authoritative state after success.
- Request collections only in configured bounded windows.
- Generate method fields from parameter semantics and preserve omitted, default, and explicit-null
  states.
- Present structured failures without parsing messages.
- Re-read authoritative state when a control refreshes.

## Lifetime

Going back removes the departed control. Permanent Core removal removes all controls belonging to
the navigator. TUI Close hides the exact live navigator presentation in a one-item cache without
changing Core; the navigator remains an ordinary Core navigator and may continue receiving valid
updates. Closing another pane permanently removes and disposes the previously cached navigator.
Restore Closed returns the same navigator, node occurrences, presentation, control, placement, and
focus memory. Closing the TUI leaves the caller-owned host and domain objects alive.

A method control observes invocation state on its method-node occurrence. Removing the control
detaches its completion continuation and releases that node; the already-started domain task
continues independently.

## Layout Persistence

The TUI owns a layout snapshot that combines a GUID-based Core workspace snapshot with GUID-keyed
placement and size configuration. Core knows nothing about those presentation fields or whether a
pane is temporarily hidden. The TUI filters its hidden navigator from its layout snapshot and does
not persist the cache, drafts, focus, loaded values, controls, or running invocations.

Loading a layout creates all saved navigators. A path that no longer resolves is invalid, displays
no node content, and can navigate back or be removed.

## Toolkit and Ownership

- XenoAtom.Terminal.UI remains pinned to an accepted version.
- `TuiWorkspace` owns its core workspace, visual tree, and TUI presentation state.
- The TUI uses the active `UIEngineHost` singleton and does not retain an explicit host reference.
- `TuiWorkspace` and its owned Core workspace are UI-thread-confined. TUI commands call Core
  synchronously on that thread. The TUI contains no cross-thread dispatch machinery.
- Keyboard operation is complete without requiring mouse input.
- Core has no XenoAtom dependency.

## Verification

Tests cover:

- root selection and navigator creation;
- duplication with independent node and control instances;
- destructive back navigation and navigator removal;
- terminal nodes;
- invalid restored paths and recovery by going back;
- scalar editing, validation, and dirty drafts;
- bounded collection windows;
- method invocation and continued execution after control disposal;
- refresh from authoritative live values;
- independent navigator lifetimes;
- layout round trips;
- keyboard-only workflows and resize; and
- caller-owned host lifetime.
