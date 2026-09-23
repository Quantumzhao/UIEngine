# Method-Node Invocation Ownership

**Status:** Accepted, not yet implemented  
**Date:** 2026-09-23  
**Decision:** Invocation observation belongs to the method-node occurrence, not to
`UIEngineHost`.

## Context

The current implementation creates one public `ActionInvocation` for every successful call to
`IMethodNode.Invoke`. `UIEngineHost` retains every unfinished invocation in a concurrent
collection. Completion removes it from that collection, while host disposal completes every
retained invocation with a `DISPOSED` failure.

That model gives an invocation a longer observation lifetime than the method node that started it.
A frontend can remove the method control and its navigation entry while the host continues to own
and update the invocation object. It also makes the host responsible for a presentation-facing
piece of state: whether a particular call is running, succeeded, or failed and what result it
produced.

This ownership is broader than required. The host must resolve the target and provide the live
operation boundary, but it does not need to retain every asynchronous operation started through
that boundary.

## Decision

Each resolved method-node occurrence owns the observable state of the invocation it starts.
`UIEngineHost` will not retain, enumerate, complete, or otherwise manage running invocations.

The following rules define the new lifetime:

1. A method node starts a domain method and records that call's status, completion signal, and
   structured result as state of that node occurrence.
2. The underlying domain `Task` may continue after the method node's navigation entry or frontend
   control is removed.
3. Removing the node does not cancel the domain task. Cancellation remains outside UIEngine's
   scope.
4. Once the frontend releases the node and detaches its completion continuation, it deliberately
   abandons the ability to present that invocation's eventual status or result.
5. Disposing `UIEngineHost` does not manufacture a `DISPOSED` result for work that has already
   started. That work completes according to the domain task's actual outcome.
6. Different resolved occurrences of the same reflected method have independent invocation state.
   Removing one occurrence cannot affect another.
7. Invocation state is transient. It is never placed in a logical path, navigator state, or layout
   snapshot.

This is observation ownership, not execution ownership. After reflection has returned a domain
`Task`, the domain task owns its own execution. The node only observes and presents its outcome.

## Meaning of Node Disposal

`BaseNode` does not currently implement `IDisposable`, and this decision does not add that
interface. In this document, disposing a method node means that its owning navigation entry or
frontend control is disposed and releases the node occurrence.

The frontend must detach any continuation that would update the removed visual tree. The public
node wrapper can then become collectible. The Core continuation may temporarily retain its
internal method binding while recording the domain task's outcome; the binding becomes collectible
after that continuation finishes. No host registry keeps either object alive.

If explicit node disposal is introduced later for another reason, it should abandon observers and
presentation state only. It must not attempt to cancel an already-started domain task.

## Public Contract

`ActionInvocation` will be removed. It is a second lifetime object for state that is now owned by
the method-node occurrence.

`IMethodNode` should expose the latest invocation directly. The target contract is:

```csharp
public interface IMethodNode : IMemberNode
{
    IReadOnlyList<MethodParameter> Parameters { get; }

    Type? ResultType { get; }

    bool IsAsynchronous { get; }

    InvocationStatus? Status { get; }

    Task? Completion { get; }

    InteractionResult<object?>? Result { get; }

    InteractionResult<InvocationStatus> Invoke(
        IReadOnlyDictionary<string, object?> arguments);
}
```

The properties have these meanings:

| Member | Meaning |
|---|---|
| `Status` | `null` before the first accepted invocation, then `RUNNING`, `SUCCEEDED`, or `FAILED` |
| `Completion` | `null` before invocation; otherwise a signal that completes when the current invocation reaches a terminal state |
| `Result` | `null` before and during invocation; after completion, the method value or a structured invocation failure |
| `Invoke` | Validates and starts the call, returning the state reached before `Invoke` returns |

`Completion` is only a notification signal. It must complete successfully even when the domain
method fails; the structured outcome is read from `Result`. This prevents domain exceptions from
escaping through two different channels.

