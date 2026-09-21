# TUI Frontend Simplification

## Objective

UIEngine frontends should primarily choose controls that represent Core descriptors and bind those
controls to Core operations. A frontend may add a session manager for richer coordination, but it
should not need a second interaction architecture.

Official examples must demonstrate this simplicity. Most source files should remain below roughly
200 lines, while a complete control and its bindings should normally remain within roughly 300
lines.

## Current Problem

`TuiSession` currently concentrates intent queuing, task ownership, cancellation generations,
subscription and invocation lifetime, stale-result rejection, dispatcher marshalling, and
presentation callbacks. This infrastructure already dominates the TUI implementation before the
interactive controls exist.

`TuiIntent` also duplicates operations already expressed by Core descriptors. In particular,
`ReadPathIntent` combines a semantic operation with an execution policy and an
`Action<InteractionResult<ResolvedPath>>` presentation callback. Generalizing its name would not
make that coupling modality-independent.

One global operation generation is also too broad for the planned frontend. Navigation, individual
editors, collection windows, observations, and action invocations have different lifetimes. A new
operation in one area must not automatically cancel unrelated work elsewhere.

## Agreed Direction

The primary frontend boundary remains the existing semantic API:

```text
Frontend controls and native bindings
                |
UIEngine descriptors and operations
                |
        Live domain objects
```

- Controls call `ResolvePathAsync`, descriptor read/write methods, bounded collection reads,
  `InvokeAsync`, and `ObserveAsync` directly.
- Each value, reference, collection, or action control keeps its toolkit state, bindings, drafts,
  validation presentation, and event handling together.
- Frontends use their toolkit's native state, command, binding, focus, layout, and dispatcher
  facilities. UIEngine does not impose a universal presentation loop.
- Core remains authoritative. Frontend state contains only interaction and presentation state and
  does not mirror the domain model.

## Optional Session Manager

A comprehensive official example will include an optional session manager. It demonstrates
navigation history, operation cancellation, observation lifetime, stale-result protection, and
deterministic shutdown, while remaining completely decoupled from UI controls.

The session may own:

- the caller-supplied host reference without taking ownership of the host;
- current logical navigation state and history;
- session and operation cancellation scopes;
- frontend-started task, subscription, and invocation lifetime; and
- plain interaction status or immutable state snapshots.

It must not own or reference:

- XenoAtom controls, visuals, bindings, focus, layout, or key gestures;
- presentation callbacks stored in queued requests;
- toolkit dispatcher selection;
- control-specific edit drafts or display formatting; or
- one global generation that invalidates every active feature.

If the session contains no TUI policy and is useful to other frontends, it should be named
`InteractionSession`. If it retains TUI-specific workflow without referencing controls, the name
`TuiSession` remains appropriate.

## Intent and Operation Lifetime

The current `TuiIntent` layer should be removed unless refactoring reveals a concrete need that
direct descriptor calls cannot satisfy. Core operations already express the semantic requests, and
different frontend modalities require different scheduling policies.

The reusable abstraction worth considering is a small operation scope. Such a scope may start and
own asynchronous work, expose cancellation and completion, reject stale results within that scope,
and join owned work during disposal. It must not know why an operation was started or how its result
is presented.

Independent controls should receive independent scopes. For example, navigating away may retire a
detail scope, while starting a collection read must not cancel an unrelated action invocation.

## Refactor Starting Point

1. Characterize the current lifetime guarantees with the existing focused tests.
2. Separate presentation dispatch and callbacks from session-owned operations.
3. Replace the global generation with explicit session, screen, and control/operation scopes.
4. Remove `TuiIntent` and invoke Core operations directly through those scopes.
5. Keep the workspace facade and thin cyclic-world host unchanged where possible.
6. Build subsequent browser, value, collection, and action features as self-contained control and
   binding units.

The refactor should remain small and incremental. It should preserve caller ownership of
`UIEngineHost`, deterministic cleanup, stale-update protection, and the existing fullscreen and
embedded hosting paths after every step.
