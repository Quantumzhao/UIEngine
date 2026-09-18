# Milestone 0001 — Architecture Runway and CLI Walking Skeleton

**Status:** Planned  
**Prerequisite:** The removed package credential has been revoked through its provider.  
**Governing documents:** [Project context](../../PROJECT_CONTEXT.md), [target design](../../runtime-domain-workbench-design.md), and [reboot plan](../../REBOOT_PLAN.md)

This outline does not authorize implementation. Work begins only when the milestone is explicitly selected for implementation.

## Summary

Establish the remaining safety baseline and implement the smallest end-to-end replacement slice:

`Annotated domain model -> reflection provider -> host-scoped graph runtime -> semantic descriptors -> CLI`

The legacy projects remain frozen in place except for characterization tests and explicit baseline fixes. New projects must not depend on legacy `Dashboard` or `Node` APIs.

This milestone excludes asynchronous invocation, observation, paging and virtualization, durable binding recovery, the product TUI, layouts, batch operations, packaging, and legacy removal.

## Baseline and Repository Structure

- Add a `global.json` that selects a supported .NET 8 SDK feature band without advancing the target framework.
- Apply nullable analysis, implicit usings, and analyzers strictly to new projects. Keep any necessary legacy exemptions explicit and local rather than suppressing findings repository-wide.
- Use xUnit for characterization, unit, and integration tests.
- Add GitHub Actions checks for clean restore, build, and test, plus a current-working-tree secret scan. Historical secret scanning and Git-history rewriting remain outside this milestone.
- Organize the solution into `Legacy`, `Framework`, `Samples`, and `Tests` solution folders without moving the existing legacy files.
- Add these new projects:
  - `UIEngine.Core`
  - `UIEngine.Attributes`
  - `UIEngine.Reflection`
  - `UIEngine.Frontend.Cli`
  - a deterministic sample-domain project
  - a legacy characterization-test project
  - framework and CLI test projects
- Prevent new implementation projects from referencing legacy assemblies. Only legacy characterization tests may reference the old `UIEngine` assembly.
- Keep package generation disabled.

## Initial Public Contracts

These contracts define the minimum vertical slice. Exact signatures may be refined during implementation only when tests or language constraints require it; any architectural change must first be reconciled with the governing documents.

- `UIEngineHost` owns roots, providers, runtime identity, and lifetime. There is no global registry.
- `ObjectIdentity` represents reference-based identity within one host.
- `ObjectHandle` identifies a live runtime object without exposing the object instance as frontend state.
- Semantic descriptor roles are represented by `IObjectDescriptor`, `IValueDescriptor`, `IReferenceDescriptor`, `ICollectionDescriptor`, and `IActionDescriptor`.
- `IObjectDescriptorProvider` allows the runtime to obtain descriptors without coupling the core to reflection.
- `InteractionResult<T>` represents expected success and failure with stable error codes. Frontends must not parse exception messages.
- `[Expose]`, `[Action]`, `[Children]`, and `[Summary]` provide the initial opt-in reflection vocabulary.
- Runtime operations accept `CancellationToken` and return `ValueTask`-based structured results from the outset, even where the first implementation completes synchronously.

Descriptors express domain semantics and operations, never CLI or TUI controls.

## Runtime Behavior

- Roots are registered explicitly on a host; duplicate root identifiers are rejected with a structured error.
- Runtime identity is scoped to a host. Repeated encounters with the same object reference produce the same identity, while separate hosts remain isolated.
- Graph traversal is lazy and cycle-safe. Discovering a reference returns a descriptor and handle rather than recursively expanding an object tree.
- Reflection metadata is cached by type and separated from live object state.
- Reflection discovers only explicitly exposed public instance properties and methods.
- Strings and scalar types are values, object references are navigable references, and enumerable members are collections.
- Descriptor identifiers are unique within their declaring object descriptor and stable for the lifetime of the reflected type metadata.
- Reads, writes, and calls operate on current domain state rather than mirrored node state.
- The initial conversion layer supports strings, primitive numeric types, booleans, nullable forms, and enums, returning structured conversion or validation failures.
- Actions are synchronous in this milestone, but use the asynchronous-shaped operation contract.
- Collection enumeration produces a finite snapshot only when the caller explicitly requests it. Paging and live collection semantics are deferred.

