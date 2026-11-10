using System.Drawing;
using System.Windows.Forms;

namespace FeatherWall.Tray;

public static class ControlTree
{
    /// <summary>Controls.Clear() only detaches: the removed controls keep their window handles and
    /// GDI objects until the finalizer runs. Disposing removes each one from its parent as well.</summary>
    public static void ClearAndDispose(Control host)
    {
        foreach (var child in host.Controls.Cast<Control>().ToList())
            child.Dispose();
        host.Controls.Clear();
    }
}

/// <summary>The Style page's backdrop: one full-resolution copy of the wallpaper frame, replaced when
/// the wallpaper changes and freed with the window.</summary>
public sealed class BackdropHolder : IDisposable
{
    public Image? Current { get; private set; }

    public void Replace(Image? next)
    {
        var old = Current;
        Current = next;
        if (!ReferenceEquals(old, next)) old?.Dispose();
    }

    public void Dispose() => Replace(null);
}
