# UIEngine Architecture, Reboot Audit, and Plan

> **Status:** The reboot and subsequent Core streamlining are complete. This document preserves
> the architectural rationale, migration history, current design, and future roadmap. For exact
> implemented APIs, see [docs/architecture.md](docs/architecture.md).

## 1. Executive Assessment

UIEngine began as a tree-oriented reflection prototype centered on global `Dashboard` and `Node`
state. That prototype proved that live domain objects could drive generated tooling, but its static
ownership, recursive wrappers, eager collection behavior, and unstructured invocation model were
not a safe foundation for cyclic graphs or multiple frontends.

The reboot established a host-scoped graph runtime, frontend-neutral semantic descriptors,
structured failures, stable runtime identity, and a proving CLI. Framework hardening then added
domain identity, replacement-safe paths and bindings, dispatch, validation, bounded collections,
observation, and asynchronous invocation.

The first hardened implementation over-generalized those capabilities into sixteen public
interfaces, three parallel descriptor implementations, a mutable 800-line exposure registry, and
several speculative provider and policy layers. Because the framework remained unpublished, the
Core was intentionally streamlined through a clean API break. The current runtime preserves all
demonstrated workflows with one concrete descriptor hierarchy and only two public extension
interfaces.

## 2. Current Repository Baseline

- Target framework: .NET 10.
- Projects: `Core`, `Frontend/Cli`, `Examples/CyclicDomain`, and two test projects.
- Core size after streamlining: approximately 3,950 C# lines, down from approximately 8,320.
- Public extension interfaces: `IInteractionDispatcher` and `IStableDomainIdentity`.
- Builds: Debug and Release complete with zero warnings and errors.
- Tests: 22 black-box behavior tests pass—14 framework and 8 CLI tests.
- Packaging: disabled; normal builds do not create a package.
- Legacy: the v0.2.3 project, binary assets, and characterization project have been retired from
  the active repository. Git history remains the recovery path.

## 3. Product Definition

UIEngine exposes a running .NET application's live domain model as an interactive object graph.
It supports navigation, inspection, mutation, invocation, and observation without requiring an
application-specific UI model.

The framework targets simulations, engines, services, research systems, internal tools, and
prototypes. Domain objects remain authoritative. Frontends retain handles, paths, bindings, and
descriptors—not recursively copied state.

### Objectives

- Handle cyclic and shared-reference graphs without recursive expansion.
- Expose semantic values, references, collections, and actions independently of UI widgets.
- Keep roots, identity, configuration, subscriptions, and invocation lifetime host-scoped.
- Return structured failures for expected interaction outcomes.
- Support thread-affine domains through explicit dispatch.
- Bound collection work and asynchronous streams.
- Prove frontend independence through a CLI using only Core APIs.

### Non-goals for the current framework

- Persistent layouts or workbenches.
- Arbitrary scripting or expression execution.
- Automatic rollback for domain side effects.
- Remote object serialization or unauthenticated remote control.
- A public provider ecosystem without concrete production consumers.
- Paging continuations, custom observation adapters, or programmatic non-scalar roles.
- A package compatibility promise before release policy is defined.

## 4. Architectural Principles

1. **Live domain authority:** descriptors operate on current objects rather than mirrored nodes.
2. **Graph semantics:** cycles, shared references, replacement, nulls, and unavailable targets are
   normal runtime states.
3. **Separated identity:** `ObjectHandle`, optional `DomainIdentity`, and `LogicalPath` solve
   different problems.
4. **Semantic descriptors:** Core describes roles and operations, never controls or widgets.
5. **Host scope:** no global registries or process-wide mutable runtime state.
6. **Structured failures:** frontends branch on codes and issue records, not rendered messages.
7. **Explicit affinity:** all live access flows through the host dispatcher.
8. **Bounded work:** collection reads and event streams have explicit bounds.
9. **Deterministic lifetime:** hosts and subscriptions detach and terminate owned work.
10. **Evidence before extension:** new seams require a consumer and acceptance test.

## 5. Reboot Audit and Resolutions

