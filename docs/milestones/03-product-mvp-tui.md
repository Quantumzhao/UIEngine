# Milestone 03 — Product MVP TUI

**Status:** In Progress

**Toolkit:** XenoAtom.Terminal.UI 3.9.0

**Architecture:** [TUI frontend architecture](../tui-frontend-architecture.md)

## Goal

Deliver a reusable TUI that presents a `UIEngineWorkspace` as one or more independent
navigators. It must browse and edit the exposed graph, invoke methods, display bounded collections,
and save or restore navigator layouts without domain-specific screens.

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
- Method nodes generate parameter forms and present result, completion, and structured failure.
- Refresh re-reads authoritative values and preserves dirty drafts.
- Every structured target state has a distinct presentation and valid recovery commands.

## Lifetime Requirements

- Each navigator and current control has independent presentation state.
- Going back or removing a navigator removes the affected controls.
- No disposed control receives later updates.
- Removing one navigator does not interfere with another navigator's work.
- Removing a method control prevents later completion from updating that control without stopping
  its started invocation.
- Closing the TUI leaves the caller-owned host usable.

## Layout Requirements

The saved layout contains:

- navigator identifiers and order;
- current logical paths; and
- stable placement, size, and selection configuration.

It does not contain domain values, handles, nodes, controls, drafts, progress, or
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

Keep the solution buildable after every step. Add the smallest meaningful tests with each behavior;
do not defer Core or lifetime coverage to the final TUI acceptance pass.

### Migration assumptions

- `BaseNode` is the sole frontend-facing and runtime interaction model. Reflection and
  programmatic exposure metadata create nodes directly rather than maintaining a parallel model.
- Nodes expose language semantics through a small base contract plus composable facets. Avoid a
  separate concrete class for every possible combination of property, field, value kind,
  nullability, and mutability.
- A node has no public logical-path property. Path plus resolution state belongs to a navigation
  entry or another Core-owned resolution envelope.
- `UIEngineWorkspace` and `Navigator` do not own the `UIEngineHost`. A TUI-created Core workspace
  may be owned by `TuiWorkspace`; a caller-supplied Core workspace remains caller-owned.
- Core produces versioned layout data but does not choose a file, perform file I/O, or persist live
  state. The TUI owns its stable presentation fields and maps them into that data contract.
- The existing conversion, validation, identity, domain-thread affinity, bounded-stream, and
  invocation implementations are behavior to preserve, not subsystems to redesign.

### Completed foundation

- [x] Qualify and pin XenoAtom.Terminal.UI 3.9.0 through the toolkit spike.
- [x] Establish reusable fullscreen and embedded hosting with the same root visual.
- [x] Establish the Core/TUI/domain dependency boundaries and thin example executable.
- [x] Prove that closing the current TUI boundary leaves the caller-owned host usable.

### 1. Specify the node and navigation contracts

Before moving behavior, define and test the smallest public contracts needed by both frontends.

1. Define `BaseNode` and composable language-semantic facets for:
   - object and reference access;
   - property, field, and method membership;
   - readable, writable, and nullable values;
   - string/character, boolean, number, and enum values;
   - collections and their bounded entry shapes; and
   - method parameters, results, and progress metadata.
2. Record how reflected properties and fields combine with value/reference/collection facets. For
   example, a writable enum property must remain identifiable as a property, enum, readable value,
   and writable value without Core naming a control.
3. Define the semantic treatment of programmatic scalar exposures so they do not falsely claim to
   be reflected fields or properties.
4. Define a resolved-node envelope or equivalent Core-owned location token. It must carry the
   canonical path beside a node, allow a workspace to start from an already resolved occurrence,
   and still keep logical paths off the node itself.
5. Define `NavigationEntry` as path plus either a fresh node or a structured resolution failure.
   A broken entry is navigation state, not a fake node kind.
6. Define synchronous mutation results and frontend-neutral change notifications for navigator add,
   navigate, back, duplicate, reorder, and remove operations. Notifications must let the TUI
   retire exactly the control and scope associated with a removed entry without exposing toolkit
   types from Core.
7. Keep mutation and resolution synchronous on the domain thread so no queued older resolution can
   publish after navigation, back, removal, or disposal.

Verification:

- Add contract tests for overlapping facets, fresh occurrence identity, terminal nodes, structured
  broken entries, and host/workspace ownership.
- Keep Core free of XenoAtom references and presentation terminology.

### 2. Create live nodes directly

Build nodes from exposure metadata while preserving the live-domain guarantees.

1. Keep conversion, validation, collection access, and invocation in node-owned operation
   bindings. Do not retain a second public or internal object/member model.
