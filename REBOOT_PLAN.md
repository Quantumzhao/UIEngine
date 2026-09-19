# UIEngine Architecture, Reboot Audit, and Plan

**Status:** consolidated target specification and implementation plan; architecture runway and CLI walking skeleton complete; Framework MVP hardening next

**Legacy baseline:** UIEngine v0.2.3

**Target framework:** .NET 10

**Audience:** framework implementers, frontend authors, .NET library authors, and advanced users

**Scope:** frontend-agnostic exposure, navigation, inspection, mutation, invocation, batching, and workbench composition over live .NET domain models

## 1. Executive Assessment

The repository contains a buildable proof of concept for reflecting over annotated .NET objects. It can discover selected properties and methods, expose their values through mutable wrapper nodes, invoke simple synchronous methods, wrap some collections, and demonstrate those behaviors through a small CLI.

That prototype validates the original idea, but it is not a safe foundation for incremental implementation of the UIEngine design. Its central abstraction is a mutable tree of `Node` objects that combines discovery, binding, navigation, invocation state, and presentation hints. The target design instead requires a graph-aware runtime with stable identity, semantic descriptors, structured operations, explicit bindings, and frontend independence.

The reboot will therefore make a clean API break. Useful concepts and domain fixtures will be retained, but the legacy `Dashboard`/`Node` API will not receive a compatibility adapter. The new core and a minimal CLI form the framework MVP; a TUI built on that foundation forms the product MVP.

This document is the consolidated target architecture and implementation plan. It records the product semantics, what exists, what does not, known correctness risks, settled decisions, and the implementation sequence. [TODO.MD](TODO.MD) is the corresponding progress checklist.

## 2. Repository Baseline

| Area | Purpose | Current condition |
|---|---|---|
| `UIEngine/` | Legacy reflection and node library | Builds on .NET 10; retained as a characterized baseline |
| `CLITestProject/` | CLI plus a cyclic demographic model | Useful fixture source; CLI is incomplete |
| `Dataset/` | Additional annotated sample model | Despite its name, it contains no tests |
| `Core/` | Reboot runtime contracts, attributes, host, and reflection provider | Milestone 01 implementation complete |
| `Frontend/Cli/` | Descriptor-driven proving frontend | Milestone 01 implementation complete |
| `Examples/CyclicDomain/` | Deterministic reboot fixture | Covers cyclic and shared-reference traversal |
| `Tests/` | Framework, CLI, and legacy-characterization tests | 29 tests pass in clean-checkout acceptance |
| `REBOOT_PLAN.md` | Target architecture, legacy audit, migration plan, risks, and acceptance criteria | Consolidated authoritative design and execution plan |
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

Create one `Core` library with separate runtime, exposure-attribute, and reflection-discovery namespaces and directories, plus a separate CLI executable, tests, and representative examples. Do not create projects solely to mirror logical namespaces. Keep dependency direction from frontends toward semantic contracts; the core must not reference TUI or CLI presentation types.

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
- package versioning and support policy;
- exact exposure attribute names;
- an arbitrary scripting language and generalized undo model;
- parallel batch execution;
- distributed object identity;
- collaborative multi-user layouts; and
- a voice-intent system.

Deferral does not relax the settled requirements for frontend independence, graph identity, structured results, safe batch execution, or the in-process MVPs.

## 12. Product Definition and Scope

UIEngine is a .NET middleware framework that exposes a running application's domain model as an **interactive object space**. It defines a runtime interaction model rather than a particular UI technology, allowing CLI, TUI, desktop, web, voice, and domain-specific frontends to present the same semantic capabilities differently.

The primary users are simulations, engines, services, research systems, workflow systems, internal tools, and prototypes that already have a useful domain model but do not yet justify a bespoke operational UI. A temporary UI otherwise duplicates work in layout, binding, validation, navigation, synchronization, asynchronous progress, error handling, and persistence. Debuggers, property grids, and CRUD-oriented admin frameworks do not provide a stable operational model for cyclic in-memory graphs, supported domain actions, composition, or batch work.

