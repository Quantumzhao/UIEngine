# UIEngine Project Context

> **Status:** current shared project context. Update it whenever a product decision changes; unresolved items remain explicitly deferred.

## Purpose

UIEngine is a .NET framework for exposing a running application's live domain model as an interactive object space. It should let developers navigate exposed objects, inspect and mutate values, invoke operations, compose persistent workbench layouts, and run constrained batch operations without first building a bespoke application UI.

The core interaction model is frontend-independent. CLI, TUI, desktop, web, voice, and domain-specific clients may present the same capabilities differently. The framework MVP will prove the core through a minimal CLI; the product MVP will add the reference TUI.

UIEngine is intended primarily for simulations, engines, services, research systems, internal tools, and prototypes whose domain model exists before a polished operational interface.

## Document Roles and Consistency

- `PROJECT_CONTEXT.md` records settled product intent, constraints, and decisions.
- `runtime-domain-workbench-design.md` contains the detailed target architecture and desired behavior.
- `REBOOT_PLAN.md` records the legacy audit, migration strategy, risks, roadmap, and acceptance criteria.
- `TODO.MD` is the concise progress tracker derived from the reboot plan.
- `README.MD` is the public repository entry point and current-state summary.
- `docs/milestones/` contains bounded execution plans derived from the context, design, and reboot roadmap. An outline does not override those documents or authorize work until it is explicitly selected for implementation.

These documents must agree; their different roles do not create a precedence order. If a conflict is found, stop before relying on either statement, report the exact conflicting claims, resolve the intent with the product owner, and update every affected document together. Do not silently choose one document over another.

Code and tests are authoritative for current implemented behavior. The context and design are authoritative for intended behavior. Any discrepancy between implementation and intended behavior must be recorded as unfinished work or an explicit design change, not hidden by changing only one side.

## Current State

- The repository builds on .NET 8.
- The implementation is the legacy UIEngine v0.2.3 proof of concept.
- It demonstrates opt-in reflection discovery, basic value access, synchronous invocation, simple collection wrapping, partial change observation, and a small CLI.
- It uses a tree-oriented `Dashboard`/`Node` architecture that conflicts with the target graph and descriptor model.
- There is no automated test project, production frontend, layout system, batch engine, or supported package.
- Packaging is disabled. Package metadata and release automation must not be reintroduced before the product MVP release gate defines licensing, versioning, compatibility, and support policy.
- A removed package credential remains in pre-cleanup Git history and must be revoked through its provider. Rewriting history is outside the current cleanup scope.

The legacy implementation is reference material and a source of candidate fixtures. It is not the public API foundation for the reboot.

## Settled Product Decisions

- **Name and prefix:** UIEngine is the final product name and package/namespace prefix.
- **Compatibility:** make a clean public API break; do not build a `Dashboard`/`Node` compatibility adapter.
- **Framework:** target .NET 8 for the reboot. A later LTS upgrade is maintenance, not an MVP requirement.
- **Exposure:** require explicit opt-in exposure. Prefer properties while allowing fields through the normalized exposure model.
- **Trust boundary:** begin as an in-process trusted developer tool. Remote access is a later, separate protocol.
- **Framework MVP:** deliver the new core and a minimal CLI that validates the interaction model.
- **Product MVP:** deliver a TUI on top of the framework MVP. Do not build Avalonia as the reference frontend.
- **Identity:** require stable runtime identity. Accept optional domain identity through a normalized provider backed by an interface, attribute, or registry callback.
- **Layouts:** persist versioned component configuration and bindings, not domain state. Keep exact geometry frontend-owned.
- **Batch operations:** use constrained serializable descriptors, predefined filters, compatibility preview, and sequential execution by default.
- **Transactions:** do not promise rollback for arbitrary domain side effects; report partial success explicitly.
- **Packaging:** publish nothing until APIs, tests, licensing, versioning, and release policy are ready.
- **Project granularity:** begin with one `UIEngine.Framework` library containing the core, attributes, and reflection areas as directories and namespaces. Keep executable frontends separate, but do not create a project merely to mirror a namespace or architectural label. Add an assembly boundary only when dependency isolation, deployment, packaging, target-framework, or tooling requirements justify it.

## Architectural Invariants

The reboot must preserve these boundaries:

1. **The domain model remains authoritative.** UIEngine interacts with live objects or explicit adapters rather than requiring a duplicate UI model.
2. **The runtime models an object graph, not a tree.** Cycles, shared references, object replacement, and disappearing targets are normal cases.
3. **Identity and path are different concepts.** Runtime identity represents an object instance; optional domain identity survives replacement/restart; logical paths support navigation and fallback resolution.
4. **Descriptors express semantics, not controls.** The core describes readable values, writable values, selections, references, collections, actions, progress, and related capabilities without naming widgets.
5. **The runtime is host-scoped.** Avoid global registries so hosts can be isolated, disposed, configured, and tested independently.
6. **Expected failures are structured results.** Validation, binding, compatibility, and execution failures should not require frontends to parse exception strings.
7. **Thread affinity is explicit.** Reads, writes, and calls use a host-provided dispatcher when domain objects require one.
8. **Collection semantics are explicit.** Distinguish live, snapshot, paged, and virtualized access; never require eager materialization for ordinary browsing.
9. **Observation is adaptable and disposable.** Support common .NET notifications, polling, and custom providers through normalized change events and managed subscription lifetimes.
10. **Frontend capabilities are negotiated.** No frontend is assumed to support every descriptor, layout feature, or interaction.
11. **Batch mutation is preview-first.** Snapshot targets, classify compatibility, execute under an explicit policy, and record a terminal result for every target.
12. **Remote operation is not transparent object serialization.** Any future remote layer uses explicit descriptor DTOs, commands, authentication, authorization, and audit behavior.

## MVP Boundaries

### Framework MVP — Core + CLI

The framework MVP is complete when one non-trivial cyclic domain model can be used through the CLI to:

- discover explicitly exposed roots and members;
- navigate cycles and shared references without recursion failure or identity loss;
- inspect and mutate live values with validation;
- invoke parameterized synchronous and asynchronous actions;
- observe selected external state changes;
- report progress, cancellation, and structured failures;
- list and inspect collection elements without requiring eager materialization;
- expose the same semantic contracts that a non-CLI frontend can consume.

### Product MVP — TUI

The product MVP is complete when a TUI consumes the framework MVP without frontend-specific behavior leaking into the core and provides:

- an object browser with navigation history or breadcrumbs;
- generated value editors and action parameter forms;
- collection browsing;
- async progress, cancellation, and structured error presentation;
- clear unavailable, missing, and mismatched target states;
- an end-to-end workflow over the same cyclic fixture used by the CLI.

Persistent user-composed layouts, batch operations, arbitrary scripting, automatic undo, remote access, source generation, advanced analytics, and additional frontends are not part of either MVP. Layouts and batch operations remain committed post-MVP capabilities.

## Implementation Direction

New code should use the fewest project boundaries that preserve actual runtime and tooling separation. Namespace and directory structure expresses logical architecture; it does not imply one assembly per area.

A short summary comment explaining the role of each class or struct is encouraged. Name private members with an underscore followed by PascalCase, such as `_Field`, `_Property`, and `_Method`; constructors and language- or runtime-mandated names such as `Main` retain their required spelling. Name every constant and static-readonly field in `UPPER_SNAKE_CASE`, using a leading underscore only for private fields (for example, `PUBLIC_FIELD` and `_PRIVATE_FIELD`). Enum members also use `UPPER_SNAKE_CASE`, such as `InteractionErrorCode.INVALID_ROOT_IDENTIFIER`. Analyzer rule CA1707 is disabled solution-wide because its underscore prohibition conflicts with these conventions.

The framework MVP starts with:

- `UIEngine.Framework` — one library containing the `UIEngine.Core`, `UIEngine.Attributes`, and `UIEngine.Reflection` namespaces and directories;
- `UIEngine.Frontend.Cli` — a separate executable that depends on `UIEngine.Framework`;
- dedicated sample and test projects where executable or test-host boundaries require them.

Future hosting, layouts, batch, and similar capabilities begin as logical areas and namespaces unless a concrete dependency or deployment requirement justifies another project. The product TUI remains a separate frontend executable. Source generators may require their own tooling project because they have a distinct compiler-facing target and dependency model.

Exact public type names and justified assembly or package boundaries may be refined during their roadmap phase, but frontend independence and dependency direction are not negotiable.

Build the new implementation beside the legacy projects. Extract deterministic fixtures and narrow characterization tests before removing legacy code, but do not preserve accidental legacy API behavior merely for compatibility.

## Near-Term Priorities

1. Revoke the removed package credential through its provider.
2. Add characterization tests for legacy behavior worth carrying forward.
3. Enable nullable analysis and analyzers deliberately, fixing rather than suppressing findings.
4. Add CI for clean restore, build, tests, checks, and secret scanning.
5. Start the Framework/CLI vertical slice defined in `REBOOT_PLAN.md`.

Use `dotnet build UIEngine.sln` as the minimum repository verification. Add the smallest relevant automated tests for every behavior change once a test project exists.

## Explicitly Deferred Decisions

Do not invent these prematurely:

- final public type signatures beyond the descriptor roles and invariants above;
- the complete logical path grammar;
- package versioning and long-term support policy;
- source-generator implementation and AOT matrix;
- the TUI toolkit and detailed terminal composition/layout model;
- remote transport, authentication, and authorization;
- additional frontend commitments.

Resolve each deferred decision in its roadmap phase with tests or a concrete product requirement, then update this document if the result becomes a stable project-wide constraint.
