# UIEngine

UIEngine is a .NET 10 framework for exposing live domain objects as a navigable, editable, and
invocable object graph. It provides a small concrete Core runtime, a descriptor-driven CLI, and an
in-progress reusable TUI frontend.

## Build, Test, and Run

```sh
dotnet restore UIEngine.sln
dotnet build UIEngine.sln
dotnet test UIEngine.sln --no-build
dotnet run --project Frontend/Cli/Cli.csproj
dotnet run --project Examples/CyclicWorld.Tui/CyclicWorld.Tui.csproj
```

The CLI demonstrates graph navigation, reflection and third-party scalar exposure, validation,
bounded collection reads, replacement-safe bindings, dispatch, observation, and synchronous or
asynchronous actions with progress and cancellation.

The TUI executable currently proves the reusable library, ownership, and hosting boundaries. Its
interactive graph workspace is implemented in the later Product MVP steps.

## Minimal Host

```csharp
using UIEngine.Core;

using var host = new UIEngineHost();
var registered = host.SetRoot("model", model);
var descriptor = await host.DescribeAsync(registered.Value);
```

Domain members opt in with `[Expose]`, `[Children]`, and `[Action]`. `[Summary]` supplies an
optional object summary, while `IStableDomainIdentity` or `[DomainIdentitySource]` supplies an
optional replacement-stable identity. Unannotated third-party scalar types can use immutable
`TypeExposure<T>` and `ValueExposure<T,TValue>` definitions in `UIEngineHostOptions.Exposures`.

## Minimal TUI Host

```csharp
using UIEngine.Core;
using UIEngine.Frontend.Tui;

using var host = new UIEngineHost();
host.SetRoot("model", model);

await TuiFrontend.RunAsync(host, cancellationToken: stoppingToken);
```

`TuiFrontend.RunAsync` and `TuiFrontend.CreateWorkspace` both leave the supplied host under caller
ownership. An embedding application owns and disposes the returned `TuiWorkspace`, then composes
its `Visual` into an existing XenoAtom visual tree.

## Repository Guide

- [Project context](PROJECT_CONTEXT.md) — settled scope, decisions, and invariants.
- [Architecture, reboot audit, and plan](REBOOT_PLAN.md) — migration rationale, streamlining
  decisions, current design, risks, and roadmap.
- [Progress tracker](TODO.MD) — completed work and remaining product milestones.
- [Architecture](docs/architecture.md) — runtime flow and concrete public model.
- [CLI command reference](docs/cli-command-reference.md) — commands, paths, and output.
- [Milestone records](docs/milestones/README.md) — historical plans and acceptance records for the
  architecture runway and Framework MVP hardening.
- `Core/` — runtime implementation and public API.
- `Frontend/Cli/` — proving frontend.
- `Frontend/Tui/` — reusable XenoAtom-based TUI library.
- `Examples/CyclicDomain/` — deterministic cyclic example.
- `Examples/CyclicWorld.Tui/` — thin TUI composition executable.
- `Tests/` — framework, CLI, and TUI boundary tests.

Package publication remains disabled until compatibility, licensing, release policy, and support
expectations are established.