2. Resolve a registered root to an object node and every exposed member to a node with the correct
   overlapping facets. Continue to expose only opted-in reflection members and configured
   programmatic values.
3. Create new node instances on each resolution. Nodes may share the same host, live target, runtime
   handle, and domain identity, but never the same node occurrence across navigator entries.
4. Keep live operations host-mediated:
   - reads and writes resolve the current owner synchronously on the domain thread;
   - writes use the existing invariant conversion and validation pipeline;
   - reference access handles null and unavailable targets explicitly;
   - collection reads retain the host maximum and closed entry variants; and
   - method invocation returns the existing host-owned `ActionInvocation` lifetime.
5. Preserve metadata needed by generated controls: runtime type, summary, read/write capability,
   nullability, enum members, numeric range, method defaults, result type, and progress type.
Verification:

- Port the existing Core behavior tests to nodes and delete the legacy model coverage.
- Add focused tests for property versus field semantics, enum and numeric overlap, read-only and
  nullable values, null references, programmatic values, and two fresh nodes operating on the same
  live value.
- Confirm bounded collection, validation, domain-affinity, and invocation tests retain their
  current behavior.

### 3. Resolve logical paths to every node kind

Replace the current owner-plus-optional-member result with node resolution that works uniformly for
roots, members, collections, selected elements, scalars, and methods.

1. Resolve root paths to object nodes and member paths to the member node itself, including
   terminal scalar and method nodes.
2. Resolve reference members without recursively materializing their target graph. A successfully
   resolved reference can expose object semantics for its current live target while retaining its
   reference and property/field semantics.
3. Resolve a collection path to its collection node and a selector path to the selected reference
   element. Keep selector lookup bounded and retain index, key, and domain-identity selectors.
4. Define semantic parent calculation rather than only removing the final path segment:
   - the parent of `/world/Name` is `/world`;
   - the parent of `/world/Nations[index=0]` is `/world/Nations`; and
   - the parent of `/world/Nations[index=0]/Name` is
     `/world/Nations[index=0]`.
5. Return enough semantic ancestor information to initialize a navigator or rebuild its destructive
   stack from one saved current path.
6. Preserve canonical escaping, case sensitivity, cycles, shared references, compatible identity
   recovery, and conflicting-identity refusal.
7. Keep path results node-based and remove legacy member-kind and binding-specific resolution once
   all consumers use resolved nodes.

Verification:

- Cover every node kind as the end of a path.
- Cover all three selector kinds, collection-to-element parent traversal, cycles, shared references,
  compatible replacement, identity conflict, null, missing, ambiguous, and unavailable targets.
- Assert that resolving the same path twice returns distinct nodes with the expected shared domain
  handle or identity.

### 4. Implement `UIEngineWorkspace` and `Navigator`

Add the frontend-neutral navigation session only after node and path behavior is stable.

1. Add a caller-disposable `UIEngineWorkspace` over one caller-owned host and an ordered,
   read-only public view of its navigators.
2. Create a navigator from:
   - a selected registered root;
   - an absolute logical path; and
   - a supplied resolved-node occurrence through the location hand-off defined in Step 1.
3. Give each navigator a stable identifier, an ordered entry stack, and one current entry.
4. Navigate deeper by resolving and pushing a fresh node. Reject navigation from terminal nodes
   without changing the stack.
5. Go back by permanently removing the current entry. Expose no forward history, and define the
   root-entry behavior as a structured no-op/failure rather than silently removing the navigator.
6. Duplicate the current navigator by resolving its current path into a new navigator with fresh
   entries and node instances.
7. Remove and reorder navigators without affecting their domain objects, host-owned invocations,
   or any other navigator.
8. Retain an attempted target as a broken current entry when resolution fails. Back must remove the
   broken entry and reveal the previous entry.
9. Dispose all entries and publish deterministic removal notifications when a navigator or
   workspace is disposed. Do not make a node own frontend work.

Verification:

- Test push, destructive back, terminal rejection, duplicate, reorder, remove, disposal, and no
  forward history.
- Prove two navigators at one path have distinct nodes and navigation stacks while writes remain
  visible through their shared domain object.
- Prove removal of one navigator cannot invalidate another navigator's operation.
- Prove workspace disposal leaves its host and already-started invocation alive.

### 5. Add versioned layout snapshots and restore

Implement address persistence before building TUI save/restore commands.

1. Define a small versioned layout DTO containing navigator identifiers, order, current paths, the
   selected navigator identifier, and stable per-navigator presentation configuration.
