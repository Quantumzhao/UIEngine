# Milestone 02 — Framework MVP Interaction Hardening

**Status:** Superseded — capability outcomes retained by the streamlined Core

**Historical acceptance:** Local and artifact-free CI build/test/package-equivalent checks passed
on 2026-09-21: Debug and Release builds completed with zero warnings/errors, all 134 tests passed,
packaging remained disabled, and the clean output contained no package. The configured GitHub
Actions run, including its secret scan, was pending when this milestone record was written.

**Current acceptance:** After streamlining, Debug and Release builds complete with zero warnings
or errors and all 22 black-box tests pass. The smaller suite replaces tests of provider precedence,
configuration snapshots, decorators, paging modes, and other removed implementation choices.

**Prerequisite:** [Milestone 01 — Architecture Runway and CLI Walking Skeleton](01-architecture-runway-cli-walking-skeleton.md) is complete, and its post-acceptance repository baseline has been reconciled as described below.  
**Governing documents:** [Project context](../../PROJECT_CONTEXT.md) and [architecture and reboot plan](../../REBOOT_PLAN.md)

This outline does not authorize implementation. Work begins only when the milestone is explicitly selected for implementation.

> **Historical record:** The requirements below explain why identity, replacement-safe binding,
> dispatch, observation, bounded collections, and normalized invocation exist. Provider registries,
> adapters, policy interfaces, paging modes, `ValueTask` actions, and presentation metadata named in
> the original outline were later judged speculative and removed. Consult
> [the current architecture](../architecture.md) for implemented API shapes.
> The original checklist is intentionally frozen at its last milestone state; unchecked entries in
> that checklist are historical, not current work. [TODO.MD](../../TODO.MD) is the current tracker.

## Summary

Complete the Framework MVP by hardening the Milestone 01 vertical slice into a durable, observable, thread-affine interaction runtime:

`Host policy and exposure -> runtime/domain identity -> logical paths and bindings -> dispatched validation and operations -> observation, scalable collections, and invocations -> CLI`

Milestone 01 proved that semantic descriptors can navigate and operate on a live cyclic graph. This milestone makes those contracts suitable for the Product MVP TUI by adding programmatic exposure, replacement-safe binding, complete availability and validation semantics, host-provided dispatch, managed observation, scalable collection access, normalized asynchronous invocation, and structured diagnostics.

The Milestone 01 public contracts are an implementation runway rather than a compatibility promise. They may be refined where Phase 2 semantics require it, but the graph model, frontend independence, host scope, live-domain authority, and structured-failure model must remain intact.

This milestone excludes the product TUI, frontend capability negotiation, layouts and layout serialization, batch operations, direct collection mutation commands, packaging, legacy retirement, source generation, search/indexing, remote access, and additional frontends.

## Streamlining Reconciliation

All user-visible Framework MVP workflows survived the consolidation:

- runtime and domain identity, cycles, shared references, and compatible replacement;
- canonical paths with index, key, and identity selectors;
- replacement-safe bindings that refuse conflicting identities;
- nullable metadata, conversion, enum/range/DataAnnotations validation, and read-only values;
- dispatcher routing, reentrancy, queued cancellation, and host disposal;
- property and collection notifications, source replacement, explicit polling, and visible overflow;
- bounded arrays, lists, dictionaries, lazy enumerables, and null/scalar/reference entries;
- synchronous and `Task` actions with defaults, progress, cancellation, and fault capture; and
- the complete CLI command, completion, observation, and cyclic-world workflow.

The following original mechanisms were superseded:

| Original Milestone 02 mechanism | Current mechanism |
|---|---|
| Mutable `ExposureRegistry` with role builders and factories | Immutable scalar `TypeExposure<T>` definitions |
| Provider precedence and descriptor factories | Built-in reflection plus exact-type scalar exposure |
| Sixteen public interfaces | Concrete descriptors; two intentional public interfaces |
| `ObjectIdentity` wrapped by `ObjectHandle` | One `ObjectHandle(Guid Id)` |
| Resolver services, state objects, and fallback policies | Host methods returning resolved records directly |
| Capability flags, snapshots, pages, ranges, and continuations | One bounded collection-slice operation |
| Observation adapters and multiple mode/disposal interfaces | Built-in notifications or explicit polling |
| Invocation interfaces, fault wrappers, timestamps, and `CREATED` | Concrete four-state `ActionInvocation` |
| `ValueTask` action support | Sync and `Task`/`Task<T>` actions |
| Units, tags, risk, confirmation, and validation summaries | Editing metadata with demonstrated consumers only |
| Broad lifecycle/event diagnostics | Logging only unexpected runtime faults |
| 134 implementation-preserving tests | 22 black-box framework and CLI behavior tests |