The core model is:

```text
Domain Model
    -> Exposure and Metadata
    -> Interactive Object Space
    -> Binding, Invocation, and Batch Semantics
    -> Frontend Adapter
    -> CLI, TUI, Desktop, Web, Voice, or Custom Frontend
```

### 12.1 Objectives

The framework must provide:

- low-friction opt-in exposure of fields, properties, methods, and relationships through attributes or registration APIs;
- interaction with live runtime objects rather than mandatory DTO copies, except when an explicit snapshot is requested;
- frontend-independent semantic contracts;
- explicit support for cycles and shared references;
- structured parameterized actions with validation, asynchronous completion, progress, cancellation where supported, and structured results;
- constrained collection-level operations with target preview, compatibility analysis, sequential execution by default, and per-target results;
- extensibility by type, interface, capability, and semantic descriptor; and
- post-MVP user composition in which components can be created, deleted, moved, nested, configured, rebound, and persisted.

Secondary objectives are headless operation, search, logging and auditability, reusable layouts and batch templates, table/chart analysis, automation through the same interaction model, and a later explicit remote protocol.

### 12.2 Non-goals

UIEngine is not initially:

- a production end-user UI framework;
- a general low-code/no-code application builder;
- a database administration framework or ORM;
- a complete scripting language;
- a distributed object system;
- a universal visual analytics platform;
- an automatic undo or transaction engine for arbitrary side effects; or
- a promise that every frontend supports every capability.

The concise technical positioning is: **a .NET runtime interaction framework that exposes live domain objects as a navigable, mutable, invocable object space with pluggable frontends and persistent user-composed workbenches.** The product-oriented positioning is: **build operational workbenches for complex .NET systems without building the workbench by hand.**

## 13. Terminology

- **Domain object:** a live .NET object whose exposed state or operations are visible through UIEngine.
- **Interactive object space:** the graph of exposed objects, values, references, collections, actions, and metadata available to frontends.
- **Interaction descriptor:** a frontend-neutral description of a readable or writable value, finite selection, reference, collection, action, async operation, progress source, graph/sequence, summary, or batch capability.
- **Binding:** a transient or persistent association between a frontend component and a target in the object space.
- **Binding target:** the member, action, collection, query result, or computed source referenced by a binding.
- **Frontend:** a consumer of the semantic model, such as a CLI, TUI, desktop, web, voice, or custom interface.
- **Component:** a frontend-owned interaction element consuming one or more descriptors. It becomes a framework concern only where the layout subsystem represents its type, configuration, containment, and bindings.
- **Layout:** a persistent user-defined graph of component instances, configuration, containment, and bindings.
- **Batch operation:** a constrained operation plan that selects several targets and applies a compatible operation under an explicit execution policy.

## 14. Target Architecture and Principles

The semantic flow is:

```text
Domain application
    -> exposure/metadata providers
    -> host-scoped object-graph runtime
    -> interaction descriptors
    -> bindings/layouts and batch planning
    -> frontend adapter API
    -> concrete frontends
```

The architecture obeys the invariants in `PROJECT_CONTEXT.md` and the following detailed rules:

1. The core describes capabilities such as readable values, selections, actions, collections, navigation, and progress. It never exposes widget contracts such as buttons, text boxes, or dropdowns.
2. The domain model remains authoritative. A binding resolves to a live object or an explicit adapter/proxy; UIEngine does not require a parallel mutable UI model.
3. The runtime treats objects as a graph. Multiple paths may reach one object; objects and collection positions may change; computed members need not represent stored state.
4. CLR type is only a default signal. Range, units, display name, validation, read-only state, semantic role, and preferred interaction metadata refine the descriptor.
5. Safety metadata is part of the interaction contract. Actions describe parameters, validation, confirmation, concurrency, cancellation, progress, failure behavior, danger level, and batch compatibility.
6. Metadata, target identity, binding state, invocation state, and presentation state remain separate concerns.

## 15. Exposure and Descriptor Specification

