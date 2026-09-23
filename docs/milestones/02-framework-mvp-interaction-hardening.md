# Milestone 02 — Framework MVP Interaction Hardening

**Status:** Complete

## Goal

Make live graph interaction safe under replacement, asynchronous work, thread affinity, large
collections, and structured failures.

## Delivered

- Runtime handles distinct from logical paths.
- Canonical percent-escaped paths with index and key collection selectors.
- Replacement-aware path and member resolution that follows the current graph.
- Invariant conversion, nullability, enum, range, and DataAnnotations validation.
- Bounded collection windows for indexed and lazy sources.
- Structured entries for null, scalar, reference, and dictionary values.
- Synchronous and asynchronous method invocation with lifecycle status.
- Synchronous, reentrant live operations owned by the domain model's thread.
- Deterministic host and invocation lifetime.
- Black-box framework and CLI coverage of the complete cyclic-world workflow.

## Acceptance

- Replacement objects are resolved through their current paths.
- Ordinary browsing never performs an unbounded collection read.
- Expected failures return structured codes and validation issues.
- Invocation completion remains deterministic.
- Disposing the host completes owned work deterministically.
- Core and CLI tests pass with a zero-warning build.