The legacy project was retired after its useful concepts had been preserved in the cyclic fixture
and public behavior tests. This is a later repository state, not a revision of the historical
acceptance result.

## Historical Repository Baseline and Entry Gate

At the Phase 2 entry gate, the implementation provided:

- a single `Core` assembly with host-scoped replaceable roots, reference-based runtime identity, lazy domain-identity indexing, reflection discovery, semantic descriptor interfaces, structured results, scalar conversion, finite collection snapshots, and synchronous actions;
- a separate CLI with descriptor-aware completion and `ls`, `cd`, `inspect`, `get`, `set`, `call`, and `exit`;
- a deterministic cyclic fixture; and
- framework, CLI, dependency, and legacy-characterization test projects.

The Phase 2 entry gate was reconciled on 2026-09-19:

- the post-acceptance removal of `Dataset` and `CLITestProject` is accepted as intentional cleanup because their useful fixture role is retained by `Examples/CyclicDomain`;
- the five behavior-level CLI integration tests were restored against the reorganized project paths and namespaces;
- the then-current documents described the remaining legacy project consistently; and
- `dotnet build UIEngine.sln` completed with zero warnings and errors, and `dotnet test UIEngine.sln --no-build` passed all 29 tests (18 framework, 6 CLI, and 5 legacy-characterization tests).

This is the Phase 2 starting baseline. The historical Milestone 01 acceptance record remains unchanged.

## Architecture and Project Boundaries

- Keep the Framework MVP in the existing `Core`, `Cli`, `CyclicDomain`, and test projects. Use namespaces and directories for new logical areas; do not add assemblies without a concrete dependency, deployment, packaging, target-framework, or tooling boundary.
- Keep all exposure registrations, identities, dispatchers, subscriptions, binding indexes, invocations, and diagnostics scoped to one `UIEngineHost`. Do not introduce static mutable registries.
- Make host lifetime explicit. Host disposal must stop polling, detach notification handlers, complete subscriptions, and request cancellation of host-owned invocations without leaking domain objects.
- Keep descriptors semantic and frontend-neutral. No terminal controls, prompts, colors, key bindings, or TUI concepts belong in `Core`.
- Keep reflection as one provider behind normalized contracts. Programmatic exposure and custom adapters must not require a parallel domain model.
- Preserve dependency direction from frontends and examples toward Core. Core must not reference CLI, PrettyPrompt, or a future TUI toolkit.
- Keep package generation disabled.

## Contract Refinement Targets

Exact public signatures may be refined during implementation when tests or C# constraints require it. The following roles and separations are mandatory.

### Programmatic exposure and provider selection

- Add a host-configured exposure registry that can describe values, finite selections, references, collections, actions, summaries, validation, collection keys, and domain identity for types that cannot be annotated.
- Cover third-party types, fields, computed metadata, generic wrappers, instance- or role-dependent exposure, and custom descriptor factories.
- Freeze registration definitions when a host is built or first used. Runtime-dependent rules may evaluate a supplied context or instance, but mutating global metadata after discovery is not permitted.
- Define deterministic precedence between explicit registrations, custom descriptor providers, and annotated reflection fallback. Provider choice must not depend accidentally on enumeration order.
- Allow a registration to supplement or intentionally override reflection metadata. Reject duplicate explicit member identifiers, unsupported signatures, invalid identity definitions, and contradictory metadata as structured configuration or discovery failures.
- Continue caching immutable reflection/type metadata, but keep host-, instance-, role-, and live-state decisions out of the global type cache.
- Preserve stable descriptor identifiers. Existing deterministic overload disambiguation may remain, while explicitly assigned duplicate identifiers are invalid.

### Runtime and domain identity