### 15.1 Exposure providers

The baseline reflection provider supports opt-in attributes with semantics equivalent to:

```csharp
[Expose]
public int Population { get; set; }

[Expose(ReadOnly = true)]
public double GDP { get; private set; }

[Action]
public Task RecalculateAsync(CancellationToken ct) => Task.CompletedTask;

[Children]
public IReadOnlyList<City> Cities { get; }

[Summary]
public string Summary => $"{Name}: {Population:N0} residents";
```

Exact attribute names remain deferred. Programmatic registration must cover third-party types, runtime- or role-dependent exposure, computed metadata, generated models, generic wrappers, and identity callbacks. An illustrative fluent surface is:

```csharp
registry.For<City>()
    .Expose(x => x.Population)
    .Expose(x => x.GDP, readOnly: true)
    .Action(x => x.RecalculateAsync(default))
    .Children(x => x.Districts)
    .Summary(x => $"{x.Name} ({x.Population:N0})");
```

Reflection remains the dynamic fallback. A later optional source generator may provide startup performance, compile-time validation, trim/AOT safety, and generated descriptor factories.

### 15.2 Descriptor roles

Descriptors normalize reflection and programmatic exposure into semantic roles:

- a value descriptor has a stable member identifier, display name, CLR value type, readable/writable capabilities, validation metadata, and structured asynchronous read/write operations;
- a selection descriptor extends a value with a finite option set;
- an object descriptor exposes runtime identity, type/display/summary metadata, and its values, actions, references, and collections;
- a reference descriptor resolves another object lazily through an object handle;
- an action descriptor exposes parameter metadata, execution metadata, validation, and invocation;
- a collection descriptor exposes its element semantics and explicit live, snapshot, paged, or virtualized access modes; and
- progress and invocation descriptors normalize long-running work independently of the method's CLR return type.

The intended shape is illustrated below; exact public signatures remain phase-specific:

```csharp
public interface IValueDescriptor
{
    string Id { get; }
    string DisplayName { get; }
    Type ValueType { get; }
    bool CanRead { get; }
    bool CanWrite { get; }
    IReadOnlyList<IValidationRule> ValidationRules { get; }
}

public interface ISelectionDescriptor : IValueDescriptor
{
    IReadOnlyList<SelectionOption> Options { get; }
}

public interface IObjectDescriptor
{
    ObjectIdentity Identity { get; }
    string TypeName { get; }
    string DisplayName { get; }
    string Summary { get; }
    IReadOnlyList<IValueDescriptor> Values { get; }
    IReadOnlyList<IActionDescriptor> Actions { get; }
    IReadOnlyList<IReferenceDescriptor> References { get; }
    IReadOnlyList<ICollectionDescriptor> Collections { get; }
}
```

Action invocation returns an invocation object exposing current status, completion, optional progress, a structured result, captured exception information, and cancellation capability. Collection snapshot operations return handles that retain host-scoped object identity. Batch execution snapshots identities by default even though ordinary collection browsing must not require materialization.

## 16. Identity, Paths, and Binding Resolution

### 16.1 Identity scopes

Path strings cannot be identity because indices change, objects move or disappear, several paths can reach one object, and a path may later resolve to a replacement. UIEngine distinguishes:

1. **Runtime identity:** reference-based identity stable within one host/process lifetime.
2. **Domain identity:** an optional semantic key stable across replacement or restart.
3. **Logical path:** a human-readable navigation and fallback-resolution expression.

Applications may provide domain identity through an interface, attribute, or registry callback normalized behind one identity provider. Example keys include `Nation/USSR`, `City/Moscow`, and `Workflow/OrderApproval/Instance/1281`. Persistent bindings prefer domain identity and use paths according to an explicit fallback policy.

Cycle-safe tree projections maintain visited runtime identities and render repeated references as links. They must preserve that all occurrences point to the same live object.

### 16.2 Logical paths

Paths refer to exposed semantics, not raw CLR reflection chains. The eventual grammar must support named members, root aliases, keyed collection lookup, explicitly permitted index lookup, identity references, and later optional predicates. For example:

```text
/World/Nations[key=USSR]/Cities[key=Moscow]/Population
```

The exact grammar remains deferred, but path parsing/formatting, resolution, and ambiguity are structured operations.

### 16.3 Persistent binding records and states

A persistent binding contains at least:

```csharp
public sealed record BindingReference(
    string? DomainIdentity,
    string? Path,
    string MemberId,
    string ExpectedDescriptorKind,
    string? ExpectedTypeName,
    BindingFallbackPolicy FallbackPolicy);
```

Resolution has explicit states: `Resolved`, `TemporarilyUnavailable`, `TypeMismatch`, `TargetMissing`, `Ambiguous`, `PermissionDenied`, and `InvalidPath`. A failed binding must never silently attach to a semantically different target. A repair workflow may offer compatible candidates.

## 17. Type Mapping, Components, and Frontend Capabilities

Default semantic mappings include:

| Domain type or metadata | Semantic descriptor | Possible presentation |
|---|---|---|
| `bool` | editable scalar | toggle or textual boolean |
| numeric type | numeric value | numeric field |
| numeric plus range | ranged numeric value | slider or spinbox |
| enum or finite options | selection | dropdown, radio group, or completion list |
| `string` | textual value | text editor |
| collection | collection | list, table, or tree |
| object reference | navigation/reference | link or tree node |
| method | action | command, button, or menu item |
| async method | async action | command plus progress |
| time series | sequence | chart or table |
| graph model | graph descriptor | graph view or table |

Developers may override defaults with semantic metadata such as range, units, tags, and a preferred interaction. Preferences are advisory: a CLI need not emulate a slider.

A component declares the descriptor shapes and features it accepts. For example, a line chart might accept numeric sequences or observable timestamp/value pairs; an action component might accept parameterless actions or parameterized actions when it can generate an argument form.

Frontends advertise capabilities during initialization. The vocabulary includes reading and writing values, invoking actions, navigating graphs, rendering collections or charts, persisting layouts, rebinding components, previewing batches, presenting async progress and confirmation, and search. The runtime may omit, downgrade, or reject unsupported interactions, but capability negotiation does not alter core semantics.

Navigation must cover roots, references, collection elements, parent/breadcrumb history, and back/forward behavior where appropriate. Object summaries resolve in this preference order: explicit provider, annotated summary member, display-name metadata, `ToString()`, then type name plus runtime identity. Generated browsers must not rely only on CLR type names or memory-like identifiers.

## 18. Layout and Workbench Specification

The post-MVP layout system turns a transient inspector into a reusable workbench. It persists a versioned graph of frontend component instances, configuration, containment, and framework bindings—not domain state and not a universal pixel geometry model.

A layout-capable frontend supports:

- create, delete, duplicate, move, nest, and unnest where valid;
- edit component configuration;
- bind and rebind components;
- save and load layouts; and
- keep broken bindings visible with their resolution states and repair options.

An illustrative portable payload is:

```json
{
  "layoutVersion": 1,
  "root": {
    "componentType": "SplitPane",
    "children": [
      {
        "componentType": "NumericEditor",
        "binding": {
          "domainIdentity": "City/Moscow",
          "memberId": "Population"
        }
      },
      {
        "componentType": "LineChart",
        "binding": {
          "domainIdentity": "Nation/USSR",
          "memberId": "GDPHistory"
        }
      }
    ]
  }
}
```

Every serialized layout includes a schema version. Known versions have explicit migrations. A frontend-specific payload may coexist with portable framework-level component, containment, configuration, and binding data. Portable files may also include optional application/user metadata.

Three versions must remain distinct: framework API version, layout schema version, and domain binding compatibility. Domain changes can invalidate bindings independently of the other two. Migration hooks may rename or transform binding targets, for example `Nation.GDPHistory` to `Nation.EconomicHistory`.

## 19. Actions, Validation, and Invocation

### 19.1 Parameter and execution metadata