2. Choose one bounded, serializable representation for TUI presentation configuration. Keep it
   opaque to Core and restrict the initial schema to placement and size fields required by this
   milestone.
3. Snapshot only stable data. Add explicit tests preventing domain values, runtime handles, node or
   control instances, drafts, progress, and invocations from entering the contract.
4. Restore each navigator independently. One invalid record must not discard the remaining valid
   navigators.
5. Reconstruct each navigation stack from the saved current path and its semantic parents. Include
   the collection path before a selected element path.
6. If any restored prefix cannot resolve, retain that prefix and each requested descendant as
   structured broken entries so repeated back navigation eventually reaches the deepest valid
   ancestor.
7. Define deterministic handling for an unknown layout version, duplicate navigator identifiers,
   an invalid selected identifier, empty layouts, and invalid presentation fields.
8. Keep JSON or other file storage in the caller/example layer; Core only creates and consumes the
   serializable snapshot.

Verification:

- Round-trip multi-navigator order, identifiers, paths, selection, placement, and size.
- Restore mixed valid and broken navigators and navigate backward from a broken selected element to
  its valid collection.
- Inspect serialized test data to prove excluded live state is absent.

### 6. Migrate the CLI to one navigator

Use the CLI as the first complete consumer of the new Core architecture before adding substantial
TUI behavior.

1. Replace `CliSession`'s private location bindings with one `UIEngineWorkspace` and one
   `Navigator`.
2. Derive the prompt, current path, parent/back behavior, member completion, reads, writes,
   collection windows, and method invocation from the current node and its facets.
3. Preserve existing command grammar and output where the architecture does not require a change.
   Extend navigation/inspection so scalar, collection, and method nodes can be current and terminal.
4. Use navigator back semantics for `cd ..`; do not reintroduce a CLI-only history model.
5. Preserve the CLI executable's existing ownership of its host while making workspace disposal
   explicit.
6. Delete the legacy CLI adapters after the CLI tests pass on nodes.

Verification:

- Keep the existing redirected cyclic-world, validation, bounded collection,
  replacement and completion tests.
- Add CLI cases for terminal current nodes, collection-selector parent traversal, destructive back,
  and a structured broken path where the command workflow can create one.

### 7. Rebase the TUI hosting boundary on the Core workspace

Preserve the completed XenoAtom hosting boundary while changing its model source.

1. Let `TuiFrontend` either:
   - create and own a `UIEngineWorkspace` from a caller-owned host; or
   - present a caller-supplied `UIEngineWorkspace` without taking ownership.
2. Update `TuiWorkspace` to own only its visual tree, TUI state, and an internally created Core
   workspace when applicable. It never owns a caller-supplied host or workspace.
3. Replace `InitialPath` with startup configuration that can create the initial navigator or load a
   supplied layout. Define the empty-workspace behavior without treating `/` as an object node.
4. Maintain a TUI presentation record keyed by navigator identifier. Each record owns the current
   control.
5. React to Core navigation notifications by creating one replacement control for a pushed/revealed
   entry and removing the departed control exactly once.
6. Marshal visual state changes with the XenoAtom dispatcher. Domain access remains synchronous on
   the domain model's thread, with any cross-thread request boundary owned outside Core.

Verification:

- Extend boundary tests for both ownership paths, empty startup, restored startup, and disposal.
- Retain the existing caller-owned host tests against the Core workspace-backed implementation.

### 8. Build multi-navigator workspace chrome

Build the workspace interaction shell with placeholder node controls before completing the control
catalogue.

1. Add root selection and commands to create, select, duplicate, reorder, and remove navigators.
2. Give each navigator a header showing its current canonical path, broken/healthy state, and
   available back/remove commands.
3. Define stable keyboard commands and focus order for root selection, navigator selection,
   duplicate, remove, back, activation, refresh, save/load, help, and close.
4. Present navigators side by side at accepted normal widths and one selected navigator at a time at
   narrow widths.
5. Make resize a presentation recomposition only. Preserve Core navigator identity and paths plus
   TUI selection and draft state.
6. Cover zero navigators, one navigator, several navigators, and removal of the selected navigator
   with deterministic next-selection behavior.

Verification:

- Inject keyboard input for every workspace command and verify visible focus and return focus after
  dialogs.
- Resize across normal and narrow thresholds without recreating navigators or losing selection.
- Duplicate one navigator and prove the two headers refer to distinct Core navigator and node
  instances.

### 9. Add the node-to-control factory and scalar controls

Select controls from semantic facets rather than `MemberKind`, concrete domain types, or path
shape.

1. Add one control factory with documented precedence for overlapping facets. For example, enum
   editing takes precedence over the generic scalar editor, while property and field facets supply
   labels/metadata rather than choosing a widget.
