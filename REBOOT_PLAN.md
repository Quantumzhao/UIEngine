# UIEngine Reboot Audit and Plan

**Status:** architecture runway and CLI walking skeleton complete; Framework MVP hardening next

**Legacy baseline:** UIEngine v0.2.3  
**Target framework:** .NET 10
**Primary design:** [runtime-domain-workbench-design.md](runtime-domain-workbench-design.md)

## 1. Executive Assessment

The repository contains a buildable proof of concept for reflecting over annotated .NET objects. It can discover selected properties and methods, expose their values through mutable wrapper nodes, invoke simple synchronous methods, wrap some collections, and demonstrate those behaviors through a small CLI.

That prototype validates the original idea, but it is not a safe foundation for incremental implementation of the UIEngine design. Its central abstraction is a mutable tree of `Node` objects that combines discovery, binding, navigation, invocation state, and presentation hints. The target design instead requires a graph-aware runtime with stable identity, semantic descriptors, structured operations, explicit bindings, and frontend independence.

The reboot will therefore make a clean API break. Useful concepts and domain fixtures will be retained, but the legacy `Dashboard`/`Node` API will not receive a compatibility adapter. The new core and a minimal CLI form the framework MVP; a TUI built on that foundation forms the product MVP.

This document records what exists, what does not, known correctness risks, settled product decisions, and the implementation sequence. [TODO.MD](TODO.MD) is the corresponding progress checklist.

## 2. Repository Baseline

| Area | Purpose | Current condition |
|---|---|---|
| `UIEngine/` | Legacy reflection and node library | Builds on .NET 10; retained as a characterized baseline |
| `CLITestProject/` | CLI plus a cyclic demographic model | Useful fixture source; CLI is incomplete |
| `Dataset/` | Additional annotated sample model | Despite its name, it contains no tests |
| `Framework/UIEngine.Framework/` | Reboot runtime contracts, host, and reflection provider | Milestone 01 implementation complete |
| `Framework/UIEngine.Frontend.Cli/` | Descriptor-driven proving frontend | Milestone 01 implementation complete |
| `Samples/UIEngine.Samples.CyclicDomain/` | Deterministic reboot fixture | Covers cyclic and shared-reference traversal |
| `Tests/` | Framework, CLI, and legacy-characterization tests | 29 tests pass in clean-checkout acceptance |
| `runtime-domain-workbench-design.md` | Target architecture and product specification | Authoritative design input |
| `README.MD` | Repository entry point | Describes current status without presenting planned APIs as shipped |
| `TODO.MD` | Progress tracker | Concise checklist derived from this plan |

The cleanup baseline intentionally had no package-generation configuration, private package feed, Docker profile, or repository-owned editor launch configuration. Its three existing projects originally targeted `net8.0`; the repository later advanced all projects together to `net10.0` as routine LTS maintenance. There was no automated test project at the cleanup baseline.

The removed package credential was revoked through its provider but remains present in pre-cleanup Git history. Rewriting published history is intentionally outside the scope of this cleanup.

## 3. Capability Audit

Status meanings:

- **Prototype:** a narrow happy path is demonstrable and worth learning from.
- **Partial:** code exists but does not meet the design contract or has correctness gaps.
- **Conflict:** the current abstraction contradicts a mandatory design property and should be replaced.
- **Absent:** no meaningful implementation exists.

