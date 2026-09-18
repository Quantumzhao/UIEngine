# Runtime Domain Workbench for .NET

**Status:** Design specification / technical proposal  
**Audience:** Framework implementers, frontend authors, .NET library authors, advanced users  
**Scope:** A frontend-agnostic interaction framework for exposing, navigating, inspecting, mutating, invoking, batching, and composing user-facing views over live .NET domain models.

---

## 1. Executive Summary

This project is a .NET middleware framework that exposes a running application's domain model as an **interactive object space**. The framework does not define a single user interface technology. Instead, it defines a runtime interaction model that can be consumed by GUI, CLI, TUI, web, voice, or other frontends.

The core premise is that many complex applications—simulations, game engines, backend systems, research prototypes, workflow engines, internal tools, and long-running services—need a usable control surface before a bespoke production UI exists. For these systems, writing a temporary UI is often disproportionately expensive because the domain model contains many mutable values, nested objects, cyclic references, collections, methods, long-running actions, and ad-hoc analysis needs.

The framework reduces that cost by allowing developers to:

1. **Expose selected members and operations** from existing .NET objects using attributes and/or programmatic registration.
2. **Describe interaction semantics independently of presentation**, so a value can be rendered as a GUI field, CLI command, TUI widget, voice intent, or another interaction mechanism.
3. **Navigate the complete exposed domain model**, including nested objects, shared references, and cyclic graphs.
4. **Interact with live mutable runtime objects**, including property mutation and method invocation with parameters and asynchronous progress.
5. **Bind modular user-facing components to object paths or stable object identities**.
6. **Create, delete, rebind, compose, organize, and persist custom layouts** built from those components.
7. **Apply batch operations to collections** through constrained, previewable higher-order operations such as “for all compatible items, perform this action.”

The framework's primary technical challenge is not rendering controls. Its core problem is defining robust semantics for **object identity, binding, capabilities, mutation, method invocation, persistence, compatibility, batching, concurrency, failure handling, and frontend independence**.

---

# 2. Motivation

## 2.1 Problem Statement

During early and middle stages of development, a complex program often has a usable domain model but no usable interface.

Typical examples include:

- simulations with hundreds of tunable parameters;
- strategy games with complex world state;
- backend workflow engines;
- financial or risk models;
- robotics or control applications;
- AI-agent environments;
- internal operational tools;
- scientific computation systems;
- long-running services with mutable state;
- prototype applications where the final user experience is not yet known.

The common alternatives are unsatisfactory:

### Write a bespoke temporary UI

This introduces substantial engineering overhead in layout, binding, validation, navigation, state synchronization, async progress, error handling, and persistence. Temporary tools frequently become semi-permanent, causing duplicated architecture and maintenance cost.

### Use an IDE/debugger

A debugger is suitable for inspection, not for routine operation. It requires developer tooling, assumes debugging knowledge, is inconvenient for non-developers, and does not provide a stable interaction model for invoking supported actions.

### Use a property grid or object inspector

Traditional property grids are effective for one object at a time but weak at complex navigation, object identity, user-defined layouts, action invocation, batch operations, composition, analytics, and alternative interaction modalities.

### Use an admin dashboard framework

Admin frameworks typically assume CRUD entities, database-backed records, or HTTP-oriented application state. They are poorly matched to arbitrary in-memory object graphs with cycles, shared references, domain methods, and runtime-only state.

## 2.2 Core Insight

The framework should not generate “the application UI.”

Instead, it should expose a **live operational representation of the domain model**.

The core abstraction is therefore:

```text
Domain Model
    ↓
Exposure / Metadata
    ↓
Interactive Object Space
    ↓
Binding + Invocation + Batch Semantics
    ↓
Frontend Adapter
    ↓
GUI / CLI / TUI / Web / Voice / Custom
```

This separation prevents the framework from becoming coupled to one visual toolkit and makes interaction semantics reusable across presentation technologies.

---

# 3. Objectives

The framework SHALL provide the following capabilities.

## 3.1 Primary Objectives

1. **Low-friction exposure**  
   A developer SHALL be able to expose fields, properties, methods, and object relationships with minimal code, primarily through attributes or registration APIs.

2. **Live interaction**  
   Exposed values SHALL refer to live runtime objects rather than generated DTO snapshots, unless a frontend explicitly requests a snapshot.

3. **Frontend independence**  
   Core APIs SHALL describe interaction semantics, not GUI controls.

4. **Cyclic graph support**  
   The object model SHALL be treated as a graph, not a tree. Shared references and cycles SHALL be supported explicitly.

5. **User-composable layouts**  
   A frontend MAY allow users to instantiate interaction components, bind them to values or actions, move them, nest them, rebind them, delete them, and persist their composition.

6. **Action invocation**  
   Exposed methods SHALL support parameters, validation, asynchronous execution, progress reporting, cancellation where applicable, and structured results.

7. **Collection-level operations**  
   Collections SHALL support constrained batch operations with compatibility analysis, target preview, sequential execution by default, and per-item result reporting.

8. **Extensibility by type and capability**  
   Developers SHALL be able to provide custom interaction components for specific types, interfaces, capabilities, or semantic descriptors.

## 3.2 Secondary Objectives

