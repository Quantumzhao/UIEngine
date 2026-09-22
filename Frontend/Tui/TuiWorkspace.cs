using UIEngine.Core;
using XenoAtom.Terminal.UI;

namespace UIEngine.Frontend.Tui;

/// <summary>A disposable UIEngine workspace that can be embedded in a XenoAtom visual tree.</summary>
/// <remarks>
/// The workspace owns its frontend operation lifetime, but not the Core host used to create it. A
/// visual may be attached to one XenoAtom visual tree at a time.
/// </remarks>
public sealed class TuiWorkspace : IDisposable
{
    private readonly TuiOperationScope _Lifetime = new();

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

    /// <summary>Gets whether this workspace has been disposed.</summary>
    public bool IsDisposed => _Lifetime.IsDisposed;

    /// <summary>Gets the root visual to compose into a XenoAtom application.</summary>
    public Visual Visual { get; }

    internal UIEngineHost Host { get; }

    internal TuiOperationScope CreateOperationScope() => _Lifetime.CreateChild();

    /// <summary>Disposes frontend work without disposing the caller-owned Core host.</summary>
    public void Dispose() => _Lifetime.Dispose();
}