An invocation rejected before the domain method starts—for example, because the target is
unavailable or an argument fails validation—is returned as a failed `Invoke` result and does not
replace the node's prior invocation state.

For a synchronous method, `Invoke` sets `Status` to `RUNNING` before entering the domain method and
returns `SUCCEEDED` or `FAILED`. Its `Completion` is already complete when `Invoke` returns. Setting
`RUNNING` first preserves the existing reentrant behavior in which the invoked domain method can
observe that it is running.

For an asynchronous method whose returned task is incomplete, `Invoke` returns `RUNNING`; the node
later transitions when the domain task completes. If the returned task is already complete, the
node processes that outcome immediately and `Invoke` may return a terminal status.

### Invocation Concurrency

One method-node occurrence supports one invocation at a time. Calling `Invoke` while its status is
`RUNNING` returns `InteractionErrorCode.UNAVAILABLE` and does not start another call.

After completion, the node may be invoked again. The new accepted call replaces `Completion`,
`Result`, and `Status`. This restriction keeps the node-owned state unambiguous. Callers that need
parallel calls can resolve separate method-node occurrences, which is already the isolation model
used by independent navigators.

## Internal Design

`MethodNodeBinding` remains responsible for target resolution, argument conversion, validation,
reflection, and task observation. Its invocation fields should become the complete state rather
than a reference to an `ActionInvocation`:

```text
MethodNodeBinding
├── invocation gate
├── current status
├── current completion source
└── current structured result
```

All state transitions occur under the invocation gate, except that continuations must be released
outside the gate. Each accepted invocation creates a new completion source with
`RunContinuationsAsynchronously`.

The transition sequence is:

```text
never run
   |
   | accepted Invoke
   v
RUNNING + incomplete Completion + null Result
   |
   +---- domain return ----> SUCCEEDED + successful Result
   |
   +---- domain fault -----> FAILED + structured failure Result
```

Terminal completion must be idempotent. Although the normal path completes once, idempotence
protects against accidental double completion while keeping frontend continuations deterministic.

`LiveMethodNode` continues to delegate to `MethodNodeBinding`; it exposes the binding's new
`Completion` and `Result` properties in addition to the existing metadata and status.

No invocation members are added to object, value, reference, or collection nodes. Their operations
remain synchronous and continue to return their existing `InteractionResult<T>` contracts.

Domain exceptions must still be caught. `UnauthorizedAccessException` maps to
`PERMISSION_DENIED`; other domain exceptions map to `FAULT`. Whether `FAULT` outcomes are also
written to the configured diagnostic logger remains a host service call made at completion time,
not host ownership or tracking of the invocation. The logger must not influence completion.

## Host Changes

Remove the following from `UIEngineHost`:

- `_Invocations` and its `ConcurrentDictionary` dependency;
- `CreateInvocation()`;
- `_InvocationCompleted(ActionInvocation)`; and
- the invocation loop in `Dispose()`.

Host disposal will continue to clear roots and runtime handles. It will neither wait for nor alter
already-started domain tasks. A method invocation that has not yet started still fails normally if
its target can no longer be resolved.

Removing the registry also means the host has no shutdown barrier for domain tasks. Applications
that require graceful domain-task shutdown must implement that policy in the domain model before
disposing the host.

## Frontend Changes

Frontends should follow this sequence:

1. Call `Invoke` and present any immediate validation or target-resolution failure.
2. Read the node's `Completion` after a successful start.
3. Await that completion only while the node's control remains active.
4. After completion, read `Status` and `Result` from the same node occurrence.
5. When the control is removed, detach or invalidate its UI continuation and release the node.

The CLI may retain its resolved method node until `Completion` finishes because the `call` command
is explicitly waiting to print a result. The TUI must guard its completion continuation with the
control or navigation-entry lifetime so a removed control is never updated.

## Implementation Plan

### 1. Move invocation state into the method binding