- headless operation;
- remote transport support in a later extension layer;
- search across exposed objects;
- logging and auditability;
- reusable saved layouts;
- reusable saved batch-operation templates;
- analysis components such as tables and charts;
- programmatic automation through the same interaction model.

---

# 4. Non-Goals

The framework SHALL NOT initially attempt to be:

- a production end-user UI framework;
- a general low-code/no-code application builder;
- a database admin framework;
- a complete scripting language;
- an ORM;
- a distributed object system;
- a universal visual analytics platform;
- an automatic undo/transaction engine for arbitrary side effects;
- a guarantee that all frontends support all exposed interactions equally.

These exclusions are important because the value proposition depends on remaining focused on **runtime domain interaction** rather than absorbing the responsibilities of unrelated systems.

---

# 5. Terminology

## 5.1 Domain Object

Any live .NET object whose exposed state or operations are visible through the framework.

## 5.2 Interactive Object Space

The graph of exposed objects, values, relationships, collections, actions, and metadata that the framework makes available to frontends.

## 5.3 Interaction Descriptor

A frontend-independent description of an interaction capability, such as:

- readable value;
- writable value;
- enum/selection value;
- object reference;
- collection;
- action;
- async operation;
- progress source;
- graph-like structure;
- summary;
- batch operation.

## 5.4 Binding

A persistent or transient association between an interaction component and a target within the interactive object space.

## 5.5 Binding Target

The object member, action, collection, query result, or computed source referenced by a binding.

## 5.6 Frontend

A consumer of the interaction model. Examples include desktop GUI, CLI, TUI, web, voice, or application-specific custom interfaces.

## 5.7 Component

A frontend-owned interaction element that consumes one or more descriptors. Components are not core framework abstractions unless explicitly represented by the layout subsystem.

## 5.8 Layout

A persistent user-defined arrangement of component instances and their bindings.

## 5.9 Batch Operation

A constrained operation plan that selects multiple target objects and applies a compatible operation to each target according to an explicit execution policy.

---

# 6. Architectural Principles

## 6.1 The Core Does Not Know About Pixels

The core SHALL expose semantic capabilities rather than specific widgets.

Incorrect core abstraction:

```csharp
IButton
IDropDown
ITextBox
```

Preferred abstraction:

```csharp
IActionDescriptor
IEditableValueDescriptor
ISelectionDescriptor
ICollectionDescriptor
INavigationDescriptor
IProgressDescriptor
```

A frontend chooses whether an action becomes a button, command, menu item, hotkey, voice intent, or another control.

## 6.2 The Domain Model Remains Authoritative

The framework SHALL NOT require developers to copy application state into a parallel UI model.

Bindings SHOULD resolve to the actual domain object or an explicitly defined proxy/adaptor.

## 6.3 Object Graph, Not Object Tree

The framework SHALL assume:

- shared references exist;
- cycles exist;
- multiple paths may reach the same object;
- objects may be added or removed;
- collection positions may change;
- computed members may not correspond to stored values.

## 6.4 Explicit Capabilities Beat Implicit Rendering Rules

Type information is useful but insufficient.

For example, `int` might mean:

- a raw integer input;
- an index;
- a bounded slider;
- a percentage;
- a count;
- an enum-like domain code;
- a read-only metric.

Therefore, type-driven defaults SHOULD be supplemented by metadata such as ranges, units, display names, validation rules, read-only state, semantics, and preferred interaction role.

## 6.5 Safety Is Part of Interaction Semantics

A method is not merely a clickable function. The descriptor model SHOULD represent:

- parameters;
- validation;
- confirmation requirements;
- concurrency constraints;
- cancellation capability;
- progress;
- failure behavior;
- danger level;
- batch compatibility.

---

# 7. High-Level Architecture

```text
┌──────────────────────────────────────────────┐
│                Domain Application            │
│   objects, collections, services, methods    │
└──────────────────────┬───────────────────────┘
                       │
                       ▼
┌──────────────────────────────────────────────┐
│            Exposure / Metadata Layer         │
│ attributes, fluent API, source generation    │
└──────────────────────┬───────────────────────┘
                       │
                       ▼
┌──────────────────────────────────────────────┐
│            Object Graph Runtime              │
│ identity, references, traversal, summaries   │
└──────────────────────┬───────────────────────┘
                       │
                       ▼
┌──────────────────────────────────────────────┐
│         Interaction Descriptor Layer         │
│ values, actions, collections, progress       │
└───────────┬──────────────────┬───────────────┘
            │                  │
            ▼                  ▼
┌───────────────────┐  ┌──────────────────────┐
│ Binding/Layout    │  │ Batch Operation      │
│ persistence       │  │ planning/execution   │
└───────────┬───────┘  └──────────┬───────────┘
            │                     │
            └──────────┬──────────┘
                       ▼
┌──────────────────────────────────────────────┐
│             Frontend Adapter API             │
└───────┬────────┬────────┬────────┬───────────┘
        │        │        │        │
        ▼        ▼        ▼        ▼
      GUI       CLI      TUI      Web/Voice/Custom
```

---

# 8. Exposure Model

## 8.1 Attribute-Based Exposure

A baseline API SHOULD support attributes such as:

