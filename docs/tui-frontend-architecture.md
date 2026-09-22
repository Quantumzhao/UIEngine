# TUI Frontend Architecture

## Purpose

`UIEngine.Frontend.Tui` presents a `UIEngineWorkspace` with XenoAtom.Terminal.UI. It maps
frontend-neutral node semantics to controls and contains no domain-specific screens.

The frontend is a reusable class library. Applications may run it fullscreen or embed its root
visual in an existing terminal application.

## Workspace Presentation

The TUI presents several independent navigators. Each navigator has:

- a header with its current path and navigation commands;
- one control for its current node;
- concise loading, result, and failure state; and
- independent presentation state.

Users can choose a registered root, add a navigator, duplicate the current navigator, remove a
navigator, navigate deeper, and go back. Duplication resolves a new node instance at the same path;
it does not share control state.

Normal terminal widths may place navigators side by side. Narrow widths may show one selected
navigator at a time. Resize must preserve navigator identity, paths, drafts, and selection.

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
| Broken navigator entry | Structured failure and back/remove commands |

An enum property remains an enum property; Core does not describe it as a choice control. Toolkit
selection and styling stay in the TUI.

## Operations

Controls call node operations directly. Core remains responsible for conversion, validation,
dispatch, bounded reads, observation, and invocation.

- Read a value before presenting or editing it.
- Keep uncommitted drafts frontend-local.
- Submit writes through the node and re-read authoritative state after success.
- Request collections only in configured bounded windows.
- Generate method fields from parameter semantics and preserve omitted, default, and explicit-null
  states.
- Present structured failures without parsing messages.
- Refresh from property changes surfaced by the current node.

## Lifetime

Property-observation subscriptions belong to nodes and follow their lifetime. 

Going back removes the departed control. Removing a navigator removes all controls belonging to
it. Closing the TUI leaves the caller-owned host and domain objects alive.

A started `ActionInvocation` continues under host ownership after its method control is removed.

## Layout Persistence

The TUI contributes stable placement, size, and selection configuration to the workspace layout
snapshot. It does not persist drafts, focus, loaded values, controls, subscriptions, or running
invocations.

Loading a layout creates all saved navigators. A path that no longer resolves is shown as a broken
entry and can navigate back or be removed.

## Toolkit and Ownership

- XenoAtom.Terminal.UI remains pinned to an accepted version.
- `TuiWorkspace` owns its visual tree and TUI presentation state.
- The caller owns `UIEngineHost` and, when supplied separately, `UIEngineWorkspace`.
- UI changes run through the toolkit dispatcher; domain access runs through the host dispatcher.
- Keyboard operation is complete without requiring mouse input.
- Core has no XenoAtom dependency.

## Verification

Tests cover:

- root selection and navigator creation;
- duplication with independent node and control instances;
- destructive back navigation and navigator removal;
- terminal nodes;
- broken restored paths and recovery by going back;
- scalar editing, validation, and dirty drafts;
- bounded collection windows;
- method invocation and continued execution after control disposal;
- node-provided property-change refresh;
- independent navigator lifetimes;
- layout round trips;
- keyboard-only workflows and resize; and
- caller-owned host lifetime.
