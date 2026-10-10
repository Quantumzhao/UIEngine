# UIEngine

UIEngine is a .NET 10 framework for exposing live domain objects as a navigable, editable, and
invocable graph.

Its target architecture has three frontend-neutral layers:

- `UIEngineHost` owns roots, live domain access, runtime handles, and invocation.
- `BaseNode` represents an exposed .NET language feature.
- `UIEngineWorkspace` owns independent `Navigator`s and serializable layout state.

Frontends map node semantics to controls. The reusable TUI is in progress.

## Build, Test, and Run

```sh
dotnet restore UIEngine.sln
dotnet build UIEngine.sln
dotnet test UIEngine.sln --no-build
dotnet run --project Examples/CyclicWorld.Tui/CyclicWorld.Tui.csproj
```

## Minimal Host

```csharp
using UIEngine.Core;
using UIEngine.Core.Attributes;

using var host = new UIEngineHost();

static class ApplicationRoots
{
    [Root]
    public static Model Model { get; } = new();
}
```

The host discovers public static `[Root]` fields and properties, plus public static methods marked
with both `[Root]` and `[Action]`, from the assemblies loaded when it is constructed. Domain members
opt in with `[Expose]`, `[Children]`, and `[Action]`. `[Summary]` supplies an optional summary.

## Minimal TUI Host

```csharp
using UIEngine.Core;
using UIEngine.Frontend.Tui;

using var host = new UIEngineHost();
await TuiFrontend.RunAsync();
```

The active host is process-wide. Closing the TUI disposes frontend resources without stopping the
host or a method invocation that has already started; disposing the host disposes its registered
Core workspaces.

## Documentation

- [Project context](PROJECT_CONTEXT.md) — product scope and settled decisions.
- [Architecture](docs/architecture.md) — nodes, navigators, workspaces, paths, and lifetime.
- [Architecture and delivery plan](REBOOT_PLAN.md) — implementation sequence and acceptance.
- [TUI frontend architecture](docs/tui-frontend-architecture.md) — TUI control and lifetime model.
- [Progress tracker](TODO.MD) — completed foundation and remaining work.
- [Milestone records](docs/milestones/README.md) — delivery plans and acceptance records.

Package publication remains disabled until compatibility, licensing, release, and support policy
are defined.
