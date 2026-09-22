# Milestone 02 — Framework MVP Interaction Hardening

**Status:** Complete

## Goal

Make live graph interaction safe under replacement, asynchronous work, thread affinity, large
collections, and observable change.

## Delivered

- Optional stable domain identity distinct from runtime handles and logical paths.
- Canonical percent-escaped paths with index, key, and identity collection selectors.
- Replacement-aware path and member resolution with identity conflict detection.
- Invariant conversion, nullability, enum, range, and DataAnnotations validation.
- Bounded collection windows for indexed and lazy sources.
- Structured entries for null, scalar, reference, and dictionary values.
- Synchronous and asynchronous method invocation with bounded progress.
- Notification and explicit polling observation with visible overflow.
- `IInteractionDispatcher` for domain thread affinity and reentrant access.
- Deterministic host, invocation, and subscription lifetime.
- Black-box framework and CLI coverage of the complete cyclic-world workflow.

## Acceptance

- Compatible replacement can recover by path and stable identity.
- Conflicting or ambiguous identities fail explicitly.
- Ordinary browsing never performs an unbounded collection read.
- Expected failures return structured codes and validation issues.
- Observation and progress streams remain bounded.
- Disposing subscriptions detaches handlers.
- Disposing the host completes owned work deterministically.
- Core and CLI tests pass with a zero-warning build.