### Legacy prototype findings

| Finding | Reboot resolution |
|---|---|
| Static `Dashboard` state made hosts interfere | `UIEngineHost` owns all runtime state. |
| Recursive `Node` trees could not represent cycles safely | Reference-based handles and lazy graph traversal. |
| UI-shaped wrappers mixed discovery and presentation | Frontend-neutral semantic descriptors. |
| Collection enumeration could be eager and unbounded | Explicit bounded slices. |
| Reflection invocation leaked CLR exceptions and argument details | Conversion, validation, invocation lifecycle, and structured results. |
| Extension-expression nodes expanded scope without a product workflow | Removed; constrained batch work remains deferred. |

### Hardened-Core over-design findings

| Finding | Streamlined resolution |
|---|---|
| Reflection, programmatic exposure, and dispatch decorators repeated operations | One live concrete descriptor implementation shared by discovery paths. |
| Sixteen public interfaces exposed unused seams | Retain only dispatch and stable domain identity interfaces. |
| `ObjectIdentity` and `ObjectHandle` wrapped the same identifier | One `ObjectHandle(Guid Id)`. |
| Mutable `ExposureRegistry` supported speculative roles and factories | Immutable `TypeExposure<T>` and scalar `ValueExposure<T,TValue>`. |
| Four member collections duplicated metadata | `ObjectDescriptor.Members` with concrete subclasses. |
| Capability flags and collection modes modeled unused paging behavior | One `ReadAsync(offset, limit)` returning `CollectionSlice`. |
| Nullable collection-entry fields allowed invalid combinations | Closed null, scalar, and reference entry records. |
| Resolvers duplicated state objects and error mappings | Direct `InteractionResult<ResolvedPath>` and `InteractionResult<ResolvedBinding>`. |
| Multiple binding fallback policies exceeded the demonstrated workflow | Optional identity recovery plus a required path and conflict refusal. |
| Observation adapters, modes, and dual disposal contracts duplicated buffering | Notifications or explicit polling, synchronous subscription disposal, shared stream primitive. |
| Invocation wrappers exposed redundant states, timestamps, and fault types | Concrete `ActionInvocation`, four statuses, trusted exception, and `bool Cancel()`. |
| Presentation metadata had no frontend requirement | Retain nullability, defaults, enum options, and ranges only. |
| Twenty-eight error codes made every resolver translate states | Twelve stable error categories plus validation issues. |
| Expected failures and successes generated excessive diagnostics | Log unexpected discovery, dispatch, observation, and invocation faults only. |
| Repeated `ValueTask`, lifecycle checks, and exception mapping obscured operations | Public `Task` APIs and centralized host execution. |
| Runtime guards duplicated nullable contracts | Retain semantic checks; remove redundant non-null guards. |
| CLI locks and async disposal protected no concurrent asynchronous resources | Serial command handling and synchronous disposal. |
| Tests preserved implementation choices | Black-box public runtime and CLI behavior tests. |

## 6. Current Runtime Architecture

### Host composition

`UIEngineHost` is the composition and lifetime boundary. Its optional `UIEngineHostOptions`
contains immutable scalar exposures, an interaction dispatcher, collection and stream capacities,
and logging configuration. The host copies settings internally.

Roots use `SetRoot`, `RemoveRoot`, and `Roots`. Replacing a root changes runtime identity while
compatible domain identity and canonical paths allow safe rebinding.

### Descriptor model

`ObjectDescriptor` exposes:

- `ObjectHandle Handle`;
- optional `DomainIdentity`;
- CLR `TypeName`;
- optional `Summary`; and
- `IReadOnlyList<MemberDescriptor> Members`.

`MemberDescriptor` has `Id` and `MemberKind`. Its sealed runtime roles are
`ValueDescriptor`, `ReferenceDescriptor`, `CollectionDescriptor`, and `ActionDescriptor`.
`ActionParameter` is a separate record because action inputs are not object members.

Each descriptor holds the host and owning handle. Live reads, writes, reference resolution,
collection reads, and calls therefore share target resolution, dispatch, cancellation, validation,
and error normalization.

