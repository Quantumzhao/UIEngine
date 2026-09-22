# Milestone 03 — Product MVP TUI

**Status:** In Progress

**Toolkit:** XenoAtom.Terminal.UI 3.9.0

**Architecture:** [TUI frontend architecture](../tui-frontend-architecture.md)

## Goal

Deliver a reusable TUI that presents a `UIEngineWorkspace` as one or more independent
navigators. It must browse and edit the exposed graph, invoke methods, display bounded collections,
observe changes, and save or restore navigator layouts without domain-specific screens.

## Boundaries

- `Frontend/Tui` is a class library over Core and XenoAtom.Terminal.UI.
- Core contains no terminal or toolkit concepts.
- The TUI contains no dependency on a specific domain assembly.
- `TuiWorkspace` owns its visual tree and frontend work, not the caller's host.
- Fullscreen and embedded use the same controls.
- The example executable contains composition only.

## Required Workspace Behavior

- Show registered roots when creating a navigator.
- Add a navigator from a chosen root.
- Duplicate the current navigator at the same path with fresh node and control instances.
- Remove a navigator without affecting its domain object or other navigators.
- Navigate from any current node to its exposed children.
- Allow scalar and method nodes to be current while treating them as terminal.
- Go back by permanently removing the current navigation entry.
- Show unresolved paths as broken entries with back and remove commands.
- Save and restore paths plus stable layout configuration.

## Required Node Presentation

- Object and reference nodes show summary and navigable members.
- String, character, boolean, number, and enum semantics use appropriate editors.
- Read-only and nullable state is explicit.
- Writes use Core conversion and validation, then re-read authoritative state.
- Collections use bounded windows with position, key, null, scalar, and reference entries.
- Method nodes generate parameter forms and present result, progress, and structured failure.
- Observation refreshes only affected controls and preserves dirty drafts.
- Every structured target state has a distinct presentation and valid recovery commands.

## Lifetime Requirements

- Each navigator and current control has an independent operation scope.
- Going back or removing a navigator disposes the affected controls, readers, and subscriptions.
- No disposed control receives later updates.
- Removing one navigator does not cancel another navigator's work.
- Removing a method control stops its progress readers but not its started invocation.
- Closing the TUI leaves the caller-owned host usable.

## Layout Requirements

The saved layout contains:

- navigator identifiers and order;
- current logical paths; and
- stable placement, size, and selection configuration.

It does not contain domain values, handles, nodes, controls, drafts, subscriptions, progress, or
running operations. Every saved navigator is recreated, including broken entries.

## Interaction and Layout

- All workflows are keyboard accessible.
- Normal widths may present navigators side by side.
- Narrow widths may present one selected navigator at a time.
- Resize preserves navigator paths, identity, selection, and drafts.
- Loading, empty, success, validation, and failure states are explicit.

## Acceptance Workflow

The cyclic-world example must demonstrate:

1. Create a navigator from the `world` root.
2. Navigate through a cycle without recursive expansion.
3. Duplicate the navigator and operate both copies independently.
4. Navigate to scalar and method nodes as terminal current nodes.
5. Edit values and display validation failures.
6. Browse several bounded collection windows.
7. Start an asynchronous method, remove its control, and confirm the domain invocation continues.
8. Remove one navigator without disturbing the other.
9. Save and restore the layout.
10. Restore a broken path and navigate back to a valid node.
11. Complete the workflow using only the keyboard and repeat it after resize.
12. Close the TUI while leaving the host usable.

## Implementation Steps

### Completed foundation

- Qualify and pin XenoAtom.Terminal.UI.
- Establish reusable fullscreen and embedded hosting.
- Add independent operation scopes with deterministic cleanup.

### Remaining work

1. Implement the Core `ObjectNode`, `UIEngineWorkspace`, and `Navigator` contracts.
2. Build multi-navigator workspace chrome and keyboard commands.
3. Map object, reference, scalar, enum, number, collection, and method semantics to controls.
4. Add observation, structured failure, and replacement handling.
5. Add layout snapshots and TUI presentation configuration.
6. Complete the cyclic-world acceptance workflow.

## Exit Criteria

- The complete acceptance workflow passes without model-specific controls.
- Navigator duplication and removal are isolated.
- Back navigation is destructive and has no forward history.
- Broken saved paths remain visible and recoverable.
- Collection access and asynchronous streams stay bounded.
- Frontend disposal detaches all readers and subscriptions without stopping domain work.
- Core has no XenoAtom reference and the TUI has no domain-fixture reference.
- `dotnet build UIEngine.sln` completes with zero warnings.
- `dotnet test UIEngine.sln --no-build` passes.
