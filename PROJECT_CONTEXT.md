# UIEngine Project Context

## Purpose

UIEngine exposes a running .NET application's live objects as a navigable, editable, and
invocable graph. Domain objects remain authoritative; UIEngine provides frontend-neutral nodes,
navigation, paths, validation, and invocation observation.

## Architecture

`UIEngineHost` discovers roots and owns access to the live domain graph. Exactly one host is active
at a time. A `UIEngineWorkspace` uses that active host and owns an ordered collection of independent
`Navigator`s. Each navigator tracks one stack of logical paths
alongside `Either<InteractionError, Option<BaseNode>>` resolution results.

`BaseNode` is frontend-neutral. Its concrete types and semantic interfaces describe .NET
language features rather than controls. Names follow those features—for example, `PropertyNode`,
`MethodNode`, `NumberNode`, and `EnumNode`. A node may combine several semantics; an enum-valued
property is both a property and an enum.

Frontends map node semantics to controls. They do not access domain members directly or place
toolkit types in Core.

## Settled Decisions

- Every exposed root or member resolves to an `BaseNode`.
- The active host discovers public static `[Root]` members from assemblies loaded at construction.
  Root methods also require `[Action]`; invalid markers are skipped and logged.
- Root registrations have host-lifetime GUIDs and no separate names. The root `BaseNode` retains
  the reflected member's ordinary name.
- Exactly one host is active at a time. Nodes, bindings, workspaces, and frontends use that host
  without retaining explicit host references. Host disposal retires its registered workspaces.
- `/` is path decoration. An absolute path begins with a root-GUID segment, followed by ordinary
  member or collection-selection segments.
- Every node can be the current node of a navigator. Scalar and method nodes are terminal.
- Nodes are live operation endpoints, not copied domain state.
- Nodes do not own logical paths or UI controls.
- A navigator records the path and stack for one independent view into the graph.
- A navigator holds no workspace or host reference. It requests path resolution and publishes
  committed navigation changes through events; the owning workspace subscribes while the
  navigator belongs to its ordered collection.
- Navigating deeper pushes a new entry. Going back removes the current entry permanently.
- Removing an entry or permanently removing a navigator causes its frontend control and
  frontend-owned work to be disposed. A TUI-close operation may instead hide one live presentation
  in its transient one-item cache.
- Each method-node occurrence owns its latest invocation state. The underlying domain task
  continues independently after its control is removed or the host is disposed.
- A workspace may contain several navigators at the same path. Each resolves separate node and
  presentation instances while sharing the same domain object.
- A navigator can start from a registered root or a specific resolved `BaseNode`. The workspace
  captures that node's location and resolves a navigator-owned instance.
- An unresolved persisted path leaves its navigator invalid and displays no node content. The user
  can navigate back until a valid node is reached.
- Navigators have stable GUID identities and no names. Core workspace snapshots store navigator
  GUIDs, order, structured logical paths, and selected GUID.
  Frontend-owned layouts may add stable presentation configuration. Neither stores domain values,
  runtime handles, node instances, edit drafts, or running operations.
- Runtime handles and logical paths remain distinct concepts.
- Collections are read in bounded windows.
- Expected failures use structured results. Unexpected faults remain available only for trusted
  diagnostics.
- Core live operations are synchronous and are called on the domain model's owning thread.
- The TUI and its owned `UIEngineWorkspace` are confined to the XenoAtom UI thread. TUI commands
  call Core synchronously on that thread; do not add dispatching or cross-thread synchronization.

## Architectural Invariants

1. Cycles, shared references, replacement, nulls, and unavailable targets are normal graph states.
2. Node semantics describe the exposed language feature, not a widget or interaction archetype.
3. A node instance belongs to one resolved navigator occurrence and is never shared between
   navigators.
4. Navigation state belongs to `Navigator`; domain access belongs to `UIEngineHost`; invocation
   observation belongs to the originating method-node occurrence.
5. Core contains no frontend or toolkit types.
6. Persisted workspace snapshots contain addresses and selection, not live state; frontend layouts
   may separately add stable presentation configuration.

## Repository Structure

- `Core/` — host, exposure, handles, paths, nodes, workspaces, and navigation.
- `Frontend/Tui/` — reusable XenoAtom-based TUI frontend.
- `Examples/CyclicDomain/` — cyclic and shared-reference acceptance model.
- `Examples/CyclicWorld.Tui/` — thin TUI composition executable.
- `Tests/` — black-box Core and TUI behavior tests.
- `docs/architecture.md` — authoritative architecture.
- `REBOOT_PLAN.md` — implementation sequence and acceptance criteria.
- `TODO.MD` — progress tracker.

## Verification

```sh
dotnet build UIEngine.sln
dotnet test UIEngine.sln --no-build
```

The build must complete with zero warnings.

## Coding Style

- Don't add null checks when the method's parameters are known to be not-null
- No need to check if an object is disposed. 
- Never over-design, and be cautious when applying design patterns. 
- `Task`s do not accept cancellation tokens; stopping a task is beyond our scope.
- In general, files should be within 500 lines. If you think it qualifies being longer than that, discuss it with me. Long files are usually the symbol of bad design. Test files and configurations are exceptions to this. 
- If classes/structs are wrappers/converters of other ones, think twice if that's necessary. Be extremely cautious of applying factory design pattern. Ask me if you really need to.
- UIEngine runs on the same thread as domain model (single thread), and TUI uses only 1 UI thread. You must not implement multi-thread machinary to avoid over-design.