```csharp
[Expose]
public int Population { get; set; }

[Expose(ReadOnly = true)]
public double GDP { get; private set; }

[Action]
public void AdvanceTurn() { }

[Action]
public Task RecalculateAsync(CancellationToken ct) { }

[Children]
public IReadOnlyList<City> Cities { get; }

[Summary]
public string Summary => $"{Name}: {Population:N0} residents";
```

Exact attribute names remain implementation details, but their semantics SHALL be explicit.

## 8.2 Programmatic Exposure

Attributes alone are insufficient for:

- third-party types;
- runtime-dependent exposure;
- role-based exposure;
- computed metadata;
- generated object models;
- generic wrappers.

The framework SHOULD therefore support a fluent registration API.

Illustrative form:

```csharp
registry.For<City>()
    .Expose(x => x.Population)
    .Expose(x => x.GDP, readOnly: true)
    .Action(x => x.RecalculateAsync(default))
    .Children(x => x.Districts)
    .Summary(x => $"{x.Name} ({x.Population:N0})");
```

## 8.3 Source Generation

Reflection is acceptable for prototypes, but a production-quality implementation SHOULD provide an optional source generator for:

- faster startup;
- AOT friendliness;
- compile-time validation;
- reduced reflection overhead;
- trim safety;
- generated descriptor factories.

Reflection SHOULD remain available as a fallback for dynamic scenarios.

---

# 9. Descriptor Model

The framework SHOULD normalize exposed members into semantic descriptors.

## 9.1 Value Descriptor

```csharp
public interface IValueDescriptor
{
    string Id { get; }
    string DisplayName { get; }
    Type ValueType { get; }
    bool CanRead { get; }
    bool CanWrite { get; }
    object? Read();
    ValueTask<SetValueResult> WriteAsync(object? value, CancellationToken ct);
    IReadOnlyList<IValidationRule> ValidationRules { get; }
}
```

A concrete implementation MAY provide generic typed forms.

## 9.2 Selection Descriptor

Used for enums or finite choices.

```csharp
public interface ISelectionDescriptor : IValueDescriptor
{
    IReadOnlyList<SelectionOption> Options { get; }
}
```

## 9.3 Object Descriptor

```csharp
public interface IObjectDescriptor
{
    ObjectIdentity Identity { get; }
    string TypeName { get; }
    string DisplayName { get; }
    string Summary { get; }

    IReadOnlyList<IValueDescriptor> Values { get; }
    IReadOnlyList<IActionDescriptor> Actions { get; }
    IReadOnlyList<IReferenceDescriptor> References { get; }
    IReadOnlyList<ICollectionDescriptor> Collections { get; }
}
```

## 9.4 Action Descriptor

```csharp
public interface IActionDescriptor
{
    string Id { get; }
    string DisplayName { get; }
    IReadOnlyList<IParameterDescriptor> Parameters { get; }
    ActionExecutionMetadata Execution { get; }

    ValueTask<ActionValidationResult> ValidateAsync(
        object? target,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct);

    ValueTask<IActionInvocation> InvokeAsync(
        object? target,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken ct);
}
```

`IActionInvocation` SHOULD expose:

- current status;
- completion task;
- progress source if available;
- structured result;
- exception information;
- cancellation capability.

## 9.5 Collection Descriptor

```csharp
public interface ICollectionDescriptor
{
    string Id { get; }
    string DisplayName { get; }
    Type ElementType { get; }
    ValueTask<IReadOnlyList<ObjectHandle>> SnapshotAsync(CancellationToken ct);
}
```

Collection access SHALL distinguish between:

- a live enumerable;
- a stable snapshot;
- a paged or virtualized source.

Batch execution SHOULD operate on a snapshot by default.

---

# 10. Object Identity and References

## 10.1 Identity Requirement

Path strings alone are insufficient for robust bindings because:

- collection indices change;
- objects move;
- multiple paths can reference one object;
- cycles exist;
- objects can disappear;
- a path may resolve to a different object after mutation.

The framework SHOULD assign or derive a stable `ObjectIdentity` for every exposed object during its lifetime.

Possible forms:

```csharp
public readonly record struct ObjectIdentity(Guid RuntimeId);
```

Applications MAY additionally provide persistent semantic keys:

```csharp
public interface IStableDomainIdentity
{
    string DomainKey { get; }
}
```

Example domain keys:

```text
Nation/USSR
City/Moscow
Unit/4f91b2
Workflow/OrderApproval/Instance/1281
```

## 10.2 Identity Scope

Three identity scopes SHOULD be distinguished:

1. **Runtime identity**  
   Stable only during the current process lifetime.

2. **Domain identity**  
   Stable across object replacement or application restarts if the domain provides such a key.

3. **Binding path**  
   Human-readable navigation or resolution expression.

Persistent layouts SHOULD prefer domain identity when available and fall back to path-based resolution.

## 10.3 Cycles

Traversal SHALL maintain a visited identity set when producing tree-like views.

A frontend MAY display repeated references as links rather than recursively expanding indefinitely.

Example:

```text
World
 ├─ Nations
 │   └─ Nation: A
 │      └─ Capital → City: X
 └─ Cities
     └─ City: X
        └─ Owner → Nation: A
```

The framework SHALL preserve the fact that both references point to the same runtime object.

---

# 11. Paths and Binding Resolution

## 11.1 Path Semantics

A path SHALL be a logical reference expression, not a raw CLR reflection path.