- Add status, completion-source, and result fields to `MethodNodeBinding`.
- Reject a new invocation while the current invocation is running.
- Set the running state before invoking reflected domain code.
- Complete synchronous returns and faults before `Invoke` returns.
- Observe asynchronous tasks and transition the node on their natural completion.
- Preserve parameter conversion, default values, nullability, range checks, DataAnnotations, and
  reflected exception unwrapping.

### 2. Change the node contract

- Add `Completion` and `Result` to `IMethodNode`.
- Change `Invoke` to return `InteractionResult<InvocationStatus>`.
- Forward the new members from `LiveMethodNode`.
- Remove the public `ActionInvocation` type after all consumers migrate.
- Keep `InvocationStatus` as the shared state enum.

### 3. Remove host ownership

- Delete the host invocation registry and completion callback.
- Stop completing running method work from `UIEngineHost.Dispose()`.
- Retain fault-to-diagnostic reporting as a non-owning service if required by the existing
  diagnostics contract.
- Remove unused concurrent-collection imports.

### 4. Adapt frontends

- Update the CLI to await `method.Completion` and then read `method.Result`.
- Update method controls to treat completion as occurrence-local state.
- Ensure removal and back navigation detach UI update continuations without touching the domain
  task.
- Present `UNAVAILABLE` when the same node is invoked again while already running.

### 5. Replace tests

Add or update tests proving that:

- status is `null` before the first invocation;
- synchronous code observes `RUNNING` reentrantly and returns with terminal state;
- asynchronous success records its value on the originating node;
- asynchronous and synchronous faults become structured node results;
- a second invocation on the same running node is rejected without starting domain work;
- independently resolved method nodes can invoke the same domain method concurrently;
- releasing a navigation entry or frontend control does not cancel the domain task;
- a detached frontend does not apply the later result;
- disposing the host neither completes nor rewrites an already-started invocation;
- natural completion still occurs after host disposal;
- invocation state is absent from workspace layout serialization; and
- the public API no longer exports `ActionInvocation`.

Delete the test that expects host disposal to complete a running invocation with `DISPOSED` and
replace it with the natural-completion behavior above.

### 6. Align governing documentation

The implementation must update statements that currently describe invocations as host-owned in:

- `PROJECT_CONTEXT.md`;
- `docs/architecture.md`;
- `docs/tui-frontend-architecture.md`;
- `REBOOT_PLAN.md`; and
- the relevant milestone and progress documents.

Completed milestone documents should retain historical accuracy where useful, but their lifetime
claims must be marked as superseded by this decision if they remain visible as current guidance.

## Compatibility and Risks

This is a breaking public API change because `IMethodNode.Invoke` changes return type and
`ActionInvocation` is removed.

The main behavioral change is host disposal. A caller can no longer use host disposal as a signal
that every invocation observer has completed. This is intentional: disposing a graph-access host
does not control domain task lifetime.

The single-running-call rule is also stricter than the current implementation. It avoids silently
losing or misattributing results when all observable state belongs to one node occurrence. Parallel
work remains available through separately resolved occurrences.

The asynchronous continuation temporarily retains the method binding until the domain task
finishes. This is necessary to observe the task safely, but it is not a registry or an externally
reachable lifetime. A domain task that never completes may therefore retain its binding; UIEngine
will not attempt to cancel or forcibly release it.

## Non-Goals

- Cancelling or stopping domain tasks.
- Waiting for domain tasks during host, workspace, navigator, or frontend disposal.
- Persisting invocation state.
- Sharing invocation state between method-node occurrences.
- Adding progress streaming or task history.
- Adding `IDisposable` to `BaseNode` solely for method invocation.

## Resulting Ownership Summary

```text
UIEngineHost
└── roots, handles, resolution, and live-operation boundary

Method-node occurrence
└── latest invocation status, completion signal, and structured result

Domain Task
└── execution lifetime, independent after it has started

Frontend control
└── presentation and detachable completion continuation
```

The host enables a method invocation but does not own it. The node observes it while that node
occurrence remains relevant, and discarding the occurrence knowingly discards its presentation
state without changing the domain operation.
