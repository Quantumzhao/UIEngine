# Architecture

UIEngine presents a live .NET object graph through frontend-neutral `BaseNode`s. A workspace
holds independent `Navigator`s, and each navigator resolves one path through that graph at a time.

```mermaid
flowchart LR
    Domain[Live domain objects] --> Host[UIEngineHost]
    Host --> Nodes[BaseNode instances]
    Host --> Workspace[UIEngineWorkspace]
    Workspace --> Navigators[Navigator collection]
    Navigators --> Nodes
    Nodes --> Frontends[CLI, TUI, or another frontend]
```

## Boundaries

### `UIEngineHost`

The host owns:

- registered domain roots;
- runtime handles and optional stable domain identities;
- reflection and programmatic exposure;
- path resolution and live operations;
- domain dispatch;
- observation subscriptions; and
- started invocation lifetime.

Hosts are isolated and disposable. They contain no process-global registry or frontend state.

### `UIEngineWorkspace`

A workspace is a frontend-neutral navigation and layout session over one host. It owns an ordered
collection of `Navigator`s and produces a serializable layout snapshot. A host can support more
than one workspace without sharing navigation state between them.

### Frontends

A frontend maps node semantics to controls and owns all toolkit state. It may create a workspace,
or present a caller-supplied workspace. Core never references controls, focus, colors, geometry,
key bindings, or a UI dispatcher.

## Object Nodes

Every exposed root and member resolves to an `BaseNode`. A node represents one live exposure
occurrence; it is not a recursively copied object tree.

Node types and interfaces describe .NET language semantics. The model includes concepts such as:

- object and reference;
- property and field;
- method;
- collection and element;
- string, boolean, number, and enum; and
- nullability, mutability, and validation metadata.

Names follow those concepts—for example, `PropertyNode`, `MethodNode`, `NumberNode`, and
`EnumNode`—rather than presentation archetypes. Concrete variants and semantic interfaces may be
combined where the language concepts overlap.

Semantic facets may overlap. For example, a writable enum property has property, enum, and
writable semantics. Its frontend chooses an appropriate editor from those semantics without Core
naming a widget such as a choice control.

The source of an exposure and its value shape are independent facets:

| Exposure | Membership facet | Additional facets |
|---|---|---|
| Reflected property | `IPropertyNode` | Readable/writable/nullability and value-shape facets |
| Reflected field | `IFieldNode` | Readable/writable/nullability and value-shape facets |
| Reflected method | `IMethodNode` | Parameter, result, and progress metadata |
| Programmatic scalar | `IProgrammaticValueNode` | Readable/writable/nullability and value-shape facets |

A programmatic scalar is deliberately not an `IPropertyNode` or `IFieldNode`; it is an exposed
value without a reflected-member claim. String, character, Boolean, number, and enum facets are
orthogonal to membership, reading, writing, and nullability.

Nodes expose the live operations appropriate to their semantics:

- readable nodes read the current value;
- writable nodes convert, validate, and write;
- reference and object nodes expose navigable members;
- collection nodes return bounded windows and expose navigable reference elements; and
- method nodes bind parameters and start invocations.

Every node may be displayed as a navigator's current node. Scalar and method nodes are terminal.
A method result is presented by the method node and does not implicitly create a navigation edge.

Node instances are not shared between navigators. Two navigators at the same path have independent
node and presentation instances while operating on the same live domain object.

## Identity and Paths

UIEngine keeps three concepts separate:

| Concept | Purpose |
|---|---|
| `ObjectHandle` | Identifies one live reference within a host lifetime. |
| Domain identity string | Optionally identifies a domain entity across compatible replacement. |
| `LogicalPath` | Identifies how a navigator reached an exposed node. |

Logical paths are absolute, case-sensitive, and percent-escaped. Collection elements use an
explicit index, key, or domain-identity selector.

