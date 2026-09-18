# UIEngine Milestone Outlines

This directory contains bounded, implementation-ready plans for UIEngine milestones. An outline records the decisions, dependencies, exclusions, verification, and exit criteria needed to execute one milestone without redefining the wider product.

Milestone outlines are derived from the repository's governing documents:

- [PROJECT_CONTEXT.md](../../PROJECT_CONTEXT.md) records settled product intent and architectural constraints.
- [runtime-domain-workbench-design.md](../../runtime-domain-workbench-design.md) describes the target architecture and behavior.
- [REBOOT_PLAN.md](../../REBOOT_PLAN.md) contains the overall migration roadmap.
- [TODO.MD](../../TODO.MD) remains the concise progress tracker.

An outline does not override those documents or authorize implementation by itself. If an outline conflicts with them, implementation must pause until the product owner resolves the conflict and every affected document is updated.

## Naming

Use `NN-short-descriptive-name.md`, with two-digit chronological numbers allocated once and never reused. Keep filenames stable when milestone status changes.

## Statuses

- **Draft:** under discussion and not ready to execute.
- **Planned:** decision-complete, but not authorized or started.
- **Active:** explicitly selected for implementation.
- **Complete:** implemented and verified against its exit criteria.
- **Superseded:** retained as history but replaced by a linked outline or decision.

## Index

| Milestone | Status | Summary |
|---|---|---|
| [01 — Architecture Runway and CLI Walking Skeleton](01-architecture-runway-cli-walking-skeleton.md) | Planned | Establish the safety baseline and prove a minimal graph-aware runtime through the CLI. |