| Design capability | Status | Evidence and conclusion |
|---|---|---|
| Opt-in exposure metadata | Prototype | `VisibleAttribute`, `ParamInfoAttribute`, `IVisible`, and runtime weak-table metadata expose selected members and descriptions. |
| Reflection discovery | Prototype | Static roots and nested public properties/methods can be discovered. Discovery is not represented as immutable semantic descriptors. |
| Live property reads and writes | Partial | Direct reflection supports simple values, but there is no conversion, validation, dispatching, permission model, or structured failure result. |
| Method invocation | Partial | Parameter nodes, synchronous invocation, and return wrapping exist. Async methods, defaults, nullability, validation, progress, cancellation, and reliable candidate selection do not. |
| Collections | Partial | Simple generic enumerables are eagerly materialized as nodes. Element mutation and several collection-change actions are broken or unimplemented. |
| State observation | Partial | Some `INotifyPropertyChanged` and `INotifyCollectionChanged` events are observed, but subscription lifetime and change coverage are unsafe. |
| Object graph and cycles | Conflict | `ObjectNode` explicitly requires a tree. Shared references receive separate wrappers, no visited-identity set exists, and cyclic expansion is not bounded by the runtime. |
| Stable identity | Absent | Generated node names are process-order artifacts, not runtime or domain identities. |
| Paths and durable bindings | Absent | Tree succession and string names do not implement logical paths, binding records, resolution states, or recovery. |
| Semantic descriptors | Absent | Nodes mix object state, reflection, navigation, invocation, and UI-oriented state rather than exposing frontend-neutral capabilities. |
| Frontend capabilities | Absent | There is no capability negotiation or component binding contract. |
| CLI frontend | Partial | The demo supports only a fragile subset of `show`, assignment, execution, and exit behavior; it is not a navigable descriptor client. |
| Product TUI frontend | Absent | No TUI project exists. Historical WPF component projects were removed in 2020 and will not be revived for the MVP. |
| Layout composition and persistence | Absent | There is no component model, layout schema, persistence, migration, or broken-binding UI. |
| Batch operations | Absent | Disabled extension-function experiments and methods throwing `NotImplementedException` do not provide compatibility preview or controlled execution. |
| Validation and error model | Absent | Expected failures generally surface as raw exceptions or warnings. |
| Threading and dispatch | Absent | Reflection reads, writes, and calls run directly on the caller's thread. |
| Diagnostics and logging | Absent | Warning events are not a structured diagnostic or audit system. |
| Automated verification | Absent | Neither `Dataset` nor `CLITestProject` is a test project. |

## 4. Correctness and Maintenance Risks

### Exposure and runtime ownership

- `Dashboard` stores roots in global static state, preventing isolated hosts, deterministic cleanup, and straightforward parallel tests.
- Exposure supports properties and methods only. It has no normalized registry for third-party types, contextual metadata, fields, or role-dependent exposure.
- Runtime metadata uses object-attached attributes, but there is no collision policy, replacement behavior, or host lifetime.
- Attribute metadata includes a delegate-shaped preview property that cannot be supplied through ordinary attribute syntax.

### Values and object references

- A null value is also used to mean “not loaded,” so nullability and load state are conflated.
- Reflection setters are invoked directly without conversion, domain validation, dispatcher use, or a structured result.
- Collection-element assignment searches for the new value after replacing the wrapper's old value, so it cannot reliably locate the source slot.
- Enum detection compares exact types incorrectly and does not reliably distinguish single- and multi-selection semantics.
- Reference properties create new wrapper subtrees instead of preserving shared object identity.

### Methods

- Invocation is synchronous and exposes reflection exceptions directly.
- `Task` and `ValueTask` results are treated as ordinary objects; progress and cancellation do not exist.
- Default values, optional parameters, nullability, `ref`/`out`, overload selection, and validation are not modeled.
- Candidate discovery tests the wrapper node's type rather than its contained domain value, producing incorrect compatibility results.
- Return nodes reuse parameter-oriented internals and do not have explicit immutable result semantics.

### Collections

- Collection types are assumed to have a usable first generic type argument; arrays, nongeneric enumerables, dictionaries, and unusual generic shapes are unsafe.
- Enumeration is eager, which can block, consume one-shot sequences, or materialize an unbounded source.
- Null elements cannot be wrapped safely.
- Add handling observes only the first item in a multi-item event and does not preserve the source index.
- Remove notifications do not consistently remove the mirrored node; replace and reset throw, and move clears the mirror without rebuilding it.
- Public node collection mutations do not reliably mutate the domain collection, so the old “two-way collection binding” claim is inaccurate.
- Event handlers are not managed through a clear subscription lifetime, allowing duplicate subscriptions and stale references.

### CLI and extension functions

- The parser is whitespace-based, assumes nonempty input, and has weak literal and error handling.
- `show` can inspect cached objects but does not implement path navigation, breadcrumbs, or a stable current location.
- Commands described in comments, including parameter assignment and collection expressions, are not implemented.
- Filter, flatten, and merge experiments are disabled, incomplete, or throw at runtime.
- The old expression direction conflicts with the approved model of constrained, serializable, preview-first batch operations.

### Engineering baseline

- There are no unit, integration, property-based, or end-to-end tests.
- Nullable analysis and analyzers are not enabled.
- The solution has no continuous integration or clean-checkout verification.
- Package identity, licensing, compatibility, and release policy are intentionally undefined; packaging must remain disabled.

## 5. Conflicts With the Target Design