Illustrative form:

```text
/World/Nations[key=USSR]/Cities[key=Moscow]/Population
```

The path grammar SHOULD support:

- named members;
- keyed collection lookup;
- index lookup where explicitly allowed;
- object identity references;
- optional predicates in later versions;
- root aliases.

## 11.2 Binding Record

A persistent binding SHOULD contain more than a path string.

```csharp
public sealed record BindingReference(
    string? DomainIdentity,
    string? Path,
    string MemberId,
    string ExpectedDescriptorKind,
    string? ExpectedTypeName,
    BindingFallbackPolicy FallbackPolicy);
```

## 11.3 Broken Bindings

Bindings SHALL have explicit states:

```text
Resolved
TemporarilyUnavailable
TypeMismatch
TargetMissing
Ambiguous
PermissionDenied
InvalidPath
```

A frontend SHALL NOT silently rebind a failed target to a semantically different object.

A repair workflow MAY offer compatible candidates.

---

# 12. Type Mapping and Component Compatibility

## 12.1 Default Type Mapping

The framework MAY provide default semantic mappings:

| Domain Type / Metadata | Descriptor | Example GUI Rendering |
|---|---|---|
| `bool` | editable scalar | checkbox/toggle |
| `int`, `double`, `decimal` | numeric value | numeric field |
| numeric + range metadata | ranged numeric value | slider/spinbox |
| enum | selection | dropdown/radio |
| `string` | textual value | textbox |
| collection | collection | list/table/tree |
| object reference | navigation/reference | hyperlink/tree node |
| method | action | button/menu command |
| async method | async action | button + progress |
| time series | sequence descriptor | chart/table |
| graph model | graph descriptor | graph view/table |

## 12.2 Semantic Override

Developers SHOULD be able to override defaults.

Example:

```csharp
[Expose]
[Range(0, 1)]
[Unit("ratio")]
[PreferredInteraction("slider")]
public double Throttle { get; set; }
```

`PreferredInteraction` SHALL be advisory. A CLI frontend is not required to implement a slider.

## 12.3 Component Contract

A frontend component SHOULD declare which descriptor shapes it accepts.

Example:

```csharp
public interface IComponentBindingContract
{
    bool CanBind(InteractionDescriptor descriptor);
}
```

A line chart might accept:

- `IEnumerable<double>`;
- timestamp/value sequences;
- observable numeric histories.

A button component might accept:

- parameterless action;
- parameterized action with generated form support;
- async action.

---

# 13. Frontend Capability Model

The core SHALL NOT assume all frontends support all capabilities.

Illustrative flags:

```csharp
[Flags]
public enum InteractionCapabilities
{
    None = 0,
    ReadValue = 1 << 0,
    WriteValue = 1 << 1,
    InvokeAction = 1 << 2,
    NavigateObjectGraph = 1 << 3,
    RenderCollection = 1 << 4,
    RenderChart = 1 << 5,
    PersistLayout = 1 << 6,
    RebindComponent = 1 << 7,
    BatchOperationPreview = 1 << 8,
    AsyncProgress = 1 << 9,
    Confirmation = 1 << 10,
    Search = 1 << 11
}
```

A frontend SHOULD advertise a capability set during initialization.

The framework MAY reject, downgrade, or omit unsupported interaction descriptors.

---

# 14. Layout System

## 14.1 Purpose

The layout system turns the framework from a transient inspector into a reusable workbench.

Users MAY create interaction components, bind them to targets, position them, nest them, configure them, and persist the result.

## 14.2 Layout Model

A layout SHOULD be a serialized graph of component instances rather than a serialized domain model.

Illustrative form:

```json
{
  "layoutVersion": 1,
  "root": {
    "componentType": "SplitPane",
    "children": [
      {
        "componentType": "NumericEditor",
        "binding": {
          "domainIdentity": "City/Moscow",
          "memberId": "Population"
        }
      },
      {
        "componentType": "LineChart",
        "binding": {
          "domainIdentity": "Nation/USSR",
          "memberId": "GDPHistory"
        }
      }
    ]
  }
}
```

## 14.3 Required Component Operations

A layout-capable frontend SHOULD support:

- create component;
- delete component;
- move component;
- nest/unnest component where valid;
- edit component configuration;
- bind/rebind component;
- duplicate component;
- save layout;
- load layout;
- report broken bindings.

## 14.4 Layout Versioning

Serialized layouts SHALL contain a schema version.

The framework SHOULD support migrations for known historical versions.

A frontend-specific layout payload MAY coexist with a framework-level binding payload.

---

# 15. Navigation

## 15.1 Baseline Navigation Model

At minimum, frontends SHOULD be able to navigate:

- root objects;
- exposed object references;
- collection elements;
- parent history or breadcrumbs;
- back/forward navigation where appropriate.

## 15.2 Summaries

Objects SHOULD expose compact summaries to make navigation useful.

Summary resolution priority MAY be:

1. explicit summary provider;
2. annotated summary member;
3. display-name metadata;
4. `ToString()` fallback;
5. type name + runtime identity fallback.

A generated object browser SHALL NOT rely solely on memory addresses or CLR type names.

---

# 16. Actions and Method Invocation

## 16.1 Parameter Handling

Action descriptors SHALL expose structured parameter metadata.