## CLI Slice

Implement a dependency-free command dispatcher over the semantic contracts. The CLI is an architectural proving frontend, not the product TUI.

Commands:

- `ls` lists roots or members at the current location.
- `cd /root/Member/0` navigates an absolute logical path; `cd ..` navigates to the parent location.
- `inspect` shows descriptor roles, metadata, current runtime identity, and available operations.
- `get <member>` reads a value.
- `set <member> <value>` converts and writes a value.
- `call <action> name=value ...` invokes a parameterized synchronous action.
- `exit` ends the session.

Command verbs are case-insensitive; exposed member identifiers are case-sensitive. Quoted strings and numeric collection indices are supported. Syntax, missing-target, conversion, validation, and invocation failures are printed from structured results and do not terminate the session.

Use a deterministic cyclic fixture such as:

`World -> Nation -> Capital City -> Owner Nation`

Navigating back to `Owner Nation` must return the original nation's runtime identity rather than constructing another wrapper subtree.

## Legacy Characterization

Characterize only behavior worth carrying forward conceptually:

- explicit root and member discovery;
- basic property reads and writes;
- synchronous method invocation;
- simple collection enumeration;
- basic `INotifyPropertyChanged` propagation.

Do not encode known broken collection mutation, global-state behavior, node identifiers, parser quirks, or extension-expression behavior as compatibility requirements. Serialize tests that interact with the static legacy `Dashboard` state so they cannot interfere with one another.

## Verification and Test Scenarios

- Identity tests prove repeated references share an identity, cycles terminate, and different hosts are isolated.
- Exposure tests prove unannotated members remain hidden and descriptor identifiers are deterministic and unique.
- Value tests cover live reads, writes, supported conversions, nullability, read-only values, and structured failures.
- Action tests cover parameter discovery, named argument binding, successful synchronous calls, domain exceptions, and invalid input.
- Collection tests prove enumeration is requested explicitly and that cyclic elements preserve runtime identity.
- CLI integration tests exercise `ls`, absolute and parent `cd`, `inspect`, `get`, `set`, `call`, malformed commands, and `exit` over the deterministic fixture.
- Dependency tests or project-reference inspection prove the core is frontend-independent and new projects do not reference legacy assemblies.
- Repository checks prove the revoked credential is absent from the working tree, packaging remains disabled, and normal builds produce no `.nupkg`.
- Clean restore, `dotnet build UIEngine.sln`, and all tests complete with zero warnings and errors in CI.

## Exit Criteria

- The historical package credential is confirmed revoked before implementation begins.
- The legacy behavior selected above has narrow characterization coverage.
- The new project graph enforces dependency direction and does not consume legacy runtime APIs.
- A cyclic/shared-reference sample can be navigated indefinitely without recursive expansion or identity loss.
- The CLI reads and mutates live state and invokes a parameterized action through frontend-neutral contracts.
- Expected failures reach the CLI as structured results.
- CI restores, builds, tests, and scans the current tree successfully.
- No package is generated, and no TUI or post-MVP subsystem has been started.
- Only genuinely completed items are checked in [TODO.MD](../../TODO.MD).

## Implementation Order

1. Confirm external credential revocation.
2. Establish SDK selection, strict defaults for new code, CI, and legacy characterization tests.
3. Add the new projects and enforce their dependency direction.
4. Implement attributes, descriptor contracts, host scope, identity, and reflection discovery.
5. Implement live value, collection-snapshot, and synchronous action operations.
6. Implement the CLI and deterministic cyclic sample.
7. Complete contract, integration, dependency, and repository acceptance checks.

Later framework hardening begins only after this milestone satisfies every exit criterion.
