# TUI Frontend Simplification

**Status:** Implemented on 2026-09-22.

## Objective

UIEngine frontends should primarily choose controls that represent Core descriptors and bind those
controls to Core operations. The workspace and its navigation pages should own the small amount of
shared and page-local lifetime needed by the TUI without creating a second interaction
architecture.

Official examples must demonstrate this simplicity. Most source files should remain below roughly
200 lines, while a complete control and its bindings should normally remain within roughly 300
lines.

## Previous Problem

`TuiSession` concentrated intent queuing, task ownership, cancellation generations,
subscription and invocation lifetime, stale-result rejection, dispatcher marshalling, and
presentation callbacks. This infrastructure already dominated the TUI implementation before the
interactive controls existed.

`TuiIntent` also duplicated operations already expressed by Core descriptors. In particular,
`ReadPathIntent` combines a semantic operation with an execution policy and an
`Action<InteractionResult<ResolvedPath>>` presentation callback. Generalizing its name would not
make that coupling modality-independent.

One global operation generation was also too broad for the planned frontend. Navigation, individual
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

## Workspace and Navigation Pages

`TuiWorkspace` is the single public frontend lifetime. It retains the caller-supplied host without
owning it, copies the options, owns the root visual and operation scope, and will own the current
page plus bounded back/forward stacks when navigation is implemented.

A `NavigationPage` owns one `LogicalPath`, its corresponding object/member control, local
presentation state, and a child operation scope. The workspace does not duplicate a separate
current path; it reads the current page's path. Going up derives a parent path, while back and
forward select pages that retain their individual paths.

Inactive pages retain only inexpensive control state. They stop reads, observations, and progress
readers, then create a fresh scope and re-resolve their path when activated again. This prevents
history from keeping stale descriptors or background work alive.

## Intent and Operation Lifetime

The provisional `TuiIntent` layer was removed because refactoring revealed no concrete need that
direct descriptor calls could not satisfy. Core operations already express the semantic requests,
and different frontend modalities require different scheduling policies.

The reusable abstraction worth considering is a small operation scope. Such a scope may start and
own asynchronous work, expose cancellation and completion, reject stale results within that scope,
and join owned work during disposal. It must not know why an operation was started or how its result
is presented.

Independent controls should receive independent scopes. For example, navigating away may retire a
detail scope, while starting a collection read must not interrupt an unrelated progress reader.
Scope cancellation is strictly a frontend lifetime mechanism: retiring an action view stops its
readers but never requests cancellation of the underlying domain invocation. The minimal TUI does
not keep a background invocation registry, replay detached progress, or provide resume, undo, or
action-cancellation controls.

## Implemented Outcome

- The original focused tests characterized cancellation, stale-result, dispatch, and cleanup
  behavior before the refactor.
- `TuiOperationScope` now provides hierarchical workspace, page, control, and operation lifetimes.
  Each scope independently owns cancellable frontend work and observations, joins its tasks on
  disposal, and can be retired without invalidating sibling work or domain actions.
- `TuiWorkspace` directly retains the caller-owned host reference, immutable options, root visual,
  and root shared lifetime; the redundant `TuiSession` layer was removed.
- Focused tests call Core directly, marshal state changes with the toolkit dispatcher, and cover
  independent frontend cancellation plus deterministic read, observation, and progress-reader
  cleanup while proving that a started invocation continues.
- The workspace facade, fullscreen/embedded construction, and thin cyclic-world composition host
  remain unchanged in shape and continue to preserve caller ownership of `UIEngineHost`.

Subsequent browser, value, collection, and action features should be self-contained control and
binding units over these scopes.
