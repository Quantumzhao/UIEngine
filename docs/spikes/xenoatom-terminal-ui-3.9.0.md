# XenoAtom.Terminal.UI 3.9.0 Qualification

**Decision:** Accepted for Milestone 03 behind the `UIEngine.Frontend.Tui` boundary.

**Qualified on:** 2026-09-21

**Scope:** Public APIs in the published NuGet packages. No source build, reflection, unsupported
internals, or physical-terminal automation is required by the spike.

## Evidence

The disposable project at
`Spikes/XenoAtom.Terminal.UI.Spike/XenoAtom.Terminal.UI.Spike.csproj` pins
`XenoAtom.Terminal.UI` to exactly `3.9.0`. Its automated tests prove that the published packages
support:

- fullscreen hosting with deterministic shutdown;
- composition of a UIEngine-owned visual inside a caller-owned visual tree and `TerminalApp`;
- keyboard-event injection, focus traversal, and activation through `InMemoryTerminalBackend`;
- resize-event injection followed by layout at the new width;
- UI-thread marshalling from a worker through the public dispatcher;
- replacement of a `ListBox<T>` window from two bounded Core reads; and
- a 10,000-item lazy Core source is not fully enumerated (the reads inspect 4 and then 7 items,
  including the look-ahead used to establish `HasMore`).

Run the qualification with:

```sh
dotnet test Spikes/XenoAtom.Terminal.UI.Spike/XenoAtom.Terminal.UI.Spike.csproj
```

The assertions intentionally inspect semantic state and selected output text rather than broad
character-perfect terminal snapshots.

## Accepted dependency graph

| Package | Version | Relationship | License |
|---|---:|---|---|
| `XenoAtom.Terminal.UI` | 3.9.0 | Direct | BSD-2-Clause |
| `XenoAtom.Terminal` | 2.2.0 | Transitive | BSD-2-Clause |
| `XenoAtom.Ansi` | 1.7.0 | Transitive | BSD-2-Clause |
| `Wcwidth` | 4.0.1 | Transitive | MIT |

The accepted packages identify upstream commits `424e384a785c85f3815f5687c70d4dda8b8053d1`
(Terminal.UI), `5517cb3d8cdf0532ecc89260067064f98cde6137` (Terminal),
`7d1d52ef596dfb8ef8139e30b83fe2a902a380cf` (Ansi), and
`64a3f523d5dc27124e04ff9079137ac2c5c72143` (Wcwidth). The three XenoAtom packages attribute
copyright to Alexandre Mutel. The Wcwidth package attributes authorship to Patrik Svensson and
contributors.
NuGet's advisory query reported no known vulnerability in the resolved graph on 2026-09-21. None
of the toolkit runtime packages is deprecated; the disposable test project retains the repository's
existing xUnit 2 line, which NuGet labels legacy, and does not make it a product dependency.

BSD-2-Clause and MIT both require their copyright and permission text to accompany redistributed
copies. Packaging remains disabled, so no release notice is added by this spike. Before enabling
packaging, add the complete license texts and these attributions to the product's third-party
notices.

Sources: [UI package metadata](https://www.nuget.org/packages/XenoAtom.Terminal.UI/3.9.0),
[Terminal repository](https://github.com/XenoAtom/XenoAtom.Terminal),
[Ansi repository](https://github.com/XenoAtom/XenoAtom.Ansi), and
[Wcwidth repository](https://github.com/spectreconsole/wcwidth).

## Known limits and integration rules

- The accepted packages target .NET 10 and use C# 14 APIs.
- UI visuals, state, and the dispatcher are single-thread-affine. Domain/background results must
  be marshalled to the UI dispatcher.
- Routed input handlers are synchronous. They may enqueue intent, but must not be `async void`.
- Only one asynchronous host update callback is in flight. Independent work belongs off the UI
  thread and publishes results through the dispatcher.
- Fullscreen hosting owns the calling thread and alternate screen until exit. Embedders should
  compose the UIEngine visual into their existing tree instead of starting a second loop.
- Windows Console and Unix terminals on Linux/macOS are supported by the terminal package, but
  color depth, mouse input, extended keys, clipboard, and terminal protocols are capability-based.
  Product workflows must retain keyboard-only and plain-text fallbacks.
- The automated spike qualifies the public in-memory backend on Linux. It does not replace
  physical-terminal smoke tests on supported operating systems, nor does it prove behavior through
  terminal multiplexers or remote shells.
- Toolkit recycling reduces visual work only. UIEngine collection access must remain bounded by a
  Core `CollectionSlice`; a virtualized control never authorizes an unbounded source read.

The spike found no missing Core semantic information and does not justify a Core capability or
toolkit contract.

## Upgrade procedure

1. Read the upstream release notes and hosting, async, input, and data-templating documentation.
2. Change the exact package version in the spike first; never use a floating range.
3. Restore and run `dotnet list ... package --include-transitive`; compare every resolved runtime
   dependency, package repository commit, license expression, deprecation, and vulnerability result
   with this record.
4. Run the spike tests, then the complete solution build and tests with zero warnings.
5. Repeat physical smoke tests on the supported Windows, Linux, and macOS terminal matrix before a
   product release.
6. Only after those checks pass, update the production TUI reference and this qualification record.

An upgrade is rejected or deferred if a mandatory behavior requires internal APIs, weakens
deterministic input/resize coverage, breaks dispatcher isolation, or makes bounded collection
presentation impossible.