### Exposure

Reflection is built into the host. `[Expose]`, `[Children]`, `[Action]`, `[Summary]`, and
`[DomainIdentitySource]` opt members into discovery. `[Action]` is a marker.

Unannotated third-party scalar types use immutable `TypeExposure<T>` instances containing
`ValueExposure<T,TValue>` definitions and optional identity and summary delegates. Programmatic
references, collections, actions, custom validation, factories, and post-construction mutation are
deliberately absent until a real consumer requires them.

### Identity, paths, and bindings

`ObjectHandle(Guid Id)` identifies a reference within one host. `DomainIdentity` is an optional
semantic identity that can survive replacement. `LogicalPath` describes a route through exposed
semantics.

Paths are absolute, case-sensitive, and percent-escaped. Collections use canonical `index`, `key`,
or `identity` selectors. The temporary `/Collection/0` alias is not supported.

Path and binding resolution are methods on the host. A binding contains its required object path,
member identifier, expected kind, and optional domain identity. Resolution may recover an
unavailable path through a known identity, but refuses a path that resolves to a conflicting
identity.

### Values and validation

Value writes and action arguments apply invariant conversion followed by nullability, enum-option,
range, and DataAnnotations validation. Expected failures use `InteractionResult<T>` and one of:

- invalid input;
- not found;
- unavailable;
- ambiguous;
- type mismatch;
- unsupported;
- conversion failed;
- validation failed;
- permission denied;
- cancelled;
- disposed; or
- unexpected fault.

Validation issues retain the member or parameter ID.

### Collections

All collection access uses `ReadAsync(long offset, int limit, CancellationToken)`. A
`CollectionSlice` contains the offset, bounded entries, optional total count, and `HasMore`.
Entries are one of `NullCollectionEntry`, `ScalarCollectionEntry`, or
`ReferenceCollectionEntry`, with position and optional key on the base record.

Arrays, lists, dictionaries, observable collections, and bounded scans over lazy enumerables use
the same contract. Selector lookup remains an internal path-resolution operation.

### Actions and invocations

Reflection supports synchronous instance methods and `Task`/`Task<T>` methods. Optional trailing
`IProgress<T>` and `CancellationToken` parameters are framework-supplied. `ValueTask`, `ref`,
`out`, open generics, and ambiguous infrastructure signatures are rejected.

`ActionInvocation` exposes running, succeeded, failed, and cancelled status; one completion
result; bounded ordered progress; `bool Cancel()`; and the underlying exception for trusted local
diagnostics. Host disposal deterministically claims the disposed result before signalling the
domain cancellation token.

### Observation

`ObserveAsync` uses built-in `INotifyPropertyChanged` and `INotifyCollectionChanged` support unless
a positive polling interval explicitly selects polling for one readable value or reference.
`ObservationSubscription` uses synchronous disposal and detaches handlers deterministically.

Invocation progress and observations share one internal bounded stream. Observation overflow is
visible as `BUFFER_OVERFLOW` with a dropped-record count.

### Dispatch and diagnostics

`IInteractionDispatcher` is the only execution-context seam. The inline implementation is the
default. Host execution centralizes disposal, cancellation, dispatch, and exception normalization,
including reentrant access.

Structured operation failures are the frontend contract and are not logged. Logging is reserved
for unexpected discovery, dispatch, observation, and invocation faults. Sensitive exception
details require explicit opt-in.

## 7. Frontend Boundary

The CLI owns tokenization, command arity, current-location presentation, completion, and text
formatting. Core owns path grammar, binding recovery, conversion, validation, dispatch, collection
bounds, observation, and invocation lifecycle.

The proving commands are `ls`, `cd`, `inspect`, `get`, `set`, `call`, `watch`, and `exit`.
Redirected sessions use the same `CliSession` as interactive PrettyPrompt sessions.

## 8. Migration Record

### Phase 0 — Safety and baseline

Completed: credential revocation, packaging removal, nullable/analyzer enablement, CI, and clean
build/test baseline.

### Milestone 01 — Architecture runway and CLI walking skeleton

