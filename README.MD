# UIEngine

UIEngine is a proposed .NET framework for exposing live domain objects as a navigable, mutable, and invocable object space with pluggable frontends.

> **Reboot status:** the current implementation is the legacy UIEngine v0.2.3 prototype. The reboot keeps the UIEngine name while replacing its architecture; that new architecture has not been implemented yet.

## What Works Today

The legacy prototype demonstrates a small part of the intended interaction model:

- opt-in property and method exposure through attributes;
- reflection-based discovery of exposed members;
- basic property reads and writes;
- synchronous method invocation with parameters and return values;
- eager wrapping of simple generic collections;
- partial `INotifyPropertyChanged` and `INotifyCollectionChanged` integration;
- a minimal command-line demonstration.

These capabilities are experimental. The repository has no automated tests, and several collection, notification, method, and graph-navigation paths are incomplete or unsafe.

## What Is Not Implemented

The current code does not provide the descriptor API, cyclic graph identity, durable bindings, async action model, validation, layout persistence, safe batch operations, production CLI, or product TUI described by the design.

The old tree-oriented `Dashboard`/`Node` API will not be preserved as a compatibility layer. New development will happen behind a clean UIEngine API while the prototype remains available only as a reference and fixture source.

The framework MVP is the new core plus a minimal CLI. The product MVP adds a TUI over that same frontend-independent core.

## Build and Run the Legacy Prototype

The solution targets .NET 8.

```sh
dotnet build UIEngine.sln
dotnet run --project CLITestProject/CLITestProject.csproj
```

The CLI is a rough demonstration rather than a supported user interface. Enter `show` to list exposed roots and `exit` to quit.

## Repository Guide

- [UIEngine design](runtime-domain-workbench-design.md) — target product and architecture.
- [Project context](PROJECT_CONTEXT.md) — concise source of settled intent and constraints.
- [Reboot audit and plan](REBOOT_PLAN.md) — detailed assessment, decisions, risks, and phased implementation plan.
- [Progress tracker](TODO.MD) — concise checklist derived from the reboot plan.
- `UIEngine/` — legacy reflection and node prototype.
- `CLITestProject/` — legacy CLI and cyclic demographic sample.
- `Dataset/` — additional legacy sample types; it is not a test project.

The project is not ready for package publication. Packaging and release metadata will be introduced only after the new APIs, tests, licensing, and release policy are established.
