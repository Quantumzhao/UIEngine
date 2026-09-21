# UIEngine

UIEngine is a .NET 10 framework for exposing live domain objects as a navigable, editable, and
invocable object graph. It provides a small concrete Core runtime and a descriptor-driven CLI.

## Build, Test, and Run

```sh
dotnet restore UIEngine.sln
dotnet build UIEngine.sln
dotnet test UIEngine.sln --no-build
dotnet run --project Frontend/Cli/Cli.csproj
```

The CLI demonstrates graph navigation, reflection and third-party scalar exposure, validation,
bounded collection reads, replacement-safe bindings, dispatch, observation, and synchronous or
asynchronous actions with progress and cancellation.

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

## Repository Guide

- [Project context](PROJECT_CONTEXT.md) — settled scope, decisions, and invariants.
- [Architecture](docs/architecture.md) — runtime flow and concrete public model.
- [CLI command reference](docs/cli-command-reference.md) — commands, paths, and output.
- `Core/` — runtime implementation and public API.
- `Frontend/Cli/` — proving frontend.
- `Examples/CyclicDomain/` — deterministic cyclic example.
- `Tests/` — framework and CLI black-box tests.

Package publication remains disabled until compatibility, licensing, release policy, and support
expectations are established.
