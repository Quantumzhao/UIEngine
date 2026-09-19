# UIEngine

UIEngine is a .NET framework under active development for exposing live domain objects as a navigable, mutable, and invocable object space with pluggable frontends.

> **Reboot status:** the first runtime-and-CLI milestone is implemented beside the legacy UIEngine v0.2.3 prototype. Clean-checkout and GitHub Actions acceptance have passed.

## Build, Test, and Run the CLI

The solution targets .NET 10.

```sh
dotnet restore UIEngine.sln
dotnet build UIEngine.sln
dotnet test UIEngine.sln
dotnet run --project Frontend/Cli/Cli.csproj
```

The CLI is an architectural proving frontend rather than the product TUI. Use `ls` to list roots or members, `cd` to navigate, and `exit` to quit.

## Repository Guide

- [Project context](PROJECT_CONTEXT.md) — concise source of settled intent and constraints.
- [Architecture, reboot audit, and plan](REBOOT_PLAN.md) — target specification, detailed assessment, decisions, risks, and phased implementation plan.
- [Progress tracker](TODO.MD) — concise checklist derived from the reboot plan.
- `Core/` — reboot runtime contracts, attributes, host, and reflection provider.
- `Frontend/Cli/` — descriptor-driven CLI proving frontend.
- `Examples/CyclicDomain/` — deterministic reboot fixture.
- `Tests/` — reboot and legacy-characterization test projects.
- `UIEngine/` — legacy reflection and node prototype.
- `CLITestProject/` — legacy CLI and cyclic demographic sample.
- `Dataset/` — additional legacy sample types; it is not a test project.

The project is not ready for package publication. Packaging and release metadata will be introduced only after the new APIs, tests, licensing, and release policy are established.
