# UIEngine Project Context

## Purpose

UIEngine exposes a running .NET application's live domain objects as a navigable, editable, and
invocable object graph. Core interaction semantics are frontend-neutral; the CLI is the proving
frontend for the current framework.

The framework targets simulations, engines, services, research systems, internal tools, and
prototypes whose domain model remains the source of truth.

## Current State

- Platform: .NET 10.
- Runtime: one concrete host and descriptor hierarchy with built-in reflection discovery.
- Programmatic exposure: immutable scalar definitions for unannotated third-party types.
- Identity: one host-scoped `ObjectHandle` plus optional stable `DomainIdentity`.
- Navigation: absolute canonical paths with `index`, `key`, and `identity` collection selectors.
- Operations: validated reads/writes, bounded collection slices, sync and `Task` actions,
  progress, cancellation, notification observation, and explicit polling.
- Dispatch: one optional `IInteractionDispatcher` controls domain thread affinity.
- CLI: navigation, inspection, mutation, collection reading, invocation, watching, and completion.
- Tests: black-box framework and CLI behavior suites.
- Packaging: disabled until release policy, compatibility, licensing, and support are defined.

## Settled Decisions

- Public compatibility with the unpublished prototype is intentionally not preserved.
- Reflection is an opt-in host behavior through attributes; applications do not order providers.
- `ObjectDescriptor.Members` is the only object-member collection. Concrete value, reference,
  collection, and action descriptors contain role-specific operations.
- Action parameters are metadata records, not object members.
- Programmatic exposure supports scalar values only until another role has a production consumer.
- Collections expose only bounded `ReadAsync(offset, limit)` and return a `CollectionSlice`.
- Paths are case-sensitive and percent-escaped. `/Collection/0` is not an alias for an index.
- Binding resolution may recover by domain identity when its path is unavailable, but must reject a
  path that resolves to a conflicting identity.
- Expected failures use `InteractionResult<T>`; exceptions are retained only for trusted action
  diagnostics or logged unexpected faults.
- Public asynchronous operations use `Task`. Host, CLI session, and observation subscription use
  synchronous disposal because they own synchronous resources.
- The only public extension interfaces are `IInteractionDispatcher` and
  `IStableDomainIdentity`.
- Presentation-only units, tags, risk, confirmation, capability flags, provider precedence, and
  continuation tokens are outside the current framework.

## Architectural Invariants

1. Live objects remain authoritative; descriptors do not mirror domain state.
2. Cycles, shared references, replacement, nulls, and unavailable targets are normal graph states.
3. Runtime handle, optional domain identity, and logical path remain distinct concepts.
4. Descriptors express domain roles rather than frontend widgets.
5. Hosts are isolated, disposable, and contain no global registry state.
6. Frontends branch on structured failures rather than parsing exception messages.
7. Domain thread affinity is explicit through the dispatcher.
8. Every public collection read is bounded.
9. Observation overflow is visible, and subscriptions deterministically detach handlers.
10. Speculative extension points are added only with a concrete consumer and an acceptance test.

## Repository Structure

- `Core/Core.csproj` — runtime, attributes, descriptors, reflection, binding, and observation.
- `Frontend/Cli/Cli.csproj` — proving executable and presentation logic.
- `Examples/CyclicDomain/CyclicDomain.csproj` — deterministic cyclic acceptance model.
- `Tests/UIEngine.Framework.Tests` — public runtime behavior tests.
- `Tests/UIEngine.Frontend.Cli.Tests` — CLI behavior and workflow tests.
- `REBOOT_PLAN.md` — architectural rationale, migration reconciliation, and roadmap.
- `TODO.MD` — current completion and future-work tracker.
- `docs/architecture.md` — implementation guide.
- `docs/cli-command-reference.md` — CLI grammar and examples.
- `docs/milestones/` — historical milestone plans and acceptance records; their provisional API
  shapes are not current contracts.

## Scope

The current framework includes the Core and CLI workflows demonstrated by the cyclic model. A
product TUI, persistent layouts, batch mutation, remote transport, scripting, source generation,
automatic undo, and a supported package are deferred until backed by concrete requirements.

## Verification

Run the smallest relevant project first, followed by the repository checks:

```sh
dotnet build UIEngine.sln
dotnet test UIEngine.sln --no-build
```

The build is expected to complete with zero warnings.
