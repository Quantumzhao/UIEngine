# Milestone 02 — Framework MVP Interaction Hardening

**Status:** Complete

> Invocation lifetime details in this completed milestone were superseded by
> [Method-Node Invocation Ownership](../method-node-invocation-ownership.md). Method-node
> occurrences now own observation state, and host disposal does not alter started domain tasks.

## Goal

Make live graph interaction safe under replacement, asynchronous work, thread affinity, large
collections, and structured failures.

## Delivered

- Runtime handles distinct from logical paths.
- Canonical structured paths with list-index and dictionary-key segments.
- Replacement-aware path and member resolution that follows the current graph.
- Invariant conversion, nullability, enum, range, and DataAnnotations validation.
- Bounded collection windows for indexed and lazy sources.
- Structured entries for null, scalar, reference, and dictionary values.
- Synchronous and asynchronous method invocation with lifecycle status.
- Synchronous, reentrant live operations owned by the domain model's thread.
- Deterministic invocation completion and structured outcomes.
- Black-box framework and CLI coverage of the complete cyclic-world workflow.

## Acceptance

- Replacement objects are resolved through their current paths.
- Ordinary browsing never performs an unbounded collection read.
- Expected failures return structured codes and validation issues.
- Invocation completion remains deterministic.
- Core and CLI tests pass with a zero-warning build.