Completed: host-scoped graph runtime, opt-in reflection, semantic descriptors, structured results,
cyclic fixture, and initial CLI vertical slice. See the
[historical outline](docs/milestones/01-architecture-runway-cli-walking-skeleton.md).

### Milestone 02 — Framework MVP interaction hardening

Completed: domain identity, canonical paths and bindings, dispatch, observation, bounded
collections, validation, asynchronous actions, progress, cancellation, and complete CLI workflow.
See the [historical outline](docs/milestones/02-framework-mvp-interaction-hardening.md).

### Core streamlining

Completed: removal of speculative interfaces/providers/policies, consolidation into concrete
descriptors, immutable scalar exposure, simplified errors and lifetimes, black-box test rewrite,
legacy retirement, and documentation reconciliation.

## 9. Verification Strategy

Framework tests cover:

- annotated properties and fields, nullable metadata, enums, ranges, DataAnnotations, conversion,
  and read-only values;
- runtime/domain identity, cycles, shared references, replacement, paths, and binding recovery;
- arrays, dictionaries, lazy sequences, entry variants, bounds, and selectors;
- synchronous and asynchronous actions, progress, cancellation, faults, and host disposal;
- property/collection notifications, replacement, polling, overflow, and handler detachment;
- dispatch routing, reentrancy, queued cancellation, and disposal; and
- public API shape so removed seams do not return.

CLI tests cover every command, completion, structured failures, cyclic navigation, compatible
replacement, redirected observation, progress, cancellation, and the end-to-end world workflow.

Repository acceptance requires:

```sh
dotnet build UIEngine.sln
dotnet test UIEngine.sln --no-build
```

The build must have zero warnings. A redirected CLI session must cover navigation, mutation,
bounded collection reading, and asynchronous invocation; redirected CLI tests cover observation
with deterministic external mutation and cancellation.

## 10. Future Roadmap

### Product MVP — TUI

- Select a toolkit without coupling it to Core.
- Build graph browsing with history or breadcrumbs.
- Generate value editors and action forms from concrete descriptors.
- Present bounded collections, progress, cancellation, observation, and structured failures.
- Prove the same cyclic-world workflow as the CLI.

### Post-MVP layouts

- Persist versioned component configuration and binding records, not domain state.
- Represent broken bindings explicitly and provide repair workflows.
- Keep geometry and visual composition frontend-owned.

### Post-MVP batch operations

- Snapshot targets and use predefined member filters.
- Preview compatibility before mutation.
- Execute sequentially by default with explicit stop/continue policy.
- Record one terminal result per target; do not promise rollback for arbitrary side effects.

### Later capabilities

- Source generation and an explicit AOT compatibility matrix.
- Search and indexing.
- Tables, charts, and safe derived values.
- Authenticated remote protocol and audit behavior.
- Additional frontends justified by real users.

## 11. Risks and Guardrails

- Do not reintroduce a provider/factory interface merely to make construction look abstract.
- Do not add presentation metadata before a frontend consumes it.
- Do not add paging, continuation, or custom adapter contracts without bounded acceptance fixtures.
- Do not treat a runtime handle as durable identity or a path as object identity.
- Do not let ordinary browsing enumerate an unknown sequence without a host bound.
- Do not expose domain exceptions as the expected control protocol.
- Do not publish a package before versioning, licensing, support, and compatibility policies exist.

## 12. Success Criteria

The current Framework MVP is successful when:

- a cyclic/shared-reference model can be navigated without recursive expansion;
- values can be inspected, converted, validated, and mutated live;
- bounded collection slices preserve positions, keys, nulls, scalars, and references;
- replacement-safe paths and bindings refuse conflicting identities;
- sync and async actions share progress, cancellation, and terminal-result behavior;
- notifications and polling detach deterministically and expose overflow;
- dispatch applies to all live operations and supports reentrancy;
- the CLI proves every Core workflow without owning runtime semantics;
- only evidence-backed public seams remain; and
- Debug/Release builds and all black-box tests pass with zero warnings or errors.