- Keep runtime identity based on object reference and scoped to one host. Encountering a replacement object creates a new runtime identity even when it has the same domain identity.
- Add an optional domain-identity value and one normalized provider pipeline for interface-, attribute-, and registry-callback sources. Programmatic configuration takes explicit precedence; conflicting non-overridden definitions fail discovery rather than choosing arbitrarily.
- Validate domain identities as nonempty stable values. Two simultaneously resolvable objects with the same domain identity produce an ambiguous lookup; they must not silently share a runtime identity.
- Add the minimum root replacement/unregistration behavior needed to test recovery and to release the old root reference. Property and collection replacement must also be recoverable through re-resolution.
- Index only objects the host has encountered. Binding recovery may use a path to discover a replacement, but must not traverse the complete graph to build an eager identity index.
- Keep runtime identity, domain identity, and logical path as separate types and concepts. A path or domain key must never be substituted into `ObjectIdentity`.

### Logical paths

- Replace the CLI-private string splitter with a Core-owned immutable logical-path model, parser, canonical formatter, and asynchronous resolver.
- Define and test the Framework MVP grammar before implementing binding recovery. It must support:
  - case-sensitive root aliases and exposed member identifiers;
  - references and terminal value, action, and collection members;
  - explicitly allowed numeric index selectors;
  - provider-defined key selectors and domain-identity selectors for collections;
  - escaped delimiter characters and a canonical parse/format round trip; and
  - structured invalid, missing, ambiguous, type-mismatched, and unavailable outcomes.
- Absolute paths are the durable Core representation. CLI conveniences such as `cd ..` operate on navigation history and canonical absolute paths rather than adding parent segments to the binding grammar.
- Retain the Milestone 01 numeric collection path as accepted CLI input if practical, but format a single canonical selector form. Do not promise index stability.
- Predicates and arbitrary query expressions remain deferred. Path resolution is semantic descriptor traversal, not raw CLR member traversal.

### Binding references and recovery

- Add a serializable, data-only binding reference containing optional domain identity, optional logical path, member identifier, expected descriptor kind, optional expected type, and explicit fallback policy.
- Add a binding resolver that returns a target plus one explicit state: `RESOLVED`, `TEMPORARILY_UNAVAILABLE`, `TYPE_MISMATCH`, `TARGET_MISSING`, `AMBIGUOUS`, `PERMISSION_DENIED`, or `INVALID_PATH`.
- Resolve runtime handles only for the duration of live interaction. Durable records must not rely on process-local GUIDs.
- Prefer domain identity when the fallback policy permits it. Path fallback may recover a replacement only when identity and expected descriptor guards remain compatible.
- Never use a stale path to bind silently to a different domain identity. Return a mismatch or repair candidate instead.
- Return an updated canonical path as resolution metadata when a stable-identity target has moved, but do not mutate the caller's stored binding implicitly.
- Exercise recovery after root, reference, and collection-element replacement. Exercise broken, ambiguous, mismatched, invalid, permission-denied, and temporarily unavailable states.
- Provide binding primitives only. Layout files, migrations, repair UI, and persistent component models remain post-MVP work.

### Availability, values, selections, and validation

- Represent successful `null` or empty reference values distinctly from an unavailable containing object, missing member, failed getter, or unresolved binding.
- Use availability-bearing results where a nullable payload alone is ambiguous. Observation payloads must likewise distinguish “old/new value not supplied” from “old/new value supplied as null.”
- Add finite-selection semantics for enums and provider-defined option sets so frontends do not have to infer them from CLR types.
- Complete value and action-parameter metadata for CLR type, nullability, requiredness, presence of a default, default value, range, finite options, units/tags where supplied, and readable/writable state.
- Normalize common reflection validation metadata, including nullable annotations and supported data-annotation rules, behind frontend-neutral validation contracts. Programmatic providers may supply synchronous or asynchronous domain rules.
- Run conversion before value validation, and validate a complete action argument set before invocation. Dynamic validation that reads domain state must use the host dispatcher.
- Treat setter rejection, current-state rejection, permission-like rejection, and action precondition rejection as structured outcomes with stable codes and field/member context.
- Do not treat risk or confirmation metadata as authorization. Record action danger/confirmation hints for future frontends, but leave authorization and remote security deferred.

### Dispatch and host lifetime

- Add a host-provided interaction dispatcher with an inline default for thread-safe domains.
- Route all live domain access that can execute user code through it: summary getters, value/reference reads, validation, writes, collection access, observation setup when required, and action invocation.
- Allow a provider to mark an operation safe for direct execution only through an explicit contract. Reflection operations default to dispatched access.
- Preserve cancellation while work is queued and map dispatch rejection/failure to structured results and diagnostics.
- Define reentrancy behavior so a dispatcher can recognize its own context without deadlocking nested UIEngine operations.
- Make disposal idempotent. After disposal, new registrations, resolutions, subscriptions, and invocations fail predictably; active host-owned resources are released or completed.
- Verify dispatch with a deterministic single-thread or recording dispatcher rather than depending on a real UI framework.