For each parameter, the framework SHOULD expose:

- name;
- display name;
- CLR type;
- nullability;
- default value;
- validation rules;
- finite options if applicable;
- semantic metadata.

A frontend MAY generate an argument form automatically.

## 16.2 Async Progress

The framework SHOULD support actions returning:

- `Task`;
- `Task<T>`;
- `ValueTask`;
- `ValueTask<T>`.

Progress MAY be exposed through:

- `IProgress<T>`;
- a framework-specific progress channel;
- `IAsyncEnumerable<TProgress>`;
- an invocation object that reports progress independently of the method signature.

The recommended abstraction is an `IActionInvocation` object so that frontend behavior is independent of the method's exact CLR return type.

## 16.3 Cancellation

Actions SHALL declare whether cancellation is supported.

A frontend SHALL NOT display a cancel control unless the invocation supports cancellation.

## 16.4 Confirmation and Risk Metadata

Actions SHOULD support metadata such as:

```text
Safe
Mutating
Destructive
Irreversible
RequiresConfirmation
```

These labels SHALL be declarative hints, not security boundaries.

---

# 17. Batch Operations

## 17.1 Motivation

Single-object interaction becomes inefficient when users need to perform the same operation across a collection.

The framework therefore supports a constrained higher-order interaction model analogous to:

```text
for each x in collection:
    perform operation(x)
```

The public product concept SHOULD be called **Batch Operations**, **Collection Actions**, or similar rather than “higher-order functions.”

## 17.2 Batch Pipeline

A batch operation SHALL have four conceptual stages:

```text
Selection → Filter → Operation → Execution Policy
```

## 17.3 Selection

The target set MAY originate from:

- the current collection;
- explicitly selected elements;
- search results;
- objects of a given exposed type;
- a saved selection query.

Before execution, a batch SHOULD snapshot target identities unless the user explicitly requests live streaming semantics.

## 17.4 Template Item

A frontend MAY let the user choose one collection element as a template.

The template is used to discover candidate operations. It SHALL NOT imply that all other elements are compatible.

Compatibility MUST be checked for every target before execution.

## 17.5 Supported Operation Kinds

Initial implementation SHOULD support only constrained operations:

- set an exposed writable value;
- invoke an exposed method;
- create a frontend component bound to each item;
- collect a value into a table;
- collect a numeric/time-series value into a visualization;
- export selected values.

General user-defined code execution SHOULD be deferred.

## 17.6 Compatibility Analysis

Each candidate target SHALL be classified before execution:

```text
Compatible
IncompatibleType
MissingMember
ReadOnly
ValidationFailed
Unavailable
PermissionDenied
```

The preview SHALL show counts and, when practical, per-target reasons.

## 17.7 Preview

Mutating batch operations SHOULD default to preview-before-execute.

Example:

```text
Operation: Set City.TaxRate = 0.05
Targets: 42
Compatible: 40
Skipped: 2

Preview:
Moscow   0.12 → 0.05
Paris    0.08 → 0.05
Beijing  0.10 → 0.05
...
```

## 17.8 Execution Policy

Initial policies SHOULD include:

- sequential execution;
- continue on error / stop on first error;
- cancellation;
- per-item result recording;
- optional confirmation;
- optional dry run.

Parallel execution SHOULD NOT be the default.

## 17.9 Failure Semantics

Automatic rollback SHALL NOT be promised for arbitrary actions.

Instead, the framework SHALL report partial success explicitly.

Example:

```text
Succeeded: 37
Failed: 3
Skipped: 2
Cancelled: 0
```

Transactional behavior MAY be supported only when the domain application provides an explicit transaction or undo contract.

## 17.10 Batch Plan Model

Illustrative structure:

```csharp
public sealed class BatchOperationPlan
{
    public required TargetSelection Selection { get; init; }
    public IReadOnlyList<TargetFilter> Filters { get; init; } = [];
    public required OperationDescriptor Operation { get; init; }
    public required BatchExecutionPolicy Policy { get; init; }
}
```

Operations SHOULD be serializable descriptors, not opaque delegates.

---

# 18. Threading and Concurrency

## 18.1 Requirement

Live runtime interaction means the framework may touch objects owned by specific application threads.

The framework SHALL NOT assume arbitrary object access is thread-safe.

## 18.2 Execution Context

The host SHOULD provide an execution-context abstraction.

```csharp
public interface IInteractionDispatcher
{
    ValueTask<T> InvokeAsync<T>(Func<T> action, CancellationToken ct);
    ValueTask InvokeAsync(Action action, CancellationToken ct);
}
```

Examples:

- WPF dispatcher;
- Avalonia UI thread;
- game-engine main thread;
- simulation thread;
- single-threaded actor scheduler;
- custom synchronization context.

## 18.3 Reads and Writes

Descriptor reads and writes SHOULD execute through the host-provided dispatcher when required.

A descriptor MAY declare itself thread-safe and bypass dispatch if appropriate.

---

# 19. State Change Observation

## 19.1 Problem

A live workbench must update when domain state changes independently of the UI.

## 19.2 Supported Observation Modes

The framework SHOULD support multiple strategies:

1. `INotifyPropertyChanged`;
2. observable collections;
3. explicit framework notifications;
4. polling at configurable intervals;
5. custom change-provider adapters.