Each action parameter exposes its identifier, display name, CLR type, nullability, default value, validation rules, finite options where applicable, and semantic metadata. Frontends may generate forms or CLI completion from this information.

Action descriptors support methods returning `Task`, `Task<T>`, `ValueTask`, and `ValueTask<T>` in addition to synchronous results. Progress may be adapted from `IProgress<T>`, a UIEngine progress channel, `IAsyncEnumerable<TProgress>`, or an invocation object that reports independently of the domain signature. The normalized invocation object is the frontend contract.

Cancellation support is declared by the invocation. A frontend must not offer cancellation when the operation cannot honor it. Risk/confirmation metadata includes `Safe`, `Mutating`, `Destructive`, `Irreversible`, and `RequiresConfirmation`; these are presentation and policy hints, not authorization boundaries.

### 19.2 Validation levels

Validation is structured at three levels:

1. **Descriptor validation:** whether the requested interaction is structurally valid.
2. **Value validation:** whether a proposed value satisfies type, nullability, range, and domain rules.
3. **Action validation:** whether an action is currently valid for the target and complete argument set.

Expected validation or invocation failures are results with stable error codes. Unexpected faults may be captured as exception details without making frontends parse exception messages.

## 20. Batch Operation Specification

Batch operations provide a constrained higher-order model equivalent to “for each selected compatible target, perform this operation” without exposing arbitrary code execution. The public concept is **Batch Operations** or **Collection Actions**, not a general higher-order-function or scripting system.

### 20.1 Plan and target selection

A plan has four conceptual stages:

```text
Selection -> Filter -> Operation -> Execution Policy
```

Target selections may come from the current collection, explicitly selected elements, search results, objects of an exposed type, or a saved selection query. Before execution, the engine snapshots target identities unless the user explicitly requests later streaming semantics.

A frontend may use one element as a template for discovering candidate operations. The template never establishes compatibility for the other targets; every target is checked independently.

The initial closed operation set supports:

- set an exposed writable value;
- invoke an exposed action;
- create a frontend component bound to each item;
- collect exposed values into a table;
- collect numeric or time-series values into a visualization; and
- export selected values.

The first implementation phase is intentionally narrower—set value and invoke action—per Phase 5. General user code remains deferred. Plans contain serializable descriptors rather than delegates, conceptually:

```csharp
public sealed class BatchOperationPlan
{
    public required TargetSelection Selection { get; init; }
    public IReadOnlyList<TargetFilter> Filters { get; init; } = [];
    public required OperationDescriptor Operation { get; init; }
    public required BatchExecutionPolicy Policy { get; init; }
}
```

### 20.2 Compatibility and preview

Before execution, each target receives one compatibility classification such as `Compatible`, `IncompatibleType`, `MissingMember`, `ReadOnly`, `ValidationFailed`, `Unavailable`, or `PermissionDenied`. Preview shows aggregate counts and, where practical, the per-target reason and before/after value.

Mutating batches require preview by default. Validation failure prevents that target's operation from starting. Compatibility is a snapshot assessment and does not erase the need to handle a target becoming unavailable during execution.

### 20.3 Execution and terminal results

Initial policies include sequential execution, continue-on-error or stop-on-first-error, cancellation, per-item result recording, confirmation, and dry run. Parallel execution is not the default and remains deferred.

Every snapshotted target receives exactly one terminal status: succeeded, failed, skipped, or cancelled. Partial success is reported explicitly. UIEngine never promises rollback for arbitrary domain actions; transactional or undo behavior exists only when the host supplies an explicit contract.

## 21. Threading, Observation, and Lifetime

### 21.1 Dispatch

The runtime never assumes arbitrary domain access is thread-safe. Hosts provide an execution-context abstraction suitable for a UI dispatcher, terminal event loop, engine main thread, simulation thread, actor scheduler, or synchronization context:

```csharp
public interface IInteractionDispatcher
{
    ValueTask<T> InvokeAsync<T>(Func<T> action, CancellationToken ct);
    ValueTask InvokeAsync(Action action, CancellationToken ct);
}
```

