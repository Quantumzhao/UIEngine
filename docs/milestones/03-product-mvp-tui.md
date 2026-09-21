# Milestone 03 — Product MVP TUI

**Status:** In Progress

**Toolkit recommendation:** Adopt `XenoAtom.Terminal.UI` behind a UIEngine-owned adapter after the
Phase 1 compatibility spike passes. Pin the accepted package version; do not use a floating range.

**Research baseline:** `XenoAtom.Terminal.UI` 3.9.0, reviewed on 2026-09-21.

**Qualified version:** `XenoAtom.Terminal.UI` 3.9.0, accepted on 2026-09-21. See the
[qualification record](../spikes/xenoatom-terminal-ui-3.9.0.md).

**Prerequisite:** The streamlined Framework MVP capabilities described by
[Project context](../../PROJECT_CONTEXT.md) and [the current architecture](../architecture.md) are
the starting contract. Milestone 02's capability outcomes are retained even though its original
implementation shape was superseded.

**Governing documents:** [Project context](../../PROJECT_CONTEXT.md),
[architecture and reboot plan](../../REBOOT_PLAN.md), and [progress tracker](../../TODO.MD)

This outline does not authorize implementation. Work begins only when the milestone is explicitly
selected for implementation.

## Summary

Deliver the first product frontend as a reusable .NET class library:

`Domain model -> UIEngine.Core host and descriptors -> UIEngine TUI session -> XenoAtom visuals -> caller-owned terminal application`

The TUI will turn an exposed live object graph into a keyboard-first interactive application with
no model-specific screens. It will browse roots, references, cycles, and bounded collections;
generate value editors and action forms; present observation, progress, cancellation, and
structured failures; and remain responsive when the terminal is resized.

The frontend itself is not an executable. A domain application may reference `Core` and the TUI
library directly, or a separate thin composition project may reference the domain model, `Core`,
and the TUI library. An example executable is permitted only to demonstrate composition and run
acceptance workflows; it must contain no reusable frontend behavior.

This milestone excludes persisted layouts, user-authored dashboards, batch operations, direct
collection mutation, search/indexing, charts, remote access, scripting, source generation, public
package publication, and a general frontend plug-in system.

## Toolkit Investigation and Decision

`XenoAtom.Terminal.UI` is a strong fit for the Product MVP:

- Its current package targets .NET 10 and requires C# 14, matching this repository's target and
  SDK baseline.
- It is a retained-mode, binding-oriented library with text, numeric, selection, list, tree,
  table/data-grid, dialog, validation, progress, and command controls. These map directly to
  UIEngine's descriptor roles without adding widget concepts to Core.
- Its fullscreen host handles keyboard, mouse, focus, routed input, and resize events. Its command
  system can supply discoverable key bindings and a command bar.
- Data templates and recycled item visuals can help render heterogeneous descriptor rows, while
  `DataGridControl` and list controls provide a path to efficient collection presentation.
- `XenoAtom.Terminal`, on which it depends, documents an in-memory backend that captures output and
  injects input events. The spike must prove that the public package surface is sufficient for
  UIEngine's deterministic interaction and resize tests.
- It is distributed under BSD-2-Clause, which is permissive, but the repository's Product Release
  milestone must still settle UIEngine's own licensing and notices.

The toolkit also imposes constraints that shape this milestone:

- The UI loop and `State<T>`/visual objects are single-thread-affine. Background observation and
  invocation updates must be marshalled through the toolkit dispatcher.
- Routed event handlers are synchronous. They must enqueue intent for an async session pump; the
  TUI must not use `async void` handlers.
- Fullscreen `Run`/`RunAsync` owns the calling thread's terminal loop. UIEngine must offer this as a
  convenience host without making it the only integration path.
- The package has moved through many releases in a short period. UIEngine should isolate toolkit
  usage, pin an exact version, and qualify upgrades with adapter and acceptance tests.
- Toolkit controls and templates are presentation helpers, not validation authority. All reads,
  conversion, validation, writes, resolution, and invocation continue through Core descriptors.

The recommendation is therefore to adopt the toolkit conditionally: Phase 1 must demonstrate
hosting, input injection, resize, UI-thread marshalling, bounded collection rendering, and clean
shutdown using the pinned package. If any mandatory behavior cannot be exercised through supported
public APIs, stop before building product screens and either narrow the acceptance strategy or
reconsider the toolkit.