### Tree versus graph

The design requires cycles, shared references, and stable runtime identity. The legacy implementation requires nodes to form a tree and uses parent/succession relationships for traversal. Adding cycle checks to this model would not solve identity, repeated references, or durable binding. The graph runtime must be a new subsystem.

### Nodes versus semantic descriptors

`Node`, `ObjectNode`, `CollectionNode`, and `MethodNode` carry domain values, reflection metadata, mutable invocation state, presentation labels, enablement, preview behavior, and navigation links. Frontends would be coupled to these implementation details. The reboot must expose immutable metadata and explicit runtime operations through descriptor contracts.

### Global dashboard versus host-scoped runtime

A static root registry cannot support multiple workbenches, deterministic disposal, dependency injection, role-dependent exposure, or isolated tests. Roots and services must belong to an explicit runtime/host instance.

### Eager collections versus scalable access

Ordinary browsing must not materialize arbitrary enumerables. Collection descriptors need explicit live, snapshot, paged, or virtualized semantics. Batch execution should snapshot target identities by default without forcing normal navigation to do so.

### Raw invocation versus structured interaction

Expected validation, binding, compatibility, and execution failures must be values that any frontend can present. Exceptions remain appropriate only for unexpected faults.

### Extension expressions versus batch operations

The unfinished LINQ-like node system is not a scripting foundation. Initial batch support will use constrained operation descriptors, predefined comparisons, compatibility analysis, preview, sequential execution, cancellation, and per-item results.

## 6. Settled Decisions

1. **Product name and prefix:** the rebooted product remains UIEngine, and `UIEngine` is the final package and namespace prefix. Version 0.2.3 and its `Dashboard`/`Node` API are the legacy implementation of that product.
2. **Compatibility:** the new public API is a clean break. No `Dashboard`/`Node` compatibility adapter will be built.
3. **Framework baseline:** new and retained projects use .NET 10. The upgrade from .NET 8 was routine LTS maintenance rather than an MVP dependency.
4. **Framework MVP:** the new core and a minimal CLI validate the complete baseline interaction model.
5. **Product MVP:** a TUI is the reference product frontend. Avalonia is not part of the plan.
6. **Exposure default:** exposure is opt-in. Properties are preferred; fields may be supported through the normalized exposure model.
7. **Trust boundary:** both MVPs are in-process trusted developer tools. Remote access is deferred.
8. **Identity:** runtime identity is mandatory. Domain identity may be supplied by interface, attribute, or registry callback through a normalized provider.
9. **Layouts:** layouts are a committed post-MVP capability. They are portable, versioned files with optional application/user metadata and persist components and bindings, not domain state.
10. **Batch filters:** batch operations are a committed post-MVP capability. Their first version uses predefined member comparisons, not a general expression language.
11. **Batch execution:** mutation is preview-first and sequential by default. Partial success is explicit; automatic rollback is not promised.
12. **Component containment:** the core layout model represents generic containment and binding metadata; exact geometry belongs to the frontend.
13. **Packaging:** no package is produced until the product MVP APIs and tests are complete and license, versioning, and release policy are ready.

## 7. Migration Strategy

The new implementation will be built beside the legacy projects so that its vertical slices remain executable throughout development.

Carry forward the following ideas and fixtures:

- opt-in annotations and reflection fallback;
- the distinction between values, methods, and collections;
- `INotifyPropertyChanged` and `INotifyCollectionChanged` as supported observation adapters;
- the demographic model as a cyclic/shared-reference test fixture after it is made deterministic;
- a CLI as an architectural pressure test;
- a UI-toolkit-free core.

Replace rather than refactor:

- the `Node` inheritance tree and succession mechanism;
- the global static `Dashboard` registry;
- reflection objects as the public interaction API;
- collection mirroring and element mutation;
- method parameter/return nodes;
- extension-function nodes and expression AST;
- the ad hoc CLI parser and cache IDs;
- warning events as the error/diagnostic model.

Before removing legacy projects, extract maintained fixtures and add characterization tests only for behavior that informs the new system. The clean-break decision means tests should not freeze accidental legacy API behavior.

## 8. Phased Roadmap

### Phase 0 — Baseline and safety

Status: implemented and verified by GitHub Actions.

Goals:

- remove credential-bearing and obsolete configuration;
- target .NET 10 without producing packages;
- establish a verified build and smoke-test baseline;
- introduce tests and automated checks before runtime redesign.

