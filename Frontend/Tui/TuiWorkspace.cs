using UIEngine.Core;
using XenoAtom.Terminal.UI;

namespace UIEngine.Frontend.Tui;

/// <summary>A disposable UIEngine workspace that can be embedded in a XenoAtom visual tree.</summary>
/// <remarks>
/// The workspace owns its frontend resources, but not the Core host used to create it. A
/// visual may be attached to one XenoAtom visual tree at a time.
/// </remarks>
public sealed class TuiWorkspace : IDisposable
{
    private int _Disposed;

    internal TuiWorkspace(
        UIEngineHost host,
        TuiFrontendOptions options,
        Visual visual)
    {
        Host = host;
        Options = options;
        Visual = visual;
    }

    /// <summary>Gets the immutable configuration snapshot used by this workspace.</summary>
    public TuiFrontendOptions Options { get; }

    /// <summary>Gets the root visual to compose into a XenoAtom application.</summary>
    public Visual Visual { get; }

    internal UIEngineHost Host { get; }

    /// <summary>Disposes frontend resources without disposing the caller-owned Core host.</summary>
    public void Dispose() => Interlocked.Exchange(ref _Disposed, 1);
}
