# UIEngine Progress

See [REBOOT_PLAN.md](REBOOT_PLAN.md) for architectural rationale, migration history, risks, and
the future roadmap. Completed milestone records remain under [docs/milestones](docs/milestones/).

## Reboot and Safety Baseline

- [x] Define the graph-oriented target architecture and MVP boundaries.
- [x] Audit the legacy implementation and choose a clean public API break.
- [x] Standardize the repository on .NET 10 with nullable analysis and analyzers.
- [x] Remove credential-bearing package configuration and revoke the credential.
- [x] Disable package generation until release policy exists.
- [x] Add clean restore, build, test, secret-scan, and no-package CI checks.
- [x] Prove the initial host-scoped graph runtime and CLI vertical slice.
- [x] Harden the Framework MVP with identity, paths, bindings, dispatch, validation, bounded
  collections, observation, asynchronous actions, progress, and cancellation.

## Core Streamlining

- [x] Replace descriptor interfaces and dispatch decorators with one concrete member hierarchy.
- [x] Retain only `IInteractionDispatcher` and `IStableDomainIdentity` as public extension
  interfaces.
- [x] Collapse runtime identity into `ObjectHandle(Guid Id)`.
- [x] Replace mutable programmatic registration with immutable scalar `TypeExposure<T>` and
  `ValueExposure<T,TValue>` definitions.
- [x] Replace parallel member collections with `ObjectDescriptor.Members`.
- [x] Replace collection modes, capability flags, and continuations with bounded slices.
- [x] Use closed null, scalar, and reference collection-entry records.
- [x] Return resolved paths and bindings directly through `InteractionResult<T>`.
- [x] Reduce binding recovery to optional identity plus required path with conflict refusal.
- [x] Reduce observation to built-in notifications or explicit polling with synchronous disposal.
- [x] Replace invocation interfaces and wrappers with concrete `ActionInvocation`.
- [x] Remove unused presentation metadata, provider policies, factories, and adapters.
- [x] Consolidate structured error categories and retain IDs on validation issues.
- [x] Standardize public asynchronous operations on `Task`.
- [x] Remove redundant runtime null guards covered by nullable contracts.
- [x] Simplify the serial CLI by removing unnecessary locking and async disposal.
- [x] Replace implementation-coupled tests with black-box framework and CLI behavior tests.
- [x] Retire the v0.2.3 project, its assets, and characterization suite.
- [x] Reconcile the README, project context, architecture, CLI reference, reboot plan, progress
  tracker, and historical milestone records.

## Current Acceptance

- [x] Debug solution build completes with zero warnings and errors.
- [x] Release solution build completes with zero warnings and errors.
- [x] All 14 framework behavior tests pass.
- [x] All 8 CLI behavior tests pass.
- [x] API-shape tests prevent removed provider, factory, policy, and identity types from returning.
- [x] Redirected CLI coverage includes navigation, mutation, bounded collections, observation,
  asynchronous progress, cancellation, and structured failures.
- [X] Confirm the streamlined runtime in GitHub Actions after publication.

## Product MVP — TUI

- [ ] Select a TUI toolkit without coupling it to Core.
- [ ] Define only the frontend capability negotiation required by the selected toolkit.
- [ ] Build graph browsing with navigation history or breadcrumbs.
- [ ] Generate value editors and action forms from the concrete descriptors.
- [ ] Add bounded collection browsing.
- [ ] Present asynchronous progress, cancellation, observation, and structured errors.
- [ ] Show unavailable, missing, ambiguous, and mismatched targets explicitly.
- [ ] Verify keyboard-first operation and terminal resizing.
- [ ] Prove the TUI against the same cyclic-world workflow as the CLI.

## Product Release

- [ ] Pass Core, CLI, and TUI acceptance from a clean checkout.
- [ ] Define licensing, versioning, package metadata, and release policy.
- [ ] Define supported platforms and public API compatibility/support policy.
- [ ] Enable package generation only after those policies are settled.

## Post-MVP Layouts

- [ ] Add component creation, deletion, duplication, movement, containment, and rebinding.
- [ ] Persist versioned component configuration and bindings rather than domain state.
- [ ] Visualize and repair broken bindings.
- [ ] Add layout migrations and reusable templates only with acceptance fixtures.

## Post-MVP Batch Operations

- [ ] Snapshot targets and support predefined member filters.
- [ ] Analyze compatibility and show a preview before mutation.
- [ ] Add value assignment and action invocation with explicit confirmation and dry-run behavior.
- [ ] Execute sequentially by default with explicit continue/stop policy and cancellation.
- [ ] Record one terminal result per item and cover partial, non-transactional failure.

## Later Capabilities

- [ ] Add source generation only with a defined AOT matrix.
- [ ] Add search and indexing.
- [ ] Add tables, charts, and safe derived values.
- [ ] Design an authenticated, authorized, and auditable remote protocol.
- [ ] Add other frontends only when justified by a concrete consumer.