Reflection alone cannot infer arbitrary state changes.

## 19.3 Change Events

Normalized change events SHOULD include:

- object identity;
- member identity;
- old value if available;
- new value if available;
- change kind;
- timestamp/order token.

---

# 20. Validation

Validation SHOULD occur at three levels:

1. **Descriptor validation**  
   Is the requested interaction structurally valid?

2. **Value validation**  
   Is the proposed value acceptable by type/range/domain rules?

3. **Action validation**  
   Is the action currently legal for this target and argument set?

Validation results SHOULD be structured rather than represented only as exceptions.

---

# 21. Security and Trust Boundary

## 21.1 Baseline Assumption

The initial framework is an **in-process trusted developer tool**.

This is a normative assumption for the first implementation.

It means exposed actions are potentially equivalent to arbitrary application control.

## 21.2 Remote Frontends

If remote interaction is added later, authentication, authorization, serialization, transport security, capability filtering, and audit logs become mandatory architectural concerns.

Remote access SHALL NOT simply serialize the full in-process object graph.

Instead, a separate remote protocol SHOULD expose descriptor DTOs and explicit command messages.

---

# 22. Recommended Frontend Strategy

## 22.1 Architecture vs Product Scope

The core SHALL remain frontend-agnostic.

The initial product SHOULD NOT attempt equal support for GUI, CLI, TUI, web, and voice.

Recommended initial support:

1. **Desktop GUI:** primary reference frontend.
2. **CLI:** secondary reference frontend to validate the abstraction and support headless usage.
3. **TUI:** later if demanded.
4. **Web:** later if remote/shared deployment becomes important.
5. **Voice:** later, after confirmation and ambiguity semantics are mature.

## 22.2 Why CLI Matters

A minimal CLI is valuable because it pressure-tests the descriptor model.

Example commands:

```text
ls
cd /World/Nations[key=USSR]
inspect
get GDP
set TaxRate 0.05
call AdvanceTurn
watch FoodStockpile
batch apply RefillSupply --to Units --arg amount=100
```

If the core can express these interactions without GUI-specific leakage, the separation is probably sound.

---

# 23. Recommended .NET Toolchain

## 23.1 Core Runtime

Recommended implementation stack:

- C#;
- current supported .NET LTS target;
- nullable reference types enabled;
- analyzers enabled;
- source generator project for compile-time metadata;
- reflection fallback library;
- `System.Text.Json` for layout/config serialization;
- `Microsoft.Extensions.DependencyInjection` for optional host integration;
- `Microsoft.Extensions.Logging` for diagnostics.

The core SHOULD avoid dependencies on any desktop UI toolkit.

## 23.2 Desktop Reference Frontend

Recommended choices:

### Avalonia

Advantages:

- cross-platform desktop support;
- strong retained-mode layout system;
- natural fit for modular panes and persistent workbench layouts.

### WPF

Advantages:

- mature .NET desktop ecosystem;
- strong data-binding model;
- useful if Windows-only support is acceptable.

The reference frontend SHOULD be implemented behind adapter interfaces so the core does not depend on either.

## 23.3 CLI Reference Frontend

Recommended implementation:

- `System.CommandLine` or a small custom command parser;
- readline/history support;
- autocomplete from descriptor metadata;
- structured output option such as JSON for scripting.

## 23.4 Testing

The core test suite SHOULD include:

- cyclic graph fixtures;
- shared-reference fixtures;
- mutable collections;
- object replacement;
- broken binding recovery;
- async methods;
- cancellation;
- action exceptions;
- batch partial failure;
- thread-dispatched access;
- layout migration;
- compatibility checks.

Property-based tests are especially appropriate for path resolution, graph traversal, and layout round-tripping.

---

# 24. Proposed Package Structure

```text
RuntimeWorkbench.Core
    descriptors
    object identity
    binding
    validation
    actions
    collections
    change notifications

RuntimeWorkbench.Attributes
    exposure attributes

RuntimeWorkbench.Reflection
    reflection-based descriptor discovery

RuntimeWorkbench.Generators
    Roslyn source generators

RuntimeWorkbench.Layouts
    layout model
    binding persistence
    migration

RuntimeWorkbench.Batch
    batch plan
    compatibility
    preview
    execution

RuntimeWorkbench.Hosting
    dependency injection
    logging
    dispatchers

RuntimeWorkbench.Frontend.Avalonia
    reference GUI

RuntimeWorkbench.Frontend.Cli
    reference CLI
```

Naming is illustrative.

---

# 25. Example End-to-End Domain Model

```csharp
public sealed class WorldSimulation
{
    [Expose]
    public int Turn { get; private set; }

    [Children]
    public List<Nation> Nations { get; } = [];

    [Action]
    public void AdvanceTurn()
    {
        Turn++;
    }

    [Action]
    public async Task RecalculateEconomyAsync(
        int iterations,
        CancellationToken ct)
    {
        // domain operation
    }
}

public sealed class Nation : IStableDomainIdentity
{
    public string DomainKey => $"Nation/{Code}";

    [Expose]
    public required string Code { get; init; }

    [Expose]
    [Range(0, 1)]
    public double TaxRate { get; set; }

    [Expose(ReadOnly = true)]
    public double GDP { get; private set; }

    [Expose]
    public IReadOnlyList<double> GDPHistory => _gdpHistory;

    [Children]
    public List<City> Cities { get; } = [];

    [Summary]
    public string Summary => $"{Code}: GDP {GDP:N0}";

    [Action]
    public void NormalizeTaxes(double rate)
    {
        foreach (var city in Cities)
            city.TaxRate = rate;
    }
}
```