### State and collection observation

- Add one normalized change contract carrying runtime identity, optional domain identity, member identity, change kind, optional old/new values, and an ordering token or timestamp.
- Adapt `INotifyPropertyChanged` and `INotifyCollectionChanged`, including empty property names and multi-item add/remove, replace, move, and reset events.
- Provide host-configured polling for selected exposed members and an extension point for custom change providers. Polling is opt-in, cancellable, and uses the dispatcher.
- Return explicit disposable subscription ownership to consumers while also tracking subscriptions at host scope. Disposal must be safe, idempotent, and free of duplicate or stale event handlers.
- Preserve per-source event order. Define bounded buffering or coalescing behavior so a slow frontend cannot grow memory without limit; overflow must be observable rather than silent.
- Re-observe or re-resolve when a bound reference or collection is replaced. Do not keep handlers attached to the old source.
- Surface adapter, polling, getter, and subscriber failures through structured diagnostics without terminating unrelated subscriptions.

### Collection access

- Replace the single unbounded `SnapshotAsync` shape with explicit access modes. A descriptor advertises only the modes it can honor: live observation, finite snapshot, page, or virtualized range.
- Make snapshot requests explicit and bounded. Unknown, lazy, one-shot, or potentially unbounded `IEnumerable` sources must never be fully materialized merely for browsing.
- Define page/range requests with validated offset, limit, cancellation, optional total count, continuation/has-more information, and a host-configurable maximum page size.
- Model collection entries so object references, scalar values, dictionary entries, and null elements are representable without inventing wrapper domain objects or failing an entire page.
- Preserve runtime and optional domain identity for object entries and expose stable provider keys where available. Index is a location hint, not identity.
- Adapt arrays, generic and nongeneric finite collections, dictionaries, observable collections, and explicitly registered paged/virtual sources according to their real capabilities.
- Do not advertise paging when an adapter can only re-enumerate an arbitrary sequence from the beginning. Do not advertise liveness merely because a fresh snapshot can be requested.
- Normalize external collection changes and replacement/reset behavior. Direct add/remove/move APIs are outside this milestone; domain collection mutation remains available only through explicitly exposed actions.

### Actions, invocations, progress, and cancellation

- Replace direct result-returning invocation with a normalized invocation object or equivalent lifecycle contract shared by synchronous and asynchronous actions.
- Model at least created/running, succeeded, failed, and cancelled states; one completion result; optional ordered progress; captured fault details; and whether cancellation can be requested.
- Support reflection actions returning `void`, values, `Task`, `Task<T>`, `ValueTask`, and `ValueTask<T>`.
- Treat supported infrastructure parameters such as `CancellationToken` and `IProgress<T>` as provider-supplied inputs rather than user form fields. Reject unsupported `ref`, `out`, open-generic, or ambiguous infrastructure signatures during discovery.
- Expose user parameters with nullability, defaults, validation, and finite options. Preserve case-sensitive named binding and reject unknown or duplicate arguments.
- Offer cancellation only when the underlying adapter can honor it. Cancellation requests are structured and idempotent; `OperationCanceledException` is classified as cancellation only when associated cancellation was requested.
- Normalize progress independently of the CLR return type. Do not emit progress after a terminal result, and do not let slow progress consumers block the domain dispatcher indefinitely.
- Observe asynchronous exceptions exactly once and convert expected invocation, validation, cancellation, and target-loss cases to structured results. Unexpected faults may retain captured exception details for trusted local diagnostics.
- On host disposal, request cancellation for host-owned active invocations and complete their public streams deterministically.

### Structured diagnostics

- Integrate Core with `Microsoft.Extensions.Logging` abstractions without selecting or configuring a logging provider. Do not add the full generic host solely for logging.
- Emit stable event identifiers and structured properties for discovery/configuration, binding resolution and breakage, validation and mutation, invocation lifecycle, observation faults, collection access, dispatch failure, and host disposal.
- Define the capability-mismatch diagnostic category needed by the roadmap, but defer full frontend capability declaration and negotiation to Milestone 03.
- Avoid logging raw values, arguments, domain keys, or exception details by default when they may contain sensitive application data. Allow hosts to opt into richer trusted-development diagnostics deliberately.
- Diagnostics supplement operation results; frontends must not scrape logs to determine interaction outcomes.

