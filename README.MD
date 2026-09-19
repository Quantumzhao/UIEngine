# UIEngine

UIEngine is a .NET framework under active development for exposing live domain objects as a navigable, mutable, and invocable object space with pluggable frontends.

> **Reboot status:** the first runtime-and-CLI milestone is implemented beside the legacy UIEngine v0.2.3 prototype. Clean-checkout and GitHub Actions acceptance have passed.

## What Works Today

The reboot implementation currently provides:

- host-scoped root registration, runtime identity, and cycle-safe graph traversal;
- opt-in reflection discovery with cached type metadata and semantic descriptors;
- live scalar reads and writes with conversion, validation, and structured failures;
- explicitly requested finite collection snapshots;
- named-parameter synchronous action invocation through asynchronous-shaped contracts;
- a deterministic cyclic sample and a CLI with navigation, inspection, mutation, invocation, history, and completion; and
- automated framework, CLI, dependency, and legacy-characterization tests.

The legacy prototype remains as characterized reference material while the clean-break replacement is developed.

## What Is Not Implemented

The current code does not yet provide programmatic exposure, stable domain identity, durable binding recovery, dispatcher-mediated access, normalized observation, paging or virtualization, asynchronous actions and progress, layout persistence, safe batch operations, or the product TUI described by the design.

The old tree-oriented `Dashboard`/`Node` API will not be preserved as a compatibility layer. New development continues behind a clean UIEngine API while the prototype remains available only as a reference and fixture source.

The framework MVP is the new core plus a minimal CLI. The product MVP adds a TUI over that same frontend-independent core.

## Build, Test, and Run the CLI

The solution targets .NET 10.

```sh
dotnet restore UIEngine.sln
dotnet build UIEngine.sln
dotnet test UIEngine.sln
dotnet run --project Framework/UIEngine.Frontend.Cli/UIEngine.Frontend.Cli.csproj
```

The CLI is an architectural proving frontend rather than the product TUI. Use `ls` to list roots or members, `cd` to navigate, and `exit` to quit.

## Repository Guide

- [UIEngine design](runtime-domain-workbench-design.md) — target product and architecture.
- [Project context](PROJECT_CONTEXT.md) — concise source of settled intent and constraints.
- [Reboot audit and plan](REBOOT_PLAN.md) — detailed assessment, decisions, risks, and phased implementation plan.
- [Progress tracker](TODO.MD) — concise checklist derived from the reboot plan.
- `Framework/UIEngine.Framework/` — reboot runtime contracts, host, and reflection provider.
- `Framework/UIEngine.Frontend.Cli/` — descriptor-driven CLI proving frontend.
- `Samples/UIEngine.Samples.CyclicDomain/` — deterministic reboot fixture.
- `Tests/` — reboot and legacy-characterization test projects.
- `UIEngine/` — legacy reflection and node prototype.
- `CLITestProject/` — legacy CLI and cyclic demographic sample.
- `Dataset/` — additional legacy sample types; it is not a test project.

The project is not ready for package publication. Packaging and release metadata will be introduced only after the new APIs, tests, licensing, and release policy are established.
