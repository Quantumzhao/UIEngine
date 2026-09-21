using XenoAtom.Terminal.UI;

namespace UIEngine.Frontend.Tui;

/// <summary>A disposable UIEngine workspace that can be embedded in a XenoAtom visual tree.</summary>
/// <remarks>
/// The workspace owns its <see cref="Session"/> but not the Core host used to create it. A visual
/// may be attached to one XenoAtom visual tree at a time.
/// </remarks>
public sealed class TuiWorkspace : IDisposable
{
    internal TuiWorkspace(TuiSession session, Visual visual)
    {
        Session = session;
        Visual = visual;
    }

    /// <summary>Gets the session shared by the embedded and fullscreen hosting paths.</summary>
    public TuiSession Session { get; }

    /// <summary>Gets the root visual to compose into a XenoAtom application.</summary>
    public Visual Visual { get; }

    /// <summary>Disposes the frontend session without disposing the caller-owned Core host.</summary>
    public void Dispose() => Session.Dispose();
}