## CLI Framework-MVP Slice

Keep the CLI as the proving frontend and retain all Milestone 01 commands. Move path parsing, resolution, binding, collection access, observation, and invocation semantics into Core rather than expanding CLI-owned runtime logic.

Extend behavior as follows:

- `ls` continues to list roots or current descriptors. `ls <collection> [offset=<n>] [limit=<n>]` lists one bounded page and reports access mode, positions/keys, null entries, continuation, and optional total count.
- `cd <path>` accepts the complete logical-path grammar. The current location retains a binding/path record rather than only a stale runtime handle so compatible replacement can recover.
- `inspect` displays runtime identity, optional domain identity, canonical path, availability, validation and selection metadata, collection modes, and action async/progress/cancellation metadata.
- `get` and `set` preserve a successful `null` distinctly from target or binding failure and present structured validation details.
- `call` uses the same syntax for synchronous and asynchronous actions, prints normalized progress, waits for one terminal result, and allows command cancellation only when the invocation supports it.
- `watch <member>` and `watch <collection>` display normalized changes until cancelled. Tests drive this with deterministic cancellation rather than wall-clock delays.
- `exit` disposes the session's subscriptions and host cleanly.

Command verbs remain case-insensitive; exposed identifiers, argument names, keys, and domain identities remain case-sensitive unless a provider explicitly defines otherwise. Completion uses descriptor metadata and bounded collection/key information; it must not enumerate an arbitrary collection eagerly.

Expected path, binding, availability, validation, dispatch, collection, observation, invocation, and cancellation failures print stable structured codes and do not terminate the session. PrettyPrompt remains an editing adapter; redirected-input tests exercise the same session behavior without a terminal.

## Acceptance Fixture

Extend the deterministic cyclic fixture or add tightly related test fixtures to cover:

- cyclic and shared references with stable runtime identity;
- interface-, attribute-, and registry-callback domain identity sources;
- an unannotated third-party-style type exposed programmatically;
- null references and scalar null values;
- root, reference, and collection-element replacement with the same domain identity;
- duplicate domain identities and every binding resolution state;
- independently mutated `INotifyPropertyChanged` and `INotifyCollectionChanged` sources;
- arrays, dictionaries, null collection elements, and a large/lazy collection that detects over-enumeration;
- dispatched getters, validation, writes, collection access, and calls;
- synchronous, asynchronous, progress-reporting, cancellable, validation-rejected, permission-like rejected, and failing actions; and
- deterministic gates or manually advanced progress rather than timing-sensitive sleeps.

The default CLI fixture should remain understandable to a person. Specialized edge cases may live in test-only fixtures rather than turning the example domain into a test harness.

## Verification and Test Scenarios

- Exposure tests cover programmatic third-party exposure, registration/reflection precedence, runtime-dependent metadata, fields, summaries, finite selections, duplicate identifiers, invalid signatures, and cache isolation between hosts.
- Identity tests cover runtime identity isolation, all domain-identity sources, invalid and duplicate domain identities, root/reference/element replacement, and absence of eager graph indexing.
- Path tests cover parse/format round trips, escaping, roots, references, terminal members, index/key/identity selectors, malformed inputs, and case sensitivity. Use deterministic generated inputs for property-style coverage.
- Binding tests cover every resolution state, explicit fallback policies, moved targets, replacement with the same identity, stale paths resolving to a different identity, type/kind guards, and canonical-path suggestions.
- Value and validation tests cover successful null, unavailable targets, nullability, default-presence versus a null default, selections, common annotations, asynchronous rules, setter rejection, action preconditions, and permission-like rejection.
- Dispatcher tests prove that reads, validation, writes, collection access, observation setup, and calls run on the configured context; cancellation, dispatch failure, reentrancy, and disposal are deterministic.
- Observation tests cover property-wide invalidation, multi-item collection changes, replace/move/reset, ordering, coalescing or bounded overflow, source replacement, duplicate prevention, consumer disposal, and host disposal.
- Collection tests cover advertised access modes, page bounds, optional counts, continuation, scalar/object/null entries, arrays, nongeneric sequences, dictionaries, cancellation, one-shot enumeration, and the guarantee that ordinary browsing never materializes an unbounded source.
- Invocation tests cover every supported sync/async return shape, parameter metadata and binding, success, validation failure, target loss, task failure, ordered progress, supported/unsupported cancellation, cancellation races, and exactly one terminal outcome.
- Diagnostic tests use a recording logger to verify event IDs and structured context without relying on rendered messages or leaking raw values.
- CLI integration tests exercise the complete Milestone 01 surface plus canonical paths, replacement recovery, paged collection listing, null versus unavailable output, validation, watch/disposal, async completion/failure, progress, and cancellation.
- Dependency tests continue to prove that Core is frontend-independent and no new implementation project references the legacy assembly.
- Repository checks continue to prove that packaging is disabled, no normal build creates a `.nupkg`, and the current tree contains no credential.

