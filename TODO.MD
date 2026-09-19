# UIEngine Progress

See [REBOOT_PLAN.md](REBOOT_PLAN.md) for rationale, dependencies, risks, and exit criteria.

## Reboot Decisions and Cleanup

- [x] Define the target architecture and MVP.
- [x] Audit the legacy implementation against the design.
- [x] Keep the UIEngine name and choose a clean API break.
- [x] Use UIEngine as the final package and namespace prefix.
- [x] Choose .NET 10 and a CLI-first vertical slice.
- [x] Define the framework MVP as Core + CLI and the product MVP as TUI.
- [x] Remove the credential-bearing NuGet configuration and packaging behavior.
- [x] Remove obsolete Docker, container-tool, and VS Code configuration.
- [x] Retarget the legacy solution to .NET 10.
- [x] Revoke the removed package credential through its provider.
- [x] Add characterization tests for legacy behavior worth preserving.
- [x] Enable nullable analysis and analyzers.
- [x] Add CI and clean-checkout verification.

## Framework MVP — Core and CLI

Detailed first milestone: [Architecture Runway and CLI Walking Skeleton](docs/milestones/0001-architecture-runway-cli-walking-skeleton.md).

- [x] Create the Framework, CLI, sample, and test projects with logical areas organized by namespace.
- [x] Implement host-scoped roots and opt-in descriptor discovery.
- [x] Implement runtime identity and cycle-safe traversal.
- [x] Implement structured interaction results.
- [x] Implement CLI navigation, inspection, get, set, and call commands.
- [x] Prove the vertical slice with shared-reference and cyclic fixtures.
- [ ] Add programmatic exposure and stable domain identity providers.
- [ ] Add logical paths, binding states, and binding recovery.
- [ ] Add validation and dispatcher-mediated reads, writes, and calls.
- [ ] Add normalized state and collection observation.
- [ ] Add collection snapshots, paging, and virtualization contracts.
- [ ] Add async actions, progress, and cancellation.
- [ ] Pass the framework MVP acceptance tests through the CLI.

## Product MVP — TUI

- [ ] Select a TUI toolkit without coupling it to the core.
- [ ] Build an object browser with navigation history or breadcrumbs.
- [ ] Build generated value editors and action forms.
- [ ] Add collection browsing.
- [ ] Add async progress, cancellation, and structured error presentation.
- [ ] Show unavailable, missing, and mismatched targets explicitly.
- [ ] Prove the TUI against the same cyclic fixture as the CLI.

## Product MVP Release and Legacy Retirement

- [ ] Pass core, CLI, and TUI acceptance tests.
- [ ] Replace useful legacy samples with maintained fixtures.
- [ ] Remove the legacy UIEngine, Dataset, and CLITest projects.
- [ ] Define licensing, versioning, packaging, and release policy.

## Post-MVP Layouts

- [ ] Add component creation, deletion, movement, containment, and rebinding.
- [ ] Add versioned portable layouts and broken-binding visualization.
- [ ] Add layout migrations and repair workflows.

## Post-MVP Batch Operations

- [ ] Add target snapshots and predefined member filters.
- [ ] Add per-target compatibility analysis.
- [ ] Add preview-first value assignment and action invocation.
- [ ] Add sequential execution, cancellation, and per-item results.
- [ ] Cover partial failure and non-transactional behavior.

## Post-MVP

- [ ] Add source generation and AOT support.
- [ ] Add search and indexing.
- [ ] Add tables, charts, and safe derived values.
- [ ] Design an authenticated remote protocol.
- [ ] Add additional frontends when justified.
