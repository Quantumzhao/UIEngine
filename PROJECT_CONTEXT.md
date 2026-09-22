# UIEngine Project Context

## Purpose

UIEngine exposes a running .NET application's live objects as a navigable, editable, and
invocable graph. Domain objects remain authoritative; UIEngine provides frontend-neutral nodes,
navigation, paths, validation, observation, and invocation lifetime.

The framework targets simulations, engines, services, research systems, internal tools, and
prototypes that need generated user interfaces over live state.

## Architecture

`UIEngineHost` owns registered roots and access to the live domain graph. An
`UIEngineWorkspace` owns an ordered collection of independent `Navigator`s. Each navigator holds
one navigation stack whose current entry contains a logical path and a freshly resolved
`BaseNode`.

`BaseNode` is frontend-neutral. Its concrete types and semantic interfaces describe .NET
language features rather than controls. Names follow those features—for example, `PropertyNode`,
`MethodNode`, `NumberNode`, and `EnumNode`. A node may combine several semantics; an enum-valued
property is both a property and an enum.

Frontends map node semantics to controls. They do not access domain members directly or place
toolkit types in Core.

## Settled Decisions

- Every exposed root or member resolves to an `BaseNode`.
- Every node can be the current node of a navigator. Scalar and method nodes are terminal.
- Nodes are live operation endpoints, not copied domain state.
- Nodes do not own logical paths or UI controls.
- A navigator records the path and stack for one independent view into the graph.
- Navigating deeper pushes a new entry. Going back removes the current entry permanently.
- Removing an entry or navigator causes its frontend control and frontend-owned work to be
  disposed.
- Started domain invocations remain owned by the host and continue after their control is removed.
- A workspace may contain several navigators at the same path. Each resolves separate node and
  presentation instances while sharing the same domain object.
- A navigator can start from a registered root or a specific resolved `BaseNode`. The workspace
  captures that node's location and resolves a navigator-owned instance.
- An unresolved persisted path remains as a broken navigator entry. The user can navigate back
  until a valid node is reached.
- Layout persistence stores logical paths and stable presentation configuration only. It never
  stores domain values, runtime handles, node instances, edit drafts, subscriptions, or running
  operations.
- Runtime handles, optional stable domain identity, and logical paths remain distinct concepts.
- Collections are read in bounded windows.
- Expected failures use structured results. Unexpected faults remain available only for trusted
  diagnostics.
- Domain thread affinity is explicit through `IInteractionDispatcher`.

## Architectural Invariants

1. Live domain objects remain authoritative.
2. Cycles, shared references, replacement, nulls, and unavailable targets are normal graph states.
3. Node semantics describe the exposed language feature, not a widget or interaction archetype.
4. A node instance belongs to one resolved navigator occurrence and is never shared between
   navigators.
5. Navigation state belongs to `Navigator`; domain access and invocation lifetime belong to
   `UIEngineHost`.
6. Core contains no frontend or toolkit types.
7. Frontends branch on structured failures rather than parsing messages.
8. Every public collection read is bounded.
9. Observation overflow is visible, and subscriptions detach deterministically.
10. Persisted layouts contain addresses and stable presentation configuration, not live state.

## Current Delivery State

The host already provides live graph access, canonical paths, identity, bounded collections,
validation, observation, dispatch, and synchronous or asynchronous invocation. The CLI exercises
those capabilities. The TUI has a reusable hosting boundary and independent frontend operation
scopes.

The next architecture work is the `BaseNode`, `UIEngineWorkspace`, and `Navigator` model,
followed by the multi-navigator TUI.

## Repository Structure

- `Core/` — host, exposure, identity, paths, nodes, workspaces, and navigation.
- `Frontend/Cli/` — command-line frontend.
- `Frontend/Tui/` — reusable XenoAtom-based TUI frontend.
- `Examples/CyclicDomain/` — cyclic and shared-reference acceptance model.
- `Examples/CyclicWorld.Tui/` — thin TUI composition executable.
- `Tests/` — black-box Core, CLI, and TUI behavior tests.
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
- Never over-design, and be cautious when applying design patterns. 
- `Task`s do not accept cancellation tokens; stopping a task is beyond our scope.
- In general, files should be within 500 lines. If you think it qualifies being longer than that, discuss it with me. Long files are usually the symbol of bad design. Test files and configurations are exceptions to this. 
- If classes/structs are wrappers/converters of other ones, think twice if that's necessary. Be extremely cautious of applying factory design pattern. Ask me if you really need to.
