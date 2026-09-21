using UIEngine.Core;

namespace UIEngine.Frontend.Tui;

/// <summary>Owns the state and resources for one TUI workspace.</summary>
/// <remarks>
/// Disposing a session releases only frontend-owned resources. The <see cref="UIEngineHost"/>
/// supplied by the caller remains caller-owned and is never disposed by this type.
/// </remarks>
public sealed class TuiSession : IDisposable
{
    private int _Disposed;

    internal TuiSession(UIEngineHost host, TuiFrontendOptions options)
    {
        Host = host;
        Options = options;
    }

    /// <summary>Gets the immutable configuration snapshot used by this session.</summary>
    public TuiFrontendOptions Options { get; }

    /// <summary>Gets whether this session has been disposed.</summary>
    public bool IsDisposed => Volatile.Read(ref _Disposed) != 0;

    internal UIEngineHost Host { get; }

    /// <summary>Releases frontend-owned resources without disposing the caller-owned host.</summary>
    public void Dispose() => Interlocked.Exchange(ref _Disposed, 1);
}