A GUI frontend might generate:

- object browser for `WorldSimulation`;
- nation list with summaries;
- numeric editor for `TaxRate`;
- read-only label for `GDP`;
- chart for `GDPHistory`;
- buttons/forms for actions.

A CLI frontend might expose:

```text
/world/turn
/world/nations[key=USSR]/tax-rate
/world/nations[key=USSR]/gdp
/world/nations[key=USSR]/actions/normalize-taxes
```

A saved layout might contain:

- one GDP chart per selected nation;
- one global turn indicator;
- one `AdvanceTurn` action component;
- one table collecting current tax rates.

---

# 26. Example Batch Operation

User flow:

```text
Collection: World.Nations
Template item: Nation/USSR
Operation: set TaxRate = 0.05
Filter: GDP < 1e12
Execution: sequential, continue on error
Preview: required
```

Internal representation:

```csharp
var plan = new BatchOperationPlan
{
    Selection = TargetSelection.FromCollection("World.Nations"),
    Filters =
    [
        TargetFilter.MemberLessThan("GDP", 1e12)
    ],
    Operation = OperationDescriptor.SetMember("TaxRate", 0.05),
    Policy = new BatchExecutionPolicy
    {
        Sequential = true,
        ContinueOnError = true,
        RequirePreview = true
    }
};
```

Execution output:

```text
Matched: 12
Compatible: 12
Succeeded: 11
Failed: 1

Failure:
Nation/ABC: validation rejected TaxRate because fiscal policy is locked.
```

---

# 27. Diagnostics and Observability

The framework SHOULD produce structured diagnostic events for:

- descriptor discovery;
- binding resolution;
- broken binding;
- action invocation;
- action completion/failure;
- batch preview;
- batch execution;
- layout load/migration;
- frontend capability mismatch;
- thread-dispatch failures.

These events SHOULD integrate with `ILogger`.

---

# 28. Performance Considerations

## 28.1 Reflection Cost

Reflection discovery SHOULD be cached by type.

Repeated descriptor construction SHOULD reuse immutable metadata where possible.

## 28.2 Large Collections

The framework SHALL NOT require materializing arbitrarily large collections for ordinary browsing.

Collection descriptors SHOULD support paging or virtualization.

Batch operations, however, SHOULD snapshot identities before mutation unless explicitly configured otherwise.

## 28.3 Large Object Graphs

Frontends SHOULD load graph branches lazily.

The framework SHOULD avoid recursively traversing the entire exposed object graph unless performing explicit indexing/search.

---

# 29. Search

A later but valuable subsystem SHOULD allow search by:

- display name;
- type;
- domain identity;
- path;
- tags;
- exposed member name;
- summary text.

Search results SHOULD resolve back to object identities and navigation targets.

---

# 30. Versioning and Compatibility

Three kinds of versioning MUST be distinguished:

1. framework API version;
2. layout schema version;
3. domain binding compatibility.

A saved layout MAY break when the application's domain model changes.

The framework SHOULD provide migration hooks such as:

```csharp
bindingMigrations.RenameMember(
    type: "Nation",
    oldMember: "GDPHistory",
    newMember: "EconomicHistory");
```

---

# 31. Error Model

Exceptions SHOULD be treated as a last-resort transport for unexpected failures.

Expected interaction failures SHOULD use structured results.

Examples:

```text
BindingResolutionResult
SetValueResult
ActionValidationResult
ActionExecutionResult
BatchItemResult
LayoutLoadResult
```

A frontend SHOULD be able to display a meaningful user-facing reason without parsing exception strings.

---

# 32. MVP Definition

A credible MVP SHOULD prove the following six properties.

## 32.1 Core

- expose properties and methods using attributes;
- navigate a cyclic live object graph;
- preserve object identity;
- read/write values;
- invoke parameterized sync/async methods;
- observe selected state changes.

## 32.2 GUI

- object browser;
- breadcrumb navigation;
- generated editors;
- generated action forms;
- async progress UI;
- create/delete/rebind components;
- save/load layouts;
- broken binding visualization.

## 32.3 Batch

- choose a collection;
- choose a template item;
- select an exposed writable member or action;
- compatibility preview;
- sequential execution;
- per-item results.

## 32.4 CLI

- navigate;
- inspect;
- get/set;
- call actions;
- basic collection listing.

---

# 33. Post-MVP Roadmap

Recommended order:

### Phase 1 — Runtime Core

- descriptors;
- attributes;
- reflection discovery;
- identity;
- paths;
- cyclic traversal;
- sync actions.

### Phase 2 — Desktop Workbench

- object browser;
- value editors;
- action invocation;
- async progress;
- layout composition;
- persistence.

### Phase 3 — Batch Operations

- collection snapshots;
- compatibility analysis;
- preview;
- sequential execution;
- filters;
- per-item results.

### Phase 4 — CLI

- path navigation;
- get/set/call;
- autocomplete;
- structured output.