## Exit Criteria

- The post-Milestone 01 repository baseline is explicitly reconciled, and acceptance-equivalent CLI behavior tests are present.
- Programmatic and reflection exposure produce the same semantic descriptor roles without global registration state or ambiguous provider selection.
- Runtime identity remains reference-based; optional domain identity and complete logical paths recover compatible replacements without silently rebinding to a different target.
- Every binding state and fallback policy is represented and tested.
- Successful null/empty values are distinct from missing, unavailable, mismatched, ambiguous, denied, and faulted states.
- Validation, live reads/writes, collection access, and calls honor the configured dispatcher and return structured expected failures.
- Property and collection changes are normalized, ordered, and safely disposed without duplicate or stale subscriptions.
- Large, lazy, one-shot, and virtualized collections can be browsed through bounded access without unconditional materialization.
- Synchronous and asynchronous actions share one invocation lifecycle with completion, progress, supported cancellation, and exception capture.
- Structured diagnostics cover Phase 2 runtime behavior without becoming a second error protocol or exposing raw domain data by default.
- The CLI covers discovery, navigation, inspection, get/set, sync/async calls, progress, cancellation, observation, and bounded collection listing over the non-trivial cyclic fixture.
- The complete framework, CLI, dependency, repository, and legacy-characterization suite passes.
- `dotnet build UIEngine.sln` succeeds with zero warnings and errors, and CI passes from a clean checkout.
- No TUI, layout persistence, batch engine, package, remote protocol, or legacy-removal work has been started as part of this milestone.
- Only genuinely completed items are checked in [TODO.MD](../../TODO.MD).

## Progress Checklist Alignment

The Phase 2 audit found several requirements that were implicit or absent in `TODO.MD`. The checklist should explicitly track:

- reconciliation of the current post-acceptance baseline and restoration of behavior-level CLI coverage;
- successful null/empty state versus unavailable or missing state;
- finite-selection, parameter, validation, and action execution metadata;
- explicit host lifetime and subscription/invocation disposal;
- structured diagnostics and logging; and
- the concrete CLI observation, bounded collection, async progress, and cancellation surface.

The cross-roadmap comparison also found later omissions: frontend capability declaration/negotiation and terminal resize/keyboard acceptance in Product MVP, complete release compatibility/support/platform policy, advanced layout recovery/templates, and batch confirmation/dry-run/error policies. Those belong in their own milestones but should remain visible in the progress checklist.

## Implementation Steps

Keep the repository buildable after every step. Add the smallest relevant tests with each contract or behavior; do not postpone coverage to the final acceptance pass.

### 1. Reconcile and lock the starting baseline

1. [x] Decide whether the post-acceptance removal of CLI behavior tests, `Dataset`, and `CLITestProject` is intentional.
2. [x] Update the current-state documents consistently without changing the historical Milestone 01 acceptance record.
3. [x] Re-establish acceptance-equivalent tests for the retained Milestone 01 CLI commands and cyclic workflow.
4. [x] Run the current solution build and test suite and record the new Phase 2 starting baseline.

Stop here if the intended repository state cannot be resolved.

### 2. Establish host services, lifetime, and diagnostics

1. [x] Add explicit host configuration for provider precedence, dispatcher, identity providers, observation adapters, collection limits, and logging.
2. [x] Add an inline dispatcher and a deterministic test dispatcher.
3. [x] Make the host disposable and define post-disposal outcomes.
4. [x] Add structured diagnostic event IDs and a recording-logger test harness, keeping value logging redacted by default.

### 3. Add programmatic exposure and complete metadata

1. [x] Implement the host-scoped registration model and deterministic composition with reflection.
2. [x] Add value, selection, reference, collection, action, summary, validation, key, and identity registrations needed by the acceptance fixtures.
3. [x] Separate immutable type metadata from host-, context-, instance-, and live-state evaluation.
4. [x] Validate definitions and provider selection before executing live operations.