### Research sources

- [Package metadata and version history](https://www.nuget.org/packages/XenoAtom.Terminal.UI/3.9.0)
- [Project overview and control inventory](https://github.com/XenoAtom/XenoAtom.Terminal.UI)
- [Hosting and terminal integration](https://github.com/XenoAtom/XenoAtom.Terminal.UI/blob/main/site/docs/hosting.md)
- [Async and UI-thread model](https://github.com/XenoAtom/XenoAtom.Terminal.UI/blob/main/site/docs/async-await.md)
- [Commands and key hints](https://github.com/XenoAtom/XenoAtom.Terminal.UI/blob/main/site/docs/commands.md)
- [Data templates and recycling](https://github.com/XenoAtom/XenoAtom.Terminal.UI/blob/main/site/docs/data-templating.md)
- [Terminal in-memory test backend](https://github.com/XenoAtom/XenoAtom.Terminal)

These links describe the investigated upstream state, not a promise that future versions retain
the same APIs.

## Product and Project Boundaries

Add the following project boundaries during implementation:

```text
Domain model -------------------------------> UIEngine.Core
     ^                                             ^
     |                                             |
Consumer/example host ---> UIEngine.Frontend.Tui -+
           |                       |
           +-----------------------+---> XenoAtom.Terminal.UI

UIEngine.Core -X-> XenoAtom.Terminal.UI
UIEngine.Frontend.Tui -X-> a specific domain assembly
```

- `Frontend/Tui/Tui.csproj` is a class library in the `UIEngine.Frontend.Tui` namespace. It
  references Core and the pinned XenoAtom package. It has no `Program`, `Main`, domain fixture
  reference, global root registry, or implicit process ownership.
- `Tests/UIEngine.Frontend.Tui.Tests` references the TUI library, Core, and focused fixtures.
- A thin example host may be added under `Examples` to make the cyclic world runnable. It may
  construct the model and host, register roots, choose options, and start the TUI—nothing more.
- Prefer a separate composition project when the domain model is a reusable library, so domain
  types do not acquire frontend dependencies. Direct embedding remains supported for an existing
  executable domain application.
- Core must not reference XenoAtom types, terminal dimensions, visuals, commands, colors, focus,
  key gestures, or frontend state.
- The CLI remains an independent proving frontend and must continue to pass unchanged.
- Packaging remains disabled repository-wide until the Product Release milestone. This milestone
  creates a package-shaped library boundary, not a published package.

## Consumer Experience

The common case should require only a caller-owned host, roots, and one TUI entry call. The exact
names remain provisional, but acceptance should be possible with code of this shape:

```csharp
using UIEngine.Core;
using UIEngine.Frontend.Tui;

using var engine = new UIEngineHost();
engine.SetRoot("world", world);

await TuiFrontend.RunAsync(engine, cancellationToken: stoppingToken);
```

An application already using XenoAtom should be able to create a disposable UIEngine workspace and
compose its visual into an existing tree instead of starting another terminal loop:

```csharp
using var workspace = TuiFrontend.CreateWorkspace(engine, new TuiFrontendOptions());
var visual = workspace.Visual;
```

The public contract must make ownership explicit:

- The caller owns `UIEngineHost` by default. Closing or disposing the TUI does not silently dispose
  the domain runtime.
- The TUI session owns its observation subscriptions, invocation readers, queued intents, and
  presentation cancellation source, and releases all of them on close.
- A convenience overload may opt into host ownership only if the name or option makes that
  behavior unambiguous.
- `RunAsync` and the embeddable workspace use the same session/controller behavior so the
  convenience path cannot become a second frontend implementation.

The zero-configuration path must fully support the semantic types Core can currently convert:
strings, booleans, characters, numeric primitives, nullable forms, and finite options/enums.
Unknown readable scalar types may be rendered safely, but an unknown writable type must be shown
as unsupported rather than guessed. Model-specific screen code is not required for any acceptance
fixture workflow.

## Frontend Architecture

Keep the implementation small and concrete. Do not add a general UI abstraction layer or mirror
the domain graph.

### Session and state

- One disposable TUI session holds the current canonical path, resolved object descriptor,
  back/forward history, breadcrumb locations, selected member, current collection window, active
  form drafts, subscriptions, invocations, and user-visible status.
- Durable navigation state uses `LogicalPath`, `PathLocation`, and `BindingReference`, not raw
  object references or process-global widget state.
- Descriptors and handles may be retained only as live interaction state and must be re-resolved
  after replacement, unavailability, or an observation indicating source replacement.
- Presentation state contains display strings, edit buffers, selection, focus targets, and loading
  indicators. It must not become a second authoritative copy of the domain model.

### Visual composition

The initial workspace should have four responsive regions:

1. A root/history/breadcrumb header that shows the canonical location and permits parent, back,
   forward, and root navigation.
2. A member browser grouped or labelled by semantic role, with references and collections
   navigable and values/actions activatable.
3. A detail surface that hosts value editors, bounded collection rows, action forms, validation
   issues, invocation status, and structured target state.
4. A status and command bar that exposes available keyboard actions and concise operation results.

At normal widths the browser and detail surface may be side by side. At narrow widths they should
collapse into a single active pane without losing the current path, draft, or selection. Layout
geometry remains frontend-owned and is not persisted in this milestone.

### Async and dispatch bridge

- Synchronous XenoAtom handlers enqueue small typed intents such as navigate, refresh, commit,
  invoke, cancel, page, and close.
- One async pump processes intents through Core `Task` APIs. It must not block the terminal input
  loop while a dispatched domain operation or action completion is pending.
- UI state changes occur only on the XenoAtom dispatcher. Domain calls continue to honor the
  caller-supplied `IInteractionDispatcher`; the frontend must not assume that the UI thread is the
  domain thread or replace the domain dispatcher implicitly.
- Long-running `ActionInvocation` completion and progress are observed separately after invocation
  starts. The form closes or remains available according to explicit session state, not by waiting
  synchronously in an event handler.
- Every session-owned task has cancellation and deterministic completion during disposal. No
  background reader may update a detached visual tree.

## Descriptor-to-UI Mapping

| Core semantic contract | Product MVP presentation |
|---|---|
| Root / `ObjectDescriptor` | Root picker, summary, type/identity details, member browser |
| `ValueDescriptor` read-only | Label/value row with null and failure represented distinctly |
| Writable string or character | Text editor with explicit commit/cancel |
| Writable boolean | Toggle/switch with commit result |
| Writable numeric value | Numeric editor honoring range metadata when present |
| Finite options / enum | Selection control using labels while submitting declared values |
| Nullable value | Explicit null affordance, not an empty-string convention |
| `ReferenceDescriptor` | Navigable row; null, unavailable, and resolution failure remain distinct |
| `CollectionDescriptor` | Bounded window with position/key, null/scalar/reference entry variants, next/previous/refresh |
| `ActionDescriptor` | Generated parameter form with required/default/null/options/range metadata |
| `ActionInvocation` | Running/result state, progress area, and cancel command only when supported |
| `ObservationSubscription` | Targeted refresh/invalidation with overflow warning and manual refresh |
| `InteractionError` | Stable state-specific presentation; logs and exception text are not parsed |

Toolkit data templates may reduce repetitive control construction, but UIEngine owns this mapping.
The TUI must not expose a Core value by binding a control directly to the domain object or bypass
`ReadAsync`, `WriteAsync`, `ReadAsync(offset, limit)`, or `InvokeAsync`.

## Navigation and Browsing

- Start with a root picker when multiple roots exist; navigate directly when exactly one root is
  available without hiding how to return to the picker.
- Build breadcrumbs from resolved path locations. Back/forward history stores canonical paths or
  guarded binding records and skips duplicate consecutive entries.
- Navigation through cycles must not recursively expand the visual tree. Revisiting a shared or
  cyclic object resolves the same host handle while creating an ordinary history entry.
- Reference activation resolves the current target at interaction time. A null reference is an
  empty target, not a failure.
- Missing, unavailable, ambiguous, type-mismatched, permission-denied, disposed, and unexpected
  fault states receive distinct presentations with only valid recovery commands enabled.
- Manual refresh is always available. Automatic refresh must be targeted so one noisy member does
  not rebuild the complete workspace or discard unrelated form drafts.

## Values and Action Forms

- Initialize an edit draft from a successful read. Never overwrite a dirty draft because an
  observation arrived; show that the source changed and let the user reload or keep editing.
- Submit the draft to Core and display returned validation issues beside the matching member or
  action parameter. Do not duplicate Core conversion/validation as frontend business logic.
- Disable commit while it is in flight and prevent accidental duplicate writes or calls.
- Generate one field per `ActionParameter`, distinguishing required, optional, defaulted, and
  nullable values. Omitting a defaulted parameter must remain different from explicitly submitting
  null.
- After a successful write, read the authoritative value again before updating the displayed row.
- After an action starts, show ordered progress, terminal status, result, structured failure, and
  cancellation availability. Do not surface trusted `Fault` details unless an explicit
  development option allows it.

## Bounded Collections

- Read only explicit windows through `CollectionDescriptor.ReadAsync`. The TUI must never request
  more than the host maximum or enumerate the source independently.
- Treat the Core slice as the data boundary even if a XenoAtom list or grid virtualizes visuals.
  Toolkit virtualization reduces rendering work; it does not authorize unbounded domain reads.
- Present position and optional key, preserve null/scalar/reference entry variants, and permit
  navigation only for reference entries.
- Use `TotalCount` when supplied and `HasMore` otherwise. Previous, next, direct offset, refresh,
  loading, empty, and failure states must be explicit.
- A collection reset or overflow notification invalidates the visible window and offers or
  performs one bounded refresh. It must not append indefinitely to an in-memory list.

## Observation and Replacement

- Observe the current object or selected member only when the source supports notifications, or
  when the caller explicitly configures polling. Unsupported observation falls back to manual
  refresh without treating the screen as broken.
- Coalesce repeated invalidations before refreshing. Preserve the overflow signal and tell the
  user that displayed state may have skipped changes.
- On source replacement, re-resolve the current path/binding, dispose the old subscription, attach
  at most one new subscription, and retain navigation/form state only when still compatible.
- Leaving a screen, switching roots, closing the workspace, or disposing the session cancels
  readers and detaches subscriptions deterministically.

## Frontend Capability Policy

Do not add generic frontend capability negotiation to Core pre-emptively. For the selected
toolkit, capability handling divides into two local concerns:

- Descriptor support is a TUI-owned mapping over the closed Core member hierarchy and its current
  metadata. Unsupported writable types or operations are rendered explicitly and remain disabled.
- Terminal support—size, color depth, mouse, extended keys, and resize—is obtained through
  XenoAtom's terminal capabilities. Every required workflow must retain a keyboard-only fallback;
  color, mouse, icons, and extended key protocols are enhancements.

During the spike, record any concrete semantic information that the TUI cannot obtain from Core.
Add the smallest frontend-neutral Core contract only when a user workflow and acceptance test
prove the need. Do not add a toolkit name, widget type, key binding, or layout preference to Core.

## Configuration and Extension Surface

Keep MVP configuration deliberately narrow:

- optional application title and initial root/path;
- collection window size bounded by the host limit;
- optional observation polling interval;
- theme or color-scheme selection passed through at the frontend boundary;
- trusted-development fault detail toggle; and
- overridable keyboard gestures only where conflicts with an embedding host require them.

Configuration is copied into one session and is not global. Custom layouts, arbitrary control
factories, persisted settings, dashboards, and a public plug-in model remain deferred. Advanced
consumers can embed the workspace visual and compose around it without changing Core.

## Verification and Test Scenarios

- Dependency tests prove that Core has no XenoAtom reference, the TUI is a class library, and only
  the optional example/consumer project is executable.
- Public API tests cover caller-owned host lifetime, `RunAsync`, workspace creation, configuration
  validation, session disposal, and the absence of domain-fixture dependencies.
- Session tests exercise intents and presentation state without requiring a physical terminal:
  loading, stale-result rejection, duplicate-submit prevention, cancellation, disposal, and
  observation coalescing.
- Toolkit adapter tests use the supported in-memory terminal backend to inject keys and resize
  events and to inspect stable semantic output. Avoid broad character-perfect snapshots that make
  harmless style changes expensive.
- Navigation tests cover root selection, canonical breadcrumbs, parent/back/forward, cycles,
  shared references, null references, and compatible/incompatible replacement.
- Value tests cover every Core-convertible scalar family, read-only and nullable values, enum
  options, ranges, conversion failure, validation issues, setter rejection, refresh, and dirty
  draft conflict.
- Collection tests cover bounded reads, host limit negotiation, previous/next/direct offsets,
  optional counts, `HasMore`, keys, null/scalar/reference entries, empty slices, resets, overflow,
  large lazy sources, and no eager enumeration.
- Action tests cover required/default/null parameters, generated controls, synchronous and
  asynchronous results, progress, supported and unsupported cancellation, failure, and repeated
  invocation prevention.
- Failure-state tests cover every `InteractionErrorCode` without parsing messages and verify that
  trusted exception details are hidden by default.
- Lifecycle tests prove that navigation and close detach subscriptions, cancel frontend readers,
  do not update detached visuals, and do not dispose a caller-owned Core host.
- Responsive-layout tests exercise at least a normal terminal and a narrow terminal, preserving
  selection and drafts across resize.
- Keyboard acceptance completes the entire cyclic-world workflow without a mouse. Mouse input may
  be tested as an additional path, not the only path.
- Existing Core and CLI behavior suites continue to pass unchanged.

## Acceptance Workflow

The thin cyclic-world consumer must demonstrate, without custom screen code:

1. Start the TUI from a caller-created `UIEngineHost` and select the `world` root.
2. Navigate `World -> Nation -> Capital -> OwnerNation`, confirm the cycle returns to the same
   runtime identity, and use breadcrumbs/history to return.
3. Read and edit supported values, see a rejected value inline, and reload authoritative state.
4. Browse multiple bounded windows of the population forecast without materializing it.
5. Browse dictionary keys plus null, scalar, and reference collection entries.
6. Invoke synchronous and asynchronous actions, see progress, cancel a cancellable action, and
   distinguish success, cancellation, and failure.
7. Observe deterministic external mutation, collection reset, and compatible replacement without
   retaining stale handlers.
8. Display unavailable, missing, ambiguous, mismatched, permission, and overflow states with valid
   recovery commands.
9. Complete the workflow using only the keyboard and repeat navigation after terminal resize.
10. Close the TUI cleanly while leaving the caller-owned host usable until the consumer disposes it.

## Exit Criteria

- The Phase 1 spike confirms the pinned XenoAtom version through supported public APIs, including
  deterministic input/resize testing and UI-thread marshalling.
- `UIEngine.Frontend.Tui` is a reusable class library with no entry point and no reference to a
  specific domain model.
- A documented minimal consumer can create a fully interactive TUI by constructing a Core host,
  registering roots, and invoking the frontend; no model-specific views are required.
- Both convenience fullscreen hosting and embeddable visual composition use one session behavior
  implementation with explicit ownership and disposal.
- Core remains terminal- and frontend-neutral, and no speculative frontend negotiation system is
  added.
- Navigation, editing, action forms, bounded collections, progress, cancellation, observation,
  replacement, and all structured target states meet the scenarios above.
- The complete cyclic-world workflow works keyboard-first and remains usable across the accepted
  normal and narrow terminal sizes.
- The TUI does not bypass Core operations, eagerly enumerate collections, parse rendered errors,
  leak subscriptions/tasks, expose trusted faults by default, or dispose a caller-owned host.
- `dotnet build UIEngine.sln` completes with zero warnings and errors, and
  `dotnet test UIEngine.sln --no-build` passes Core, CLI, TUI, dependency, and lifecycle tests.
- Packaging remains disabled and no post-MVP layout, batch, search, remote, or scripting subsystem
  has entered the implementation.
- Current documentation and [TODO.MD](../../TODO.MD) are updated, with only genuinely completed
  items checked.

## Implementation Steps

Keep the repository buildable after every step. Add the smallest meaningful tests with each
behavior rather than deferring coverage to the final acceptance pass.

### 1. Qualify and pin XenoAtom.Terminal.UI

1. Add a disposable spike project or branch using the candidate exact package version; do not
   commit a floating dependency range.
2. Prove fullscreen and embedded visual hosting, keyboard input injection, focus traversal,
   terminal resize, dispatcher marshalling, and deterministic exit using public APIs.
3. Prove a bounded collection window can update recycled list/grid visuals without requesting the
   full Core collection.
4. Record the accepted version, transitive dependencies, license/notices, known terminal limits,
   and upgrade procedure. Stop if mandatory acceptance behavior requires unsupported internals.

### 2. Establish the library and dependency boundaries

1. Add `Frontend/Tui` as a class library and `Tests/UIEngine.Frontend.Tui.Tests` as its test project.
2. Reference only Core and the pinned toolkit from the frontend; add dependency tests preventing
   Core-to-TUI/toolkit and TUI-to-domain-fixture references.
3. Add the minimal public options, session/facade, fullscreen runner, and embeddable workspace
   contracts with explicit ownership documentation.
4. Add an empty thin cyclic-world consumer that composes the domain model, Core host, and TUI
   library, and verify that all reusable behavior remains outside the executable.

### 3. Implement session lifetime and the async bridge

1. Add disposable session state, typed intent queue, cancellation, and stale-operation generation
   guards.
2. Route synchronous control events into the async pump without `async void` handlers.
3. Marshal presentation changes to the toolkit dispatcher while leaving domain dispatch under the
   configured Core host.
4. Prove deterministic close/disposal with in-flight reads, observation, and action progress, and
   prove that a caller-owned host survives.

### 4. Build responsive workspace chrome

1. Add the header, browser, detail surface, status area, and discoverable command bar.
2. Define stable keyboard commands for root selection, activation, parent/back/forward, refresh,
   commit/cancel, paging, action cancellation, help, and close.
3. Implement normal and narrow layouts that preserve state and focus across resize.
4. Verify complete focus traversal, visible focus, dialog return focus, and keyboard-only access.

### 5. Add object navigation and history

1. Load roots and object descriptors through Core, presenting summary, type, runtime handle, and
   optional domain identity without retaining raw domain objects.
2. Render members by semantic role and navigate references through canonical resolved paths.
3. Add breadcrumbs plus bounded back/forward history, including roots, cycles, shared references,
   null references, and path-resolution failures.
4. Re-resolve navigation state after replacement and reject identity conflicts rather than silently
   rebinding.

### 6. Generate value presentation and editors

1. Map read-only values and each currently convertible writable scalar family to TUI controls.
2. Add explicit null handling, finite options, range hints, local drafts, commit/cancel, and loading
   state.
3. Submit through `WriteAsync`, associate returned validation issues with fields, and re-read after
   success.
4. Handle observation-versus-dirty-draft conflicts without overwriting user input.

### 7. Add bounded collection browsing

1. Render position/key and null/scalar/reference entry variants from one bounded Core slice.
2. Add previous, next, direct offset, refresh, count/has-more, loading, empty, and failure states.
3. Navigate reference entries and retain the collection location in history/breadcrumbs.
4. Prove bounded work for large/lazy sources and correct invalidation after reset, replacement, and
   overflow.

### 8. Generate action forms and invocation views

1. Generate fields from parameter type, required/default/null/options/range metadata and preserve
   omitted-default versus explicit-null semantics.
2. Start invocations once, disable duplicate submission, and present synchronous/asynchronous
   completion and result values.
3. Stream ordered progress without blocking the UI and expose cancellation only when supported.
4. Present validation, cancellation, permission, target-loss, and fault outcomes without leaking
   trusted exception details by default.

### 9. Integrate observation and recovery

1. Attach only the subscriptions needed by the active object/member/collection and fall back to
   manual refresh when observation is unsupported.
2. Coalesce ordinary changes, preserve buffer-overflow visibility, and refresh only affected
   presentation state.
3. Dispose and reattach exactly once on navigation, source replacement, root change, and close.
4. Cover unavailable, missing, ambiguous, mismatched, disposed, and recovered states with valid
   commands and deterministic tests.

### 10. Complete the consumer workflow and documentation

1. Finish the thin cyclic-world executable and use it only as a composition and manual acceptance
   host.
2. Add a quick-start showing both direct application embedding and the preferred separate host for
   a reusable domain library.
3. Document options, host/session ownership, supported descriptor mapping, keyboard commands,
   terminal requirements, toolkit version policy, and known limitations.
4. Reconcile the architecture, repository structure, roadmap, milestone index, and progress
   tracker without marking Product Release or post-MVP work complete.

### 11. Run milestone acceptance

1. Run the toolkit-spike, dependency, public API, session, navigation, value, collection, action,
   observation, failure, lifecycle, input, and resize tests.
2. Complete the cyclic-world workflow with injected keyboard input and with a real terminal smoke
   test; record any platform not exercised rather than implying support.
3. Run `dotnet build UIEngine.sln` with zero warnings and errors, followed by
   `dotnet test UIEngine.sln --no-build`, and verify the same checks in CI.
4. Confirm that packaging remains disabled, no deferred subsystem entered scope, all session-owned
   resources detach, and only completed [TODO.MD](../../TODO.MD) items are checked.

Milestone 04 may address Product Release only after this milestone satisfies every exit criterion.
