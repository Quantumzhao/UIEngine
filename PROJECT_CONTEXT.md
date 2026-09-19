# UIEngine Project Context

> **Status:** Current shared agent context. Update when settled decisions change. Keep unresolved choices explicitly deferred.

## Purpose

- Expose a running .NET application's live domain model as an interactive object space.
- Support navigation, inspection, mutation, invocation, persistent workbenches, and constrained batch operations without a bespoke UI.
- Keep interaction semantics frontend-neutral. CLI, TUI, desktop, web, voice, and custom clients may present them differently.
- Target simulations, engines, services, research systems, internal tools, and prototypes with an existing domain model.
- **Framework MVP:** core runtime plus proving CLI.
- **Product MVP:** reference TUI over the same contracts.

## Document Roles

| Path | Role |
|---|---|
| `PROJECT_CONTEXT.md` | Settled intent, constraints, and decisions |
| `REBOOT_PLAN.md` | Detailed architecture, legacy audit, migration plan, risks, roadmap, and acceptance criteria |
| `TODO.MD` | Progress checklist derived from the reboot plan |
| `README.MD` | Public entry point and current-state summary |
| `docs/milestones/` | Bounded execution plans; not authorization to implement |

Consistency rules:

- No document has automatic precedence.
- On conflict: stop, quote both claims, ask the product owner, then update every affected document.
- Do not silently choose one claim.
- Code and tests define implemented behavior.
- This file and `REBOOT_PLAN.md` define intended behavior.
- Record discrepancies as unfinished work or explicit design changes.

## Current State

- **Platform:** .NET 10.
- **Reboot:** Milestone 01 is implemented beside legacy UIEngine v0.2.3.
- **Accepted:** clean restore, build, 29 tests, secret scan, and no-package check pass locally and in GitHub Actions.
- **Phase 2 baseline:** the later removal of `Dataset` and `CLITestProject` is accepted as intentional cleanup; their useful cyclic-fixture role is covered by `Examples/CyclicDomain`, and the current build passes with 29 tests after restoring CLI behavior coverage.
- **Runtime:** host-scoped descriptors, opt-in reflection discovery, runtime identity, cycle-safe graph traversal, live scalar operations, synchronous actions, finite collection snapshots, structured failures, and a deterministic cyclic sample.
- **CLI:** descriptor-based navigation and operations; PrettyPrompt editing, process-local history, and descriptor-aware completion.
- **Tests:** framework, CLI, dependency, and legacy-characterization projects exist.
- **Legacy:** the remaining tree-based `Dashboard`/`Node` code is characterized reference material, not the new API foundation.
- **Missing:** product TUI, layouts, batch engine, and supported package.
- **Packaging:** disabled until the Product MVP release gate defines APIs, tests, licensing, versioning, compatibility, and support.
- **Credential history:** the removed credential is revoked but remains in pre-cleanup Git history. History rewriting is out of scope.

## Settled Decisions

- **Name:** UIEngine is the final product, package, and namespace prefix.
- **Compatibility:** clean public API break. No `Dashboard`/`Node` adapter.
- **Framework:** .NET 10. The .NET 8 upgrade was routine LTS maintenance, not an MVP dependency.
- **Exposure:** explicit opt-in. Prefer properties; allow fields through normalized exposure.
- **Trust:** in-process trusted developer tool. Remote access requires a later protocol.
- **Framework MVP:** new core plus minimal CLI.
- **Product MVP:** TUI over the Framework MVP. Avalonia is not the reference frontend.
- **Identity:** stable runtime identity is required. Optional domain identity comes from an interface, attribute, or registry callback through one provider.
- **Layouts:** persist versioned component configuration and bindings, not domain state. Geometry is frontend-owned.
- **Batch:** serializable descriptors, predefined filters, compatibility preview, and sequential execution by default.
- **Transactions:** no automatic rollback for arbitrary side effects. Report partial success.
- **Packaging:** publish only after APIs, tests, licensing, versioning, and release policy are ready.
- **Projects:** use one `Core` library for runtime contracts, attributes, and reflection. Keep executables separate. Add assemblies only for real dependency, deployment, packaging, target-framework, or tooling boundaries.

## Architectural Invariants

