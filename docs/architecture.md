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
    Nodes --> Frontends[TUI, or another frontend]
```

## Boundaries

### `UIEngineHost`

The host owns:

- discovered domain roots;
- runtime handles;
- reflection and programmatic exposure;
- path resolution and live operations;
- single-threaded domain interaction; and
- non-owning diagnostic reporting for invocation faults.

Exactly one host is active at a time and is available through `UIEngineHost.Instance`. Its live
graph operations are synchronous and run on the same thread as the domain model. Constructing a
host discovers public static members marked `[Root]` from every assembly already loaded, in
canonical assembly/type/member order. Root methods must also be marked `[Action]`. Invalid roots
are skipped and reported through diagnostics. Discovery runs once; assemblies loaded later are not
added. Disposing the host disposes its registered Core workspaces and permits a replacement host.

### `UIEngineWorkspace`

A workspace is a frontend-neutral navigation session over the active host. It owns an ordered collection
of `Navigator`s and produces a serializable workspace snapshot. A host can support more
than one workspace without sharing navigation state between them.

### Frontends

A frontend maps node semantics to controls, creates and owns its workspace, and owns all toolkit
state. Core never references controls, focus, colors, geometry, key bindings, or a UI dispatcher.

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
| Reflected method | `IMethodNode` | Parameter, result, and invocation-status metadata |
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

## Handles and Paths

UIEngine keeps two concepts separate:

| Concept | Purpose |
|---|---|
| `Guid` | Identifies one root or live reference within a host lifetime. |
| `LogicalPath` | Identifies how a navigator reached an exposed node. |

Logical paths are immutable, case-sensitive linked structures. Their first segment identifies a
root by its host-lifetime GUID. Later segments store member names, list indices, and dictionary
keys directly without encoding them into a string. The leading `/` is display decoration rather
than a root node.

```text
/world
/world/Name
/world/Nations
/world/Nations[index=0]
/catalog/Items[key=SKU-42]
```

Each linked segment expresses one navigation step. `MemberLogicalPathSegment` names an unselected
member, `ListLogicalPathSegment` identifies one list index, and `DictLogicalPathSegment` identifies
one dictionary key. Resolution verifies that the live collection provides the corresponding list
or dictionary semantics. Future selection or batch operations extend the model with additional
logical-path segment variants.

A path can end at any node, not only a reference object. Parent traversal follows navigation
semantics: the parent of a selected collection element is its collection node, and the parent of a
member is its containing node.

Path resolution returns a fresh node plus its canonical path. It follows the current graph, so a
path naturally resolves to a replacement object when the corresponding root, member, or collection
entry changes.

The returned resolution chain contains one fresh `ResolvedNode` for every linked semantic location
from the registered root through the current node. A selected collection element therefore follows
its collection node directly in both the logical path and the resolution chain.

`LogicalPath.ToString()` exposes the raw root GUID for diagnostics. `ToDisplayString()` resolves
that GUID to the root node's member name through the active host. Neither string form is a
serialization format; layout persistence stores structured path segments, including the root GUID.

`ResolvedNode` is the Core-owned hand-off for that pair. Neither `ResolvedNode`, `BaseNode`, nor a
node's semantic facets retains a host reference.

## Navigators

A `Navigator` is one independent entry point into the graph. It owns a stack of navigation entries.
Its stable identity is a GUID; navigators have no names. Root nodes and members still have their
ordinary node names, but root registration stores no separate name.
Each entry contains:

- a logical path;
- the resolved `BaseNode`, when available; and
- structured resolution state.

Navigation has destructive stack semantics:

1. Starting a navigator resolves its initial node and pushes the first entry.
2. Activating a navigable child resolves a fresh node and pushes a new entry.
3. Going back removes the current entry, then reveals the previous entry.
4. There is no forward history.
5. Removing a navigator retires all of its entries.

A navigator may start from a registered root or a specific resolved `BaseNode`. The workspace
captures the node's current location and resolves a navigator-owned instance. Duplicating a
navigator follows the same rule at the current path, so the two navigators do not share nodes.

Navigator mutations and path resolution run synchronously on the domain thread. A successful
mutation returns the same committed change object published by its change notification; a failed
mutation publishes no change. Since Core does not queue or dispatch mutations, it cannot later
restore stale navigation state after back, removal, or disposal.

A navigator has no workspace or host reference. It raises a synchronous path-resolution request
and its own committed-navigation event. `UIEngineWorkspace` subscribes to both when it adds the
navigator, resolves requests through the active host, relays committed changes through the workspace
aggregate event, and unsubscribes both before permanent removal.

If a path cannot resolve, the navigator is invalid and its current entry retains the structured
failure. Back navigation remains available, but a frontend displays no node content for that
entry. A restored invalid path can therefore be unwound until a valid node is reached.

## Workspace Snapshots and Layout Persistence

A workspace creates an unversioned snapshot containing:

- navigator GUIDs and order;
- each navigator's current structured logical path; and
- the selected navigator GUID.

Each supported `ILogicalPathSegment` has a corresponding serializable snapshot type. Restore is
optimistic: malformed navigator records are reported and skipped without discarding usable records,
and unknown serialized fields do not require a schema-version gate.

The snapshot does not contain domain values, runtime handles, node instances, control instances,
edit drafts, invocation state, or running work.

Deserialization reconstructs navigation entries from each saved path and its semantic parent
locations. Resolution failures create invalid current entries rather than dropping saved
navigators. Frontends own any outer layout contract and stable presentation configuration such as
placement or size. Serialization produces data; file storage remains the caller's responsibility.

## Operations and Lifetime

All live operations flow synchronously through the host on the domain model's owning thread.
Expected outcomes use `Either<InteractionError, T>` with stable error codes and validation issues;
nullable success values use `Either<InteractionError, Option<T>>`. UIEngine and the domain model
share one thread, and Core contains no synchronization or thread-marshaling machinery.

Collection access is always bounded. Collection windows retain positions, optional keys, nulls,
scalar values, references, optional total counts, and whether more data is available.

Each method-node occurrence exposes its latest invocation status and a result task carrying the
structured outcome for that exact call. Synchronous execution is observable as running, and one
occurrence accepts only one running call at a time. Separate occurrences can invoke the same domain
method with overlapping asynchronous work. Once reflection returns a domain task, that task continues independently of the
node and host; the node only observes its outcome.

## Frontend Lifetime

For every active navigation entry, a frontend creates a control. Invocation updates are applied
only while that entry is still current.

When an entry is removed, the frontend removes its control and detaches its completion continuation.
Already-started domain tasks may finish, but they cannot update a detached visual tree. Host disposal
does not cancel them or replace their natural outcome with a host-lifetime failure.

Removing one navigator cannot interfere with work owned by another navigator, even when both point
to the same path.

## Dependency Direction

```text
Domain application ----------> Core
Frontend/Tui ----------------> Core + XenoAtom.Terminal.UI
Examples --------------------> domain + selected frontend
Tests -----------------------> implementation projects
```

Core has no frontend dependency. Frontends do not depend on a specific domain assembly.