### 4. Normalize domain identity and replacement

1. [x] Add normalized interface, attribute, and callback identity sources with documented precedence.
2. [x] Track encountered domain identities without eagerly walking the graph.
3. [x] Add structured duplicate/ambiguous identity handling.
4. [x] Add root replacement/unregistration and tests proving new runtime identity, stable domain identity, and release of old registrations.

### 5. Implement logical paths and binding resolution

1. [x] Freeze the initial grammar, escaping, selector forms, and canonical formatting in contract tests.
2. [x] Implement semantic parsing and asynchronous resolution for roots, members, references, and supported collection selectors.
3. [x] Add data-only binding references, fallback policies, kind/type guards, and all resolution states.
4. [x] Prove recovery and refusal cases for root, reference, and collection-element replacement.

### 6. Complete availability and validation semantics

1. [x] Refine operation payloads so null, empty reference, absent metadata, unavailable target, missing member, and failed access remain distinct.
2. [x] Add nullability, default-presence, finite-selection, range, and programmatic validation metadata.
3. [x] Apply conversion and validation in a consistent order for writes and action arguments.
4. [x] Add structured, contextual issues for value, parameter, action, and permission-like rejection.

### 7. Route live access through dispatch

1. [x] Move reflection summary, value, reference, collection, validation, mutation, and invocation access behind the configured dispatcher.
2. [x] Define provider opt-out, reentrancy, cancellation, and dispatch-failure behavior.
3. [x] Verify that no CLI or descriptor implementation bypasses dispatch for live domain access.
4. [x] Test queued cancellation and host disposal without a real UI thread.

### 8. Add normalized observation

1. [x] Introduce the change record and disposable subscription contracts.
2. [x] Adapt property and collection notifications completely, including wildcard property changes and all collection event shapes.
3. [x] Add polling and custom-adapter extension points with dispatch, cancellation, bounded buffering, and diagnostics.
4. [x] Handle source replacement, consumer disposal, and host disposal without stale or duplicate handlers.

### 9. Add bounded collection modes

1. [x] Introduce collection capability flags, entry semantics, bounded requests, page/range results, and continuation metadata.
2. [x] Adapt common reflection collection shapes according to their actual finite, indexed, keyed, and observable capabilities.
3. [x] Add custom provider support for native paging and virtualization.
4. [x] Prove null/scalar/reference entries, dictionaries, one-shot sequences, external mutations, and non-materialization of large/lazy sources.

### 10. Normalize action invocation

1. [x] Add invocation status, completion, progress, fault, and cancellation contracts.
2. [x] Adapt synchronous results and all supported `Task`/`ValueTask` return shapes.
3. [x] Classify user parameters versus injected cancellation/progress parameters and complete their metadata and validation.
4. [x] Test completion, task failure, progress ordering, cancellation support and races, target loss, and host disposal.

### 11. Extend the acceptance fixture and CLI

1. [x] Add readable deterministic examples for programmatic exposure, domain identity, replacement, notification, scalable collection, validation, and async action behavior.
2. [x] Replace CLI-private path and snapshot logic with Core path, binding, collection, observation, and invocation services.
3. [x] Extend `ls`, `cd`, `inspect`, `get`, `set`, and `call`; add `watch`; and update descriptor-aware completion without eager enumeration.
4. [x] Add redirected-input/session integration tests for the complete Framework MVP workflow, including failure and cancellation paths.

### 12. Run milestone acceptance

1. [x] Run all exposure, identity, path, binding, availability, validation, dispatcher, observation, collection, invocation, diagnostic, CLI, dependency, repository, and legacy-characterization tests.
2. [ ] Run `dotnet build UIEngine.sln`, requiring zero warnings and errors, then verify the same checks from a clean checkout in CI. The local build and artifact-free CI build/test/package-equivalent Release checks pass; the GitHub Actions run awaits publication.
3. [x] Confirm that packaging remains disabled, no package is produced, and no deferred subsystem entered the implementation.
4. [ ] Confirm every exit criterion above, update current-state documentation, and check only genuinely completed items in [TODO.MD](../../TODO.MD). Documentation and the checklist are current; remote CI is the only remaining exit criterion.

Milestone 03 may select and build the Product MVP TUI only after this milestone satisfies every exit criterion.