1. **Live domain authority:** use live objects or explicit adapters; do not require a duplicate UI model.
2. **Graph runtime:** cycles, shared references, replacement, and missing targets are normal.
3. **Separate identity and path:** runtime identity identifies an instance; optional domain identity may survive replacement/restart; paths support navigation and fallback.
4. **Semantic descriptors:** describe values, selections, references, collections, actions, and progress—not widgets.
5. **Host scope:** no global registries. Hosts must be isolated, configurable, disposable, and testable.
6. **Structured failures:** frontends must not parse exception strings for expected failures.
7. **Explicit affinity:** dispatch reads, writes, and calls when the domain requires it.
8. **Explicit collection modes:** distinguish live, snapshot, paged, and virtualized access. Ordinary browsing must not force eager materialization.
9. **Managed observation:** normalize .NET notifications, polling, and custom providers; dispose subscriptions safely.
10. **Capability negotiation:** never assume every frontend supports every interaction.
11. **Preview-first batch mutation:** snapshot targets, classify compatibility, apply an explicit policy, and record one terminal result per target.
12. **Explicit remote protocol:** use descriptor DTOs, commands, authentication, authorization, and audit behavior—not transparent object serialization.

## MVP Boundaries

### Framework MVP — Core + CLI

One non-trivial cyclic model must support:

- explicit root/member discovery;
- cycle- and shared-reference-safe navigation;
- validated live reads and writes;
- parameterized sync and async actions;
- selected external-state observation;
- progress, cancellation, and structured failures;
- collection browsing without forced eager materialization; and
- the same contracts for CLI and non-CLI frontends.

### Product MVP — TUI

The TUI must use the Framework MVP without core leakage and provide:

- object browsing with history or breadcrumbs;
- generated value editors and action forms;
- collection browsing;
- async progress, cancellation, and structured errors;
- explicit unavailable, missing, and mismatched states; and
- the CLI's cyclic-fixture workflow.

Not in either MVP: persistent layouts, batch operations, arbitrary scripting, automatic undo, remote access, source generation, advanced analytics, or extra frontends. Layouts and batch remain committed post-MVP work.

## Implementation Rules

### Structure

- Use the fewest assembly boundaries that preserve runtime and tooling separation.
- Use directories and namespaces for logical areas; they do not imply assemblies.
- `Core/Core.csproj`: `UIEngine.Core`, `UIEngine.Core.Attributes`, and `UIEngine.Core.Reflection`.
- `Frontend/Cli/Cli.csproj`: separate executable using `UIEngine.Frontend.Cli`.
- `Examples/CyclicDomain/CyclicDomain.csproj`: deterministic example using `UIEngine.Examples.CyclicDomain`.
- Examples and tests get projects only when their execution/hosting boundary requires one.
- Hosting, layouts, and batch start as logical areas unless a real boundary appears.
- The Product TUI is a separate executable. Source generators may need a compiler-facing project.
- Public type names and justified package/assembly boundaries may evolve. Frontend independence and dependency direction may not.

### Code Style

- Add short type summaries when they clarify a class or struct's role.
- Private members: underscore plus PascalCase, such as `_Field`.
- Constants and static-readonly fields: `UPPER_SNAKE_CASE`; private ones keep the leading underscore.
- Enum members: `UPPER_SNAKE_CASE`.
- Keep required names such as constructors and `Main` unchanged.
- CA1707 is disabled solution-wide because it conflicts with these conventions.

### Migration and Verification

- Build the reboot beside the remaining legacy project.
- Extract deterministic fixtures and narrow characterization tests before legacy removal.
- Do not preserve accidental legacy API behavior.
- Minimum repository check: `dotnet build UIEngine.sln`.
- Add the smallest relevant automated tests for each behavior change.

## Near-Term Priorities

1. Programmatic exposure and stable domain identity providers.
2. Durable logical paths, binding states, and recovery.
3. Dispatcher-mediated validation, reads, writes, and calls.
4. Normalized state and collection observation.
5. Remaining Framework MVP hardening in `REBOOT_PLAN.md`.

## Deferred Decisions

Do not invent before the relevant roadmap phase:

- final public signatures beyond established descriptor roles and invariants;
- complete logical path grammar;
- package versioning and long-term support;
- source-generator design and AOT matrix;
- TUI toolkit and terminal composition model;
- remote transport, authentication, and authorization; and
- additional frontend commitments.

Resolve each with tests or a concrete product requirement. Update this file when the result becomes a stable project-wide constraint.
