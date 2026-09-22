# UIEngine Architecture and Delivery Plan

## Objective

Expose live .NET objects through frontend-neutral `BaseNode`s and let users work through one or
more independent `Navigator`s. Preserve the existing guarantees for identity, paths, bounded work,
validation, observation, dispatch, and invocation lifetime.

## Target Model

```text
UIEngineHost
└── live roots, identity, resolution, and operations

UIEngineWorkspace
├── Navigator 1
│   └── navigation stack -> current BaseNode
├── Navigator 2
│   └── navigation stack -> current BaseNode
└── serializable layout configuration
```

`BaseNode` semantics follow .NET language concepts. Names such as `PropertyNode`, `MethodNode`,
`NumberNode`, and `EnumNode` describe those concepts directly. Nodes may combine overlapping
facets, and frontends choose controls from those semantics.

## Required Behavior

### Nodes

- Resolve every exposed root and member as an `BaseNode`.
- Allow every node to be the current node of a navigator.
- Treat scalar and method nodes as terminal.
- Keep node instances local to one navigator occurrence.
- Perform reads, writes, validation, bounded collection access, observation, and invocation
  through the host.
- Keep paths and toolkit controls out of nodes.

### Navigators

- Initialize from a registered root or a specific resolved node while creating a navigator-owned
  node instance at the same location.
- Push a fresh node entry when navigating deeper.
- Pop and dispose the current entry when navigating back.
- Provide no forward history.
- Support duplication by resolving a new navigator at the same path.
- Keep unresolved paths as broken entries with structured errors.
- Permit back navigation from a broken entry to a valid ancestor.

### Workspaces and Layouts

- Own an ordered collection of independent navigators.
- Add, duplicate, remove, and reorder navigators.
- Serialize current paths and stable presentation configuration.
- Restore missing or incompatible paths as visible broken entries.
- Never persist domain state, runtime handles, node instances, drafts, subscriptions, or running
  operations.

### Lifetime

- Dispose the frontend control and frontend-owned work when its navigation entry is removed.
- Isolate each navigator's frontend work from every other navigator.
- Leave the caller-owned host alive when a frontend closes.
- Keep started invocations alive under host ownership after their controls are removed.

## Implementation Sequence

### 1. Establish the node model

- Define the minimal `BaseNode` base contract and language-semantic variants or interfaces.
- Map reflection and programmatic exposure to nodes.
- Preserve conversion, validation, identity, dispatch, collection bounds, observation, and method
  invocation.
- Cover scalar, enum, property, field, reference, collection, and method semantics with black-box
  tests.

### 2. Resolve paths to any node

- Resolve roots, members, collections, and selected collection elements to fresh nodes.
- Define parent traversal for member and collection-selector paths.
- Preserve canonical escaping, identity recovery, and conflict detection.
- Test cycles, shared references, replacement, nulls, and unavailable targets.

### 3. Add workspace and navigator state

- Add `UIEngineWorkspace` over a caller-owned host.
- Add navigator creation from roots and specific resolved nodes.
- Implement destructive back navigation, duplication, removal, and isolation.
- Represent broken current entries without inventing a non-language node type.

### 4. Add layout snapshots

- Define a small versioned layout format.
- Serialize navigator paths, order, identifiers, and stable presentation configuration.
- Restore each navigator independently and retain broken entries.
- Keep file I/O outside Core.

### 5. Adapt the CLI

- Use one navigator for the normal command session.
- Keep commands and output stable where the architecture does not require a change.
- Verify terminal nodes, parent navigation, collection selectors, and broken paths.

### 6. Build the multi-navigator TUI

- Present the workspace's navigator collection.
- Let users choose roots, add navigators, duplicate the current navigator, and remove navigators.
- Map node semantics to controls without model-specific views.
- Remove controls with their navigation entries.
- Support layout save and restore.

## Acceptance

The architecture is complete when:

- every exposed root and member is represented by a frontend-neutral node;
- several navigators can independently display the same domain object;
- all node kinds can be current and terminal nodes cannot navigate further;
- back navigation destroys the departed entry and has no forward path;
- removing one navigator does not affect another or stop domain work;
- layout round trips preserve navigator paths and stable presentation configuration;
- broken restored paths remain visible and can navigate back;
- cyclic and shared-reference graphs do not recursively expand;
- all collection work and asynchronous streams remain bounded;
- Core, CLI, and TUI tests pass; and
- `dotnet build UIEngine.sln` completes with zero warnings.

## Verification

Run the smallest affected test project first, then:

```sh
dotnet build UIEngine.sln
dotnet test UIEngine.sln --no-build
```