The phase established:

- revoke the removed package credential through its provider;
- add characterization tests for useful exposure, value, action, collection, and notification behavior;
- enable nullable reference types and analyzers deliberately, fixing rather than suppressing findings;
- add CI for restore, build, tests, formatting/checks, and secret scanning;
- verify the repository from a fresh checkout with no machine-specific feeds or cached packages.

Exit criteria:

- a fresh checkout restores and builds with the documented .NET 10 SDK family;
- the removed credential is revoked, and no credential, EOL target, implicit package generation, or machine-specific configuration remains in the tracked working tree;
- retained legacy behavior is covered by focused tests;
- CI enforces the baseline.

### Phase 1 — Runtime core and CLI vertical slice

Status: implementation and acceptance complete.

The first bounded implementation outline, including its remaining Phase 0 prerequisites, is [Milestone 01 — Architecture Runway and CLI Walking Skeleton](docs/milestones/01-architecture-runway-cli-walking-skeleton.md).

Create one framework library with separate runtime-core, exposure-attribute, and reflection-discovery namespaces and directories, plus a separate CLI executable, tests, and representative samples. Do not create projects solely to mirror logical namespaces. Keep dependency direction from frontends toward semantic contracts; the framework must not reference TUI or CLI presentation types.

Implement the minimum end-to-end model:

- host-scoped root registration;
- opt-in reflection discovery with cached immutable type metadata;
- object, value, reference, collection, and action descriptors;
- runtime object identity based on reference identity;
- cycle-safe traversal that preserves shared references;
- structured results for reads, writes, validation, resolution, and calls;
- a minimal logical path model sufficient for CLI navigation;
- CLI `ls`, `cd`, `inspect`, `get`, `set`, and parameterized synchronous `call`.

Exit criteria:

- one cyclic/shared-reference sample can be navigated indefinitely without recursive expansion;
- repeated references resolve to the same runtime identity;
- CLI reads and writes live domain state and invokes a parameterized action;
- failures are presented from structured results rather than parsed exceptions;
- core and CLI behavior is covered by automated tests.

### Phase 2 — Framework MVP interaction hardening

Add:

- programmatic exposure for third-party and runtime-dependent types;
- normalized runtime/domain identity providers;
- complete logical paths, binding references, binding states, and recovery after object replacement;
- validation metadata and structured value/action validation;
- host-provided dispatch for thread-affine access;
- normalized property and collection changes with safe subscription disposal;
- collection live/snapshot/paged/virtualized contracts;
- async actions, completion, progress, cancellation, and exception capture;
- logging for discovery, binding, mutation, invocation, and capability mismatches.

Exit criteria:

- tests cover object replacement, broken and ambiguous bindings, mutable collections, async completion/failure, cancellation, and dispatch;
- null values and unavailable values are distinct states;
- large/lazy collections can be browsed without unconditional materialization;
- all expected interaction failures use structured results;
- the CLI covers discovery, navigation, inspection, get/set, sync/async calls, progress, cancellation, observation, and collection listing;
- the framework MVP acceptance suite passes against a non-trivial cyclic fixture.

### Phase 3 — TUI product MVP and legacy retirement

Build the reference TUI on the framework MVP without adding frontend-specific behavior to the core:

- capability-aware object browser, breadcrumbs, and navigation history;
- generated value editors and action parameter forms;
- async progress and cancellation presentation;
- collection browsing;
- structured failure and unavailable/missing/mismatched target presentation;
- a frontend capability declaration exercised by both TUI and CLI;
- keyboard-first interaction and terminal resize behavior appropriate to the selected TUI toolkit.

Exit criteria:

- users can navigate, inspect, mutate, and invoke the same cyclic fixture through CLI and TUI;
- async progress, cancellation, collection browsing, and structured failures are usable in the TUI;
- core, CLI, and TUI acceptance tests pass;
- useful legacy samples have deterministic replacements;
- UIEngine, Dataset, and CLITest legacy projects can be removed without losing product coverage;
- licensing, versioning, package names, supported platforms, and release policy are defined before any package is published.

### Phase 4 — Post-MVP layouts and advanced binding

Implement:

- component creation, deletion, movement, containment, configuration, duplication, and rebinding;
- versioned portable layout serialization;
- explicit broken-binding visualization and repair candidates;
- domain identity recovery, binding migrations, and reusable layout templates;
- TUI-appropriate workspace composition without leaking terminal geometry into core binding contracts.

