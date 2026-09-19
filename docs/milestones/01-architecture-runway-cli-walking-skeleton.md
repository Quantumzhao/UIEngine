# Milestone 01 — Architecture Runway and CLI Walking Skeleton

**Status:** Complete

**Acceptance:** Local and GitHub Actions acceptance passed

**Prerequisite:** The removed package credential has been revoked through its provider.  
**Governing documents:** [Project context](../../PROJECT_CONTEXT.md), [target design](../../runtime-domain-workbench-design.md), and [reboot plan](../../REBOOT_PLAN.md)

This outline does not authorize implementation. Work begins only when the milestone is explicitly selected for implementation.

## Summary

Establish the remaining safety baseline and implement the smallest end-to-end replacement slice:

`Annotated domain model -> reflection provider -> host-scoped graph runtime -> semantic descriptors -> CLI`

The legacy projects remain frozen in place except for characterization tests and explicit baseline fixes. New projects must not depend on legacy `Dashboard` or `Node` APIs.

This milestone excludes asynchronous invocation, observation, paging and virtualization, durable binding recovery, the product TUI, layouts, batch operations, packaging, and legacy removal.

## Baseline and Repository Structure

- Add a `global.json` that selects the supported .NET 10 SDK feature band used by the repository.
- Apply nullable analysis, implicit usings, and analyzers strictly to new projects. Keep any necessary legacy exemptions explicit and local rather than suppressing findings repository-wide.
- Use xUnit for characterization, unit, and integration tests.
- Add GitHub Actions checks for clean restore, build, and test, plus a current-working-tree secret scan. Historical secret scanning and Git-history rewriting remain outside this milestone.
- Organize the solution into `Legacy`, `Framework`, `Samples`, and `Tests` solution folders without moving the existing legacy files.
- Add these new projects:
  - `UIEngine.Framework`, with Core, Attributes, and Reflection organized as namespaces and directories
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

## Acceptance Record

Local acceptance passed on 2026-09-19 from a clean archive of commit `6837c72`:

- a no-cache restore completed successfully;
- the Release solution build completed with zero warnings and errors;
- all 29 framework, CLI, dependency, repository, and legacy-characterization tests passed;
- Gitleaks 8.30.1 reported no secrets in the current tree; and
- the clean build produced no `.nupkg` files.

GitHub Actions run [#1](https://github.com/Quantumzhao/UIEngine/actions/runs/35424138427) completed successfully for commit `6837c72`, confirming the configured restore, build, test, secret-scan, and package checks. The ignored legacy package artifact in the developer working copy predates this acceptance run, is not tracked, and was not reproduced by the clean build.

## Implementation Steps

Keep the repository buildable after every step. Each step should include its smallest relevant tests; the final acceptance pass verifies the steps together rather than introducing untested behavior.

### 1. Close the safety prerequisite

1. Confirm that the removed package credential has been revoked through its provider.
2. Verify that the credential is absent from the current working tree and record only the confirmation, never the credential itself.

Stop here if revocation cannot be confirmed.

### 2. Capture the legacy baseline

1. Add the legacy characterization-test project and configure tests that touch the static `Dashboard` state to run serially.
2. Add narrow tests for explicit discovery, property reads and writes, synchronous invocation, simple collection enumeration, and `INotifyPropertyChanged` propagation.
3. Run the characterization tests against the unchanged legacy implementation.

### 3. Establish the build runway

1. Add `global.json` for the selected .NET 10 SDK feature band.
2. Enable nullable analysis, implicit usings, and analyzers for new projects while keeping any legacy exceptions local.
3. Add restore, build, test, and current-working-tree secret-scan checks to GitHub Actions.
4. Verify that packaging remains disabled and a normal build produces no `.nupkg` files.

### 4. Create the new project skeleton

1. Add the `Framework`, `Samples`, and `Tests` solution folders while leaving the legacy files in place under `Legacy`.
2. Add the Framework and CLI projects, organize Core, Attributes, and Reflection within the framework by namespace and directory, and add the deterministic sample-domain, framework-test, and CLI-test projects.
3. Add only the intended one-way project references and a guard test or inspection check that rejects references from new implementation projects to legacy assemblies.
4. Build the empty project graph before adding runtime behavior.

### 5. Introduce contracts and host scope

1. Add the initial opt-in attributes and tests proving that unannotated members remain hidden.
2. Add semantic descriptor contracts and `InteractionResult<T>` with stable error codes.
3. Add `UIEngineHost`, explicit root registration, and structured duplicate-root failures.
4. Add host-scoped `ObjectIdentity` and `ObjectHandle`, with tests for repeated references and cross-host isolation.

### 6. Add reflection discovery and graph traversal

1. Implement type-level reflection metadata caching for explicitly exposed public instance members.
2. Classify exposed members as scalar values, navigable references, collections, or actions.
3. Produce deterministic, unique descriptor identifiers within each object descriptor.
4. Resolve references lazily through handles and prove that cycles terminate without recursively expanding a tree.

### 7. Add live operations

1. Implement live value reads, read-only detection, and writes against current domain state.
2. Add conversion for strings, primitive numeric types, booleans, nullable forms, and enums, including structured conversion and validation failures.
3. Implement explicitly requested finite collection snapshots whose object elements retain host-scoped identity.
4. Implement named-parameter binding and synchronous action invocation through the asynchronous-shaped contract, including structured invalid-input and domain-exception results.

### 8. Prove the slice through the CLI

1. Add the deterministic cyclic sample and verify that `World -> Nation -> Capital City -> Owner Nation` returns to the original nation identity.
2. Implement tokenization and command dispatch for quoted strings, case-insensitive verbs, case-sensitive member identifiers, and numeric collection indices.
3. Add `ls`, absolute and parent `cd`, `inspect`, `get`, `set`, `call`, and `exit` one at a time with focused integration tests.
4. Verify that malformed commands and structured operation failures are printed without terminating the session.

### 9. Run milestone acceptance

1. Run the complete contract, identity, exposure, value, action, collection, CLI, dependency, and repository test set.
2. Run a clean restore and `dotnet build UIEngine.sln`, requiring zero warnings and errors.
3. Confirm every exit criterion above and check only genuinely completed items in [TODO.MD](../../TODO.MD).

Later framework hardening begins only after this milestone satisfies every exit criterion.