```text
/world
/world/Name
/world/Nations
/world/Nations[index=0]
/catalog/Items[key=SKU-42]
/world/Nations[identity=nation%2FN1]
```

A path can end at any node, not only a reference object. Parent traversal follows navigation
semantics: the parent of a selected collection element is its collection node, and the parent of a
member is its containing node.

Path resolution returns a fresh node plus its canonical path. Resolution may use stable domain
identity to survive compatible replacement, but it must reject a path that resolves to a
conflicting identity.

`ResolvedNode` is the Core-owned hand-off for that pair. It retains its originating host
internally so a workspace can reject an occurrence from another host, while neither
`BaseNode` nor its semantic facets expose a logical path.

## Navigators

A `Navigator` is one independent entry point into the graph. It owns a stack of navigation entries.
Each entry contains:

- a logical path;
- the resolved `BaseNode`, when available; and
- structured resolution state.

Navigation has destructive stack semantics:

1. Starting a navigator resolves its initial node and pushes the first entry.
2. Activating a navigable child resolves a fresh node and pushes a new entry.
3. Going back removes and disposes the current entry, then reveals the previous entry.
4. There is no forward history.
5. Removing a navigator disposes all of its entries.

A navigator may start from a registered root or a specific resolved `BaseNode`. The workspace
captures the node's current location and resolves a navigator-owned instance. Duplicating a
navigator follows the same rule at the current path, so the two navigators do not share nodes.

Navigator mutations are latest-request-wins. Starting a mutation advances that navigator's
generation and supersedes any older unresolved mutation. Resolution may complete in the
background, but it may publish an entry or change notification only if its generation is still
current. Back, removal, or disposal also invalidates affected pending work,
so an older resolution can never restore stale state. Successful mutation results and change
notifications carry the same committed change object; failed or superseded mutations publish no
change.

If a path cannot resolve, the current entry remains present with its structured failure. Back
navigation remains available. A restored broken path therefore stays visible and can be unwound
until a valid node is reached.

## Layout Persistence

A workspace serializes a versioned layout containing:

- navigator order and identifiers;
- each navigator's current logical path; and
- stable presentation configuration such as placement or size.

Each navigator produces its own serializable path and stable configuration. The workspace
aggregates those records into the layout snapshot.

The snapshot does not contain domain values, runtime handles, node instances, control instances,
edit drafts, subscriptions, invocation progress, or running work.

Deserialization reconstructs navigation entries from each saved path and its semantic parent
locations. Resolution failures create broken current entries rather than dropping saved
navigators. Serialization produces data; file storage remains the caller's responsibility.

## Operations and Lifetime

All live operations flow through the host and its `IInteractionDispatcher`. Expected outcomes use
`InteractionResult<T>` with stable error codes and validation issues.

Collection access is always bounded. Collection windows retain positions, optional keys, nulls,
scalar values, references, optional total counts, and whether more data is available.

Method invocation returns an `ActionInvocation` with terminal status, completion, and bounded
ordered progress. Once started, an invocation is owned by the host.
Removing a node or control stops only frontend observation of that invocation.

Observation uses domain notifications or explicit polling. Streams are bounded, overflow is
visible, and subscriptions detach deterministically.

## Frontend Lifetime

For every active navigation entry, a frontend creates a control. Property-observation
subscriptions belong to nodes and follow their lifetime; completed async work is applied only
while that entry's generation remains current.

When an entry is removed, the frontend removes its control. Already-started tasks may finish, but
generation checks prevent them from updating a detached visual tree. Removal does not stop the
domain object or a started invocation.

Removing one navigator cannot interfere with work owned by another navigator, even when both point
to the same path.

## Dependency Direction

```text
Domain application ----------> Core
Frontend/Cli ----------------> Core
Frontend/Tui ----------------> Core + XenoAtom.Terminal.UI
Examples --------------------> domain + selected frontend
Tests -----------------------> implementation projects
```

Core has no frontend dependency. Frontends do not depend on a specific domain assembly.
