# UIEngine Progress

## Runtime Foundation

- [x] Host-scoped roots and runtime handles.
- [x] Canonical logical paths.
- [x] Live reads, writes, conversion, and validation.
- [x] Bounded collection access.
- [x] Synchronous and asynchronous invocation with method-occurrence-local status and result.
- [x] Explicit domain dispatch.
- [x] Structured interaction failures.
- [x] CLI coverage of the live graph workflow.

## Object Nodes

- [x] Define the frontend-neutral `BaseNode` contract.
- [x] Model property, field, object, reference, collection, method, string, boolean, number, and
  enum semantics.
- [x] Support overlapping language-semantic facets.
- [x] Resolve every exposed root and member to a fresh node instance.
- [x] Preserve validation, dispatch, collection bounds, and invocation behavior.
- [x] Resolve logical paths to terminal as well as navigable nodes.
- [ ] Define semantic parent traversal for members and selected collection elements.
- [ ] Cover cycles, shared references, replacement, nulls, and unavailable targets.

## Workspaces and Navigators

- [ ] Add `UIEngineWorkspace` over a caller-owned `UIEngineHost`.
- [ ] Add and remove independent navigators.
- [ ] Initialize a navigator from a registered root or specific resolved node.
- [ ] Navigate deeper by pushing a fresh node entry.
- [ ] Navigate back by permanently removing the current entry.
- [ ] Duplicate a navigator at the same path with independent node instances.
- [ ] Keep broken paths as visible entries that can navigate back.
- [ ] Prove that one navigator cannot interfere with another navigator's presentation state.

## Layout Persistence

- [ ] Define a versioned workspace layout snapshot.
- [ ] Persist navigator names, order, paths, and stable presentation configuration.
- [ ] Restore each navigator independently.
- [ ] Preserve broken paths during restore.
- [ ] Verify that live state, handles, nodes, controls, drafts, and running work are never persisted.

## Product MVP — TUI

- [x] Qualify and pin XenoAtom.Terminal.UI 3.9.0.
- [x] Establish reusable fullscreen and embedded hosting.
- [ ] Present several navigators in one workspace.
- [ ] Add root selection, navigator duplication, removal, and destructive back navigation.
- [ ] Generate controls from node language semantics.
- [ ] Add scalar editors and method forms.
- [ ] Add bounded collection browsing.
- [ ] Refresh controls from live node operations and present structured failures.
- [ ] Save and restore navigator layouts.
- [ ] Show broken entries and allow back navigation to a valid node.
- [ ] Verify keyboard-only operation and terminal resizing.
- [ ] Complete the cyclic-world acceptance workflow.

## Product Release

- [ ] Pass Core, CLI, and TUI acceptance from a clean checkout.
- [ ] Define licensing, versioning, package metadata, and release policy.
- [ ] Define supported platforms and compatibility policy.
- [ ] Enable package generation after those policies are settled.

## Later Capabilities

- [ ] Batch operations with preview and explicit failure policy.
- [ ] Search and indexing.
- [ ] Tables, charts, and safe derived values.
- [ ] Source generation with an explicit AOT matrix.
- [ ] Authenticated remote access and auditing.
- [ ] Additional frontends backed by concrete consumers.