Exit criteria:

- components can be created, rebound, saved, loaded, and removed;
- layout round trips preserve framework bindings and TUI payloads;
- unavailable and mismatched bindings remain visible without silent rebinding;
- layout migration tests cover every supported schema version.

### Phase 5 — Post-MVP constrained batch operations

Implement:

- collection, explicit-selection, and search-result target selections;
- identity snapshots before execution;
- predefined member-comparison filters;
- set-value and invoke-action operation descriptors;
- per-target compatibility classification;
- mandatory preview for mutations;
- sequential execution with continue/stop policy, cancellation, confirmation, and dry run;
- per-item success, failure, skipped, and cancelled results.

Exit criteria:

- mixed-type and partially compatible collections produce accurate previews;
- validation failures do not start an invalid target operation;
- partial success is reported without implying rollback;
- stop-on-error, continue-on-error, and cancellation are deterministic and tested;
- batch plans contain serializable descriptors rather than delegates.

### Phase 6 — Post-MVP capabilities

Consider in this order, based on demonstrated demand:

1. source generation, trimming, and AOT support;
2. search and indexing;
3. reusable batch templates;
4. tables, charts, projections, and safe derived values;
5. an authenticated and authorized remote descriptor/command protocol;
6. web, desktop, voice, and domain-specific frontends.

These features must not delay either MVP or weaken their contracts through premature generalization.

## 9. Test Strategy

### Unit and contract tests

- reflection discovery, overrides, duplicate IDs, and invalid exposure;
- runtime and domain identity stability;
- value conversion, validation, nullability, read-only state, and failures;
- action parameter metadata, sync/async results, progress, cancellation, and exceptions;
- collection snapshots, paging, mutation events, replacement, reset, and null elements;
- path parsing/resolution and every binding state;
- dispatcher routing and disposal of observation subscriptions;
- frontend capability compatibility;
- batch compatibility, preview, ordering, partial failure, and cancellation;
- layout serialization, round trips, and migrations.

### Property-based tests

- path parse/format round trips;
- graph traversal termination across arbitrary cycles and shared references;
- layout round trips for valid component trees;
- batch result accounting, ensuring every snapshotted target receives exactly one terminal status.

### Integration tests

- drive the same fixture through reflection discovery, runtime operations, CLI, and TUI;
- replace bound objects and verify identity/path fallback behavior;
- mutate domain state independently and verify normalized frontend changes;
- execute thread-affine operations through a test dispatcher;
- load older layouts through registered migrations.

### Acceptance fixtures

Maintain deterministic fixtures for:

- cyclic and shared references;
- null and replaced references;
- mutable and very large/lazy collections;
- sync, async, cancellable, failing, and destructive actions;
- validation and permission-like rejection;
- mixed batch compatibility and partial failure;
- broken and recoverable bindings.

## 10. Risks and Mitigations

| Risk | Mitigation |
|---|---|
| Recreating the node tree behind new names | Keep descriptors immutable where practical and separate metadata, target identity, binding, and invocation state. |
| Building the TUI before contracts stabilize | Complete the framework MVP and its contract tests first. |
| Treating reflection paths as identity | Make runtime identity mandatory and paths an independent resolution mechanism. |
| Materializing arbitrary collections | Require explicit collection access modes and paging/virtualization contracts. |
| Promising rollback for domain side effects | Report partial success; support transactions only through explicit host contracts. |
| Generalizing batch operations into a scripting language | Start with closed operation/filter descriptor sets and serializable plans. |
| Leaking frontend concepts into the core | Enforce project dependency direction and validate the API through both CLI and TUI. |
| Publishing an unstable API | Keep packaging disabled until the product MVP release gate defines compatibility and release policy. |
| Losing useful prototype knowledge during the clean break | Add narrow characterization tests and migrate deterministic fixtures before removing legacy projects. |

## 11. Deferred Decisions

The following remain intentionally open until their roadmap phase supplies evidence:

- exact public names and signatures beyond the descriptor roles established by the design;
- final package names and repository/package rename mechanics;
- detailed path grammar beyond the Phase 1 navigation needs;
- the TUI toolkit and concrete terminal composition/layout model;
- source-generator implementation and AOT target matrix;
- remote transport and authorization model;
- package versioning and support policy.

Deferral does not relax the settled requirements for frontend independence, graph identity, structured results, safe batch execution, or the in-process MVPs.