### Phase 5 — Advanced Binding

- domain identity recovery;
- migrations;
- search;
- repaired bindings;
- reusable layout templates.

### Phase 6 — Advanced Analysis

- table projections;
- chart descriptors;
- user-defined derived values;
- safe expression system.

### Phase 7 — Optional Remote Protocol

- descriptor serialization;
- explicit command transport;
- authentication;
- authorization;
- audit logging.

### Phase 8 — Additional Frontends

- TUI;
- web;
- voice;
- domain-specific frontends.

---

# 34. Design Decisions That Should Remain Intentionally Deferred

The following are legitimate future decisions and SHOULD NOT block the MVP:

- exact GUI toolkit;
- exact path grammar syntax;
- exact attribute names;
- remote transport protocol;
- arbitrary scripting language;
- generalized undo;
- parallel batch execution;
- distributed object identity;
- collaborative multi-user layouts;
- voice intent system.

The architecture SHALL leave room for these features without pretending they are solved.

---

# 35. Ambiguities Resolved by This Specification

To avoid underspecified implementation, this document adopts the following explicit assumptions.

1. **The initial runtime is in-process.**  
   Remote operation is a later transport layer.

2. **The core is frontend-agnostic.**  
   No GUI toolkit types appear in the core API.

3. **The primary reference product is a desktop workbench.**  
   Multimodal support is an architectural property, not an MVP requirement.

4. **The CLI is a secondary reference frontend.**

5. **Cyclic references are mandatory.**

6. **Stable runtime object identity is mandatory.**  
   Path-only bindings are insufficient.

7. **Domain-stable identity is optional but strongly recommended for persistent layouts.**

8. **Saved layouts persist component configuration and bindings, not domain state.**

9. **Batch operations are constrained descriptors, not arbitrary delegates or scripts.**

10. **Batch operations execute sequentially by default.**

11. **Batch mutation uses preview-first semantics.**

12. **Automatic rollback is out of scope unless supplied by the host domain.**

13. **Thread affinity is explicit through a dispatcher abstraction.**

14. **Type-based component selection is a default, not the only source of interaction semantics.**

15. **Frontend capability support is negotiated rather than assumed.**

---

# 36. Remaining Decisions Requiring Product Owner Input

The design is implementable without these answers, but resolving them will affect public API design.

## 36.1 Exposure Default

Should members be:

- opt-in only (`[Expose]` required), or
- public-by-default with opt-out?

**Recommendation:** opt-in only. This is safer and produces a more intentional interaction surface.

## 36.2 Field Support

Should public fields be first-class exposed members, or should the framework prefer properties?

**Recommendation:** support fields, but encourage properties for validation and stable API semantics.

## 36.3 Layout Ownership

Should layouts belong to:

- a specific application instance;
- an application type/version;
- an individual user profile;
- a portable file independent of user identity?

**Recommendation:** portable serialized files with optional application/user metadata.

## 36.4 Stable Domain Identity Contract

Should persistent identity be supplied by:

- interface;
- attribute;
- registry callback;
- all of the above?

**Recommendation:** support all three through a normalized identity provider interface.

## 36.5 Batch Filter Language

Should the first release support:

- only predefined member comparisons, or
- a typed expression DSL?

**Recommendation:** predefined comparisons first. A typed expression DSL can be added later.

## 36.6 Component Nesting Semantics

Should component nesting be purely frontend-defined, or should the core layout model define containment semantics?

**Recommendation:** the core should define generic component containment and binding metadata; exact geometry remains frontend-specific.

---

# 37. Success Criteria

The framework should be considered successful if a developer can take a non-trivial .NET simulation or backend model and, with minimal annotations and no bespoke application UI, obtain all of the following:

1. navigate the exposed domain graph;
2. inspect live values;
3. modify writable values;
4. invoke domain actions with parameters;
5. observe async progress;
6. create custom workbench panels bound to arbitrary exposed values;
7. save and reload those panels;
8. perform safe previewed batch operations on collections;
9. use the same exposed model through at least one non-GUI frontend;
10. do all of the above without copying the domain model into a separate UI-specific data model.

If these properties hold, the framework is no longer merely an object inspector. It becomes a reusable **runtime domain workbench infrastructure** for .NET applications.

---

# 38. Recommended Project Positioning

A concise technical positioning statement:

> **A .NET runtime interaction framework that exposes live domain objects as a navigable, mutable, invocable object space with pluggable frontends and persistent user-composed workbenches.**

A more product-oriented positioning statement:

> **Build operational workbenches for complex .NET systems without building the workbench by hand. Annotate the domain model, bind reusable components, and interact with live application state through desktop, CLI, or custom frontends.**

---

# 39. Final Recommendation

The project is technically viable if its scope is kept disciplined.

The architecture should preserve multimodal interaction, cyclic runtime object graphs, persistent bindings, and batch operations because these are the features that distinguish it from ordinary property grids and generated admin forms.

However, implementation effort should concentrate on a small number of polished capabilities:

- one strong desktop reference frontend;
- one minimal CLI reference frontend;
- robust identity and binding semantics;
- safe action invocation;
- persistent user layouts;
- constrained batch operations.

The framework should resist pressure to become a full scripting environment, general application builder, or remote object runtime before the core interaction model is proven.