Reads, writes, validation, and calls use the dispatcher when required. A provider may explicitly mark safe operations that can bypass dispatch.

### 21.2 State and collection observation

UIEngine normalizes several observation sources:

1. `INotifyPropertyChanged`;
2. `INotifyCollectionChanged` and other observable collections;
3. explicit UIEngine notifications;
4. configurable polling; and
5. custom change-provider adapters.

Reflection alone cannot discover arbitrary external changes. A normalized change event includes object identity, member identity, optional old and new values, change kind, and timestamp or ordering token. Subscription ownership is host-scoped, disposable, and safe against duplicate or stale handlers.

## 22. Security and Remote Boundary

Both MVPs are in-process trusted developer tools. Exposed actions can be equivalent to full application control, and confirmation/danger metadata is not an authorization mechanism.

Any later remote layer requires explicit authentication, authorization, transport security, serialization rules, capability filtering, and audit logs. It must expose descriptor DTOs and explicit command messages rather than serialize or proxy the full in-process object graph transparently.

## 23. Frontend, Tooling, and Logical Structure

### 23.1 Frontend sequence

The core remains frontend-independent, but the product does not attempt equal support for every modality:

1. the CLI is the framework-MVP proving frontend and headless interface;
2. the TUI is the product MVP and primary reference frontend;
3. desktop and web frontends follow only with demonstrated demand; and
4. voice follows only after confirmation and ambiguity semantics mature.

The CLI pressure-tests whether navigation, inspection, mutation, invocation, observation, and batch semantics can be expressed without visual-toolkit leakage. Representative commands are:

```text
ls
cd /World/Nations[key=USSR]
inspect
get GDP
set TaxRate 0.05
call AdvanceTurn
watch FoodStockpile
batch apply RefillSupply --to Units --arg amount=100
```

Only commands belonging to the current roadmap phase should be implemented. The current CLI uses PrettyPrompt for interactive editing, process-local history, and descriptor-aware completion. A future structured output mode such as JSON may support scripting without turning the core into a scripting language.

The TUI toolkit remains deferred until Product MVP implementation. Selection favors active .NET 10 support, cross-platform terminal behavior, keyboard-first navigation, composable views and modal forms, non-blocking asynchronous updates, deterministic testing, and strict isolation of toolkit types from core contracts. Terminal geometry, rendering, focus, and key bindings are frontend-owned.

### 23.2 .NET implementation direction

The implementation uses C# on .NET 10 with nullable reference types and analyzers enabled. Relevant platform integrations include:

- `System.Text.Json` for layout/configuration serialization;
- optional `Microsoft.Extensions.DependencyInjection` host integration;
- `Microsoft.Extensions.Logging` for structured diagnostics;
- an optional Roslyn source generator when the roadmap reaches trimming/AOT work; and
- reflection as the dynamic fallback.

No core project depends on a desktop, web, CLI, or TUI toolkit.

### 23.3 Logical capability areas

These are namespaces and capability areas, not mandatory assembly boundaries:

```text
Core
    UIEngine.Core
        descriptors
        object identity
        binding
        validation
        actions
        collections
        change notifications
        attributes
        reflection

UIEngine.Generators
UIEngine.Layouts
UIEngine.Batch
UIEngine.Hosting
Frontend/Cli
Frontend/Tui
```

The framework MVP uses one `Core` library with the `UIEngine.Core`, `UIEngine.Core.Attributes`, and `UIEngine.Core.Reflection` namespaces. Executable frontends remain separate. A new assembly is justified only by dependency isolation, deployment, packaging, target-framework, compiler-tooling, or similar concrete requirements.

## 24. Diagnostics, Performance, Search, and Errors

### 24.1 Diagnostics

Structured diagnostic events cover descriptor discovery, binding resolution and breakage, mutation, action invocation/completion/failure, batch preview/execution, layout load/migration, frontend capability mismatch, and dispatch failure. They integrate with `ILogger` and may later feed an audit trail where the trust model requires one.

