# UIEngine

UIEngine is a .NET framework under active development for exposing live domain objects as a navigable, mutable, and invocable object space with pluggable frontends.

> **Reboot status:** the Framework MVP runtime and proving CLI are implementation-complete beside the legacy UIEngine v0.2.3 prototype. Local and artifact-free CI build/test/package-equivalent acceptance pass; GitHub Actions confirmation for Milestone 02 awaits publication.

## Build, Test, and Run the CLI

The solution targets .NET 10.

```sh
dotnet restore UIEngine.sln
dotnet build UIEngine.sln
dotnet test UIEngine.sln
dotnet run --project Frontend/Cli/Cli.csproj
```

The CLI is an architectural proving frontend rather than the product TUI. It covers descriptor-aware navigation and inspection, live reads and validated writes, bounded collection listing, observation, and normalized synchronous/asynchronous calls with progress and cancellation.

## Repository Guide

- [Project context](PROJECT_CONTEXT.md) — concise source of settled intent and constraints.
- [Architecture, reboot audit, and plan](REBOOT_PLAN.md) — target specification, detailed assessment, decisions, risks, and phased implementation plan.
- [Progress tracker](TODO.MD) — concise checklist derived from the reboot plan.
- `Core/` — reboot runtime contracts, attributes, host, and reflection provider.
- `Frontend/Cli/` — descriptor-driven CLI proving frontend.
- `Examples/CyclicDomain/` — deterministic reboot fixture.
- `Tests/` — reboot and legacy-characterization test projects.
- `UIEngine/` — legacy reflection and node prototype.

The project is not ready for package publication. Packaging and release metadata will be introduced only after the new APIs, tests, licensing, and release policy are established.