2. Add object/reference presentation with summary, runtime identity information where useful, and
   activatable child nodes. Do not recursively render descendants.
3. Add read-only scalar display and writable editors for string/character, boolean, number, and
   enum values.
4. Represent read-only, nullable, loading, empty, dirty, validation, unavailable, and successful
   states explicitly.
5. Keep drafts in the control. Commit through the node, associate validation issues with the
   editor, re-read authoritative state after success, and allow discard/reload.
6. Navigate to any selected child as a new current entry. Scalar and method controls expose no
   deeper-navigation command.

Verification:

- Test factory selection for every supported facet combination, especially writable enum and
  numeric properties and reflected fields.
- Test conversion and validation display, explicit null, read-only state, dirty-draft preservation,
  successful re-read, and control disposal during an in-flight read.
- Confirm controls depend only on Core node contracts and contain no cyclic-world types.

### 10. Add bounded collection and method controls

1. Render one configured collection window at a time with position, optional key, and distinct null,
   scalar, and reference entries.
2. Add previous, next, direct offset, refresh, count/has-more, loading, empty, and failure states.
   Never request more than `CollectionWindowSize` or the host maximum.
3. Navigate reference entries through their canonical selector location. Keep scalar and null
   entries non-navigable unless Core later defines element nodes for them.
4. Generate method fields from parameter value semantics and preserve the distinction among
   omitted, defaulted, explicit-null, and supplied values.
5. Start a method once per submission, prevent accidental duplicate submission, and present
   synchronous/asynchronous completion, result, structured failure, and bounded ordered progress.
6. On method-control disposal, release its frontend-owned resources without stopping the
   host-owned invocation.

Verification:

- Prove large and lazy collections remain bounded across several windows and selector navigation.
- Test every collection entry shape and recovery after a source reset.
- Test method parameter validation/default/null semantics, progress order, success/failure, and
  continued invocation after control removal.

### 11. Integrate refresh, replacement, and structured recovery

1. Provide an explicit refresh action for controls backed by live values.
2. Re-read through the current node without applying completed work to a detached control.
3. Never overwrite a dirty draft. Let the user keep, reload, or commit the draft.
4. Re-resolve through the navigator after compatible replacement so the active entry receives a
   fresh node. Refuse conflicting identity instead of silently rebinding.
5. Provide valid recovery commands for null, unavailable, not found, ambiguous, type mismatch,
   permission, disposed, fault, and broken-restored-path states without parsing error
   messages.

Verification:

- Test explicit refresh, dirty-draft handling, collection replacement, compatible replacement,
  and conflicting replacement.
- Remove one of two same-path navigators and prove the remaining navigator still refreshes.
- Assert that no disposed control receives a later posted update.

### 12. Connect layout commands and complete acceptance

1. Map the TUI's selected navigator, placement, and size to the Core layout snapshot without adding
   draft, focus, loaded-value, or running-work state.
2. Expose save/load through caller-supplied callbacks or another explicit composition boundary so
   the reusable TUI library does not choose a filesystem location.
3. Reconcile controls when a layout is loaded: retire removed presentations, retain navigator
   identifiers from the snapshot, create fresh nodes and controls, and select the restored
   navigator deterministically.
4. Finish `Examples/CyclicWorld.Tui` as composition and manual-acceptance code only. Add no
   model-specific control or Core behavior to the executable.
5. Automate the full acceptance workflow above with deterministic toolkit input where practical,
   then perform a real-terminal smoke test for rendering and focus behavior.
6. Update the README, architecture documents, CLI command reference, milestone index, and
   `TODO.MD` only after the corresponding behavior is implemented.

Verification:

- Run the focused Core, CLI, TUI, and toolkit-spike tests while implementing each increment.
- Run `dotnet build UIEngine.sln` and require zero warnings.
- Run `dotnet test UIEngine.sln --no-build` and require all Core, CLI, TUI, dependency, lifetime,
  keyboard, resize, and acceptance tests to pass.
- Record any terminal platform not exercised rather than implying support.

## Exit Criteria

- The complete acceptance workflow passes without model-specific controls.
- Navigator duplication and removal are isolated.
- Back navigation is destructive and has no forward history.
- Broken saved paths remain visible and recoverable.
- Collection access and asynchronous streams stay bounded.
- Frontend disposal detaches all readers without stopping domain work.
- Core has no XenoAtom reference and the TUI has no domain-fixture reference.
- `dotnet build UIEngine.sln` completes with zero warnings.
- `dotnet test UIEngine.sln --no-build` passes.