### 24.2 Performance rules

- Cache reflection discovery by type and reuse immutable metadata.
- Load object-graph branches lazily; never traverse the complete graph unless an explicit indexing or search operation requires it.
- Never materialize an arbitrary large, lazy, one-shot, or unbounded collection merely for ordinary browsing.
- Expose snapshot, paging, or virtualization deliberately according to provider semantics.
- Snapshot target identities before batch mutation unless a future policy explicitly requests live streaming.

### 24.3 Search

A later search/indexing subsystem supports display name, CLR/domain type, domain identity, path, tags, exposed member name, and summary text. Results resolve back to object identities and navigable targets. Search results may also serve as a batch selection source.

### 24.4 Structured error model

Exceptions are reserved for unexpected failures and captured fault details. Expected failures use operation-specific structured results, including binding resolution, value reads/writes, action validation/execution, batch items, and layout loading. Frontends must be able to present a stable code and meaningful reason without parsing exception text.

## 25. Worked Semantic Examples

### 25.1 Domain model

The following illustrates exposure, domain identity, validation metadata, summaries, collections, and actions. Names are examples rather than frozen public API:

```csharp
public sealed class WorldSimulation
{
    [Expose]
    public int Turn { get; private set; }

    [Children]
    public List<Nation> Nations { get; } = [];

    [Action]
    public void AdvanceTurn() => Turn++;

    [Action]
    public async Task RecalculateEconomyAsync(
        int iterations,
        CancellationToken ct)
    {
        // Domain operation.
    }
}

public sealed class Nation : IStableDomainIdentity
{
    private readonly List<double> _GDPHistory = [];

    public string DomainKey => $"Nation/{Code}";

    [Expose]
    public required string Code { get; init; }

    [Expose]
    [Range(0, 1)]
    public double TaxRate { get; set; }

    [Expose(ReadOnly = true)]
    public double GDP { get; private set; }

    [Expose]
    public IReadOnlyList<double> GDPHistory => _GDPHistory;

    [Children]
    public List<City> Cities { get; } = [];

    [Summary]
    public string Summary => $"{Code}: GDP {GDP:N0}";

    [Action]
    public void NormalizeTaxes(double rate)
    {
        foreach (City city in Cities)
        {
            city.TaxRate = rate;
        }
    }
}
```

A TUI can derive an object browser, summary list, numeric editor, read-only GDP display, GDP-history chart, and action forms. A CLI can expose paths such as `/world/nations[key=USSR]/tax-rate`. A saved workspace can hold a GDP chart per selected nation, a turn indicator, an `AdvanceTurn` action component, and a table of tax rates.

### 25.2 Batch plan

An illustrative user flow is:

```text
Collection: World.Nations
Template item: Nation/USSR
Operation: set TaxRate = 0.05
Filter: GDP < 1e12
Execution: sequential, continue on error
Preview: required
```

Its serializable plan selects `World.Nations`, applies a predefined member-less-than filter, sets `TaxRate`, and requests sequential continue-on-error execution with preview. Output reports matched and compatible counts followed by succeeded, failed, skipped, and cancelled totals, plus a structured reason for every non-successful target.

## 26. Success Criteria and Architectural Guardrails

The Framework MVP succeeds when a developer can annotate or register a non-trivial cyclic .NET model and use the CLI to navigate it without identity loss, inspect and mutate live values, invoke parameterized synchronous and asynchronous actions, observe selected changes, handle progress and cancellation, browse collections without unconditional materialization, and receive structured failures—without copying the domain into a frontend-specific mutable model.

The Product MVP succeeds when a coherent keyboard-first TUI performs the same workflow over the same fixture without introducing TUI-specific contracts into the core.

Longer-term success adds persistent user-composed workspaces and safe previewed batch operations. Those capabilities distinguish UIEngine from a property grid or generated admin form, but they must not delay the MVPs or push the framework into being a general scripting environment, application builder, or transparent remote-object runtime.
