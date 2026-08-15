using System.Runtime.InteropServices;
using FeatherWall.Common;
using FeatherWall.Interop;
using static FeatherWall.Interop.Win32Constants;

namespace FeatherWall.Desktop;

public enum DesktopTopology
{
    /// <summary>Win10 / Win11 ≤23H2 and early 24H2: wallpaper WorkerW is a top-level
    /// sibling; we SetParent into that WorkerW.</summary>
    ClassicWorkerW,
    /// <summary>2025+ "raised desktop" (HDR-capable shell): SHELLDLL_DefView is a layered
    /// child of Progman; we become a layered WS_CHILD of Progman just below DefView.</summary>
    RaisedDesktop,
}

public sealed record DesktopLayerInfo(DesktopTopology Topology, IntPtr Progman, IntPtr WorkerW, IntPtr DefView);

/// <summary>Owns the fragile part: spawning/finding the wallpaper layer, attaching our
/// windows behind the desktop icons on both known shell topologies, re-attaching when
/// explorer restarts or the layer is destroyed, and restoring the desktop on exit.</summary>
public sealed class DesktopLayerHost : IDisposable
{
    private const uint WM_SPAWN_WORKER = 0x052C;

    private readonly List<IntPtr> _attached = [];
    private User32.WinEventProc? _winEventProc; // rooted while hook lives
    private IntPtr _winEventHook;

    public DesktopLayerInfo Layer { get; private set; } = new(DesktopTopology.ClassicWorkerW, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

    /// <summary>Raised (on the WinEvent/message thread) when the wallpaper layer died and
    /// was re-created; the engine must re-attach all wallpaper windows.</summary>
    public event Action? LayerLost;

    public static DesktopLayerInfo Probe()
    {
        var progman = User32.FindWindowW("Progman", null);
        if (progman == IntPtr.Zero)
            throw new InvalidOperationException("Progman not found — is explorer.exe running?");

        // Ask Progman to spawn the wallpaper WorkerW. No-op if it already exists.
        User32.SendMessageTimeoutW(progman, WM_SPAWN_WORKER, new IntPtr(0xD), new IntPtr(0x1), SMTO_NORMAL, 1000, out _);

        bool raised = ((long)User32.GetWindowLongPtrW(progman, GWL_EXSTYLE) & WS_EX_NOREDIRECTIONBITMAP) != 0;
        if (raised)
        {
            var defView = User32.FindWindowExW(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            var workerW = User32.FindWindowExW(progman, IntPtr.Zero, "WorkerW", null);
            return new DesktopLayerInfo(DesktopTopology.RaisedDesktop, progman, workerW, defView);
        }

        // Classic: find the top-level window hosting SHELLDLL_DefView, then take the next
        // top-level WorkerW sibling after it (covers both the Win10 WorkerW-hosted DefView
        // and the Win11 Progman-hosted DefView variants).
        IntPtr host = IntPtr.Zero, worker = IntPtr.Zero, shellDefView = IntPtr.Zero;
        User32.EnumWindowsProc enumProc = (hwnd, _) =>
        {
            var dv = User32.FindWindowExW(hwnd, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (dv != IntPtr.Zero)
            {
                host = hwnd;
                shellDefView = dv;
                worker = User32.FindWindowExW(IntPtr.Zero, hwnd, "WorkerW", null);
                return false;
            }
            return true;
        };
        User32.EnumWindows(enumProc, IntPtr.Zero);
        GC.KeepAlive(enumProc);
        return new DesktopLayerInfo(DesktopTopology.ClassicWorkerW, progman, worker, shellDefView);
    }

    /// <summary>Probes with retries — on 24H2+ the WorkerW is created lazily and may not
    /// exist for a while after logon.</summary>
    public void EnsureLayer()
    {
        for (int attempt = 0; ; attempt++)
        {
            Layer = Probe();
            bool ok = Layer.Topology == DesktopTopology.RaisedDesktop
                ? Layer.WorkerW != IntPtr.Zero || Layer.DefView != IntPtr.Zero
                : Layer.WorkerW != IntPtr.Zero;
            if (ok)
            {
                Log.Info($"Desktop layer ready: {Layer.Topology} progman=0x{Layer.Progman:X} workerW=0x{Layer.WorkerW:X} defView=0x{Layer.DefView:X}");
                InstallLayerWatch();
                return;
            }
            if (attempt >= 20)
                throw new InvalidOperationException("Wallpaper layer (WorkerW) did not appear after 20 attempts. Ensure desktop icons are enabled.");
            Thread.Sleep(300);
        }
    }

    /// <summary>Parents <paramref name="hwnd"/> into the wallpaper layer and positions it to
    /// cover <paramref name="screenBounds"/> (virtual-screen coordinates). Content must be
    /// presented via DirectComposition (see CompositionHost) — redirection-surface painting
    /// is not composed on raised desktops.</summary>
    public void Attach(IntPtr hwnd, RECT screenBounds)
    {
        IntPtr parent = Layer.Topology == DesktopTopology.RaisedDesktop ? Layer.Progman : Layer.WorkerW;
        if (parent == IntPtr.Zero) throw new InvalidOperationException("Desktop layer not initialized.");

        if (Layer.Topology == DesktopTopology.RaisedDesktop)
        {
            // Child style BEFORE SetParent, then slot directly below SHELLDLL_DefView.
            // WS_POPUP and WS_CHILD are mutually exclusive — swap, never combine.
            var style = (long)User32.GetWindowLongPtrW(hwnd, GWL_STYLE);
            User32.SetWindowLongPtrW(hwnd, GWL_STYLE, new IntPtr((style & ~(long)WS_POPUP) | WS_CHILD));
        }

        if (User32.SetParent(hwnd, parent) == IntPtr.Zero)
            throw new InvalidOperationException($"SetParent into wallpaper layer failed: {Marshal.GetLastWin32Error()}");

        User32.GetWindowRect(parent, out var parentRect);
        var client = MonitorTracker.ScreenToParentClient(screenBounds, parentRect);
        User32.SetWindowPos(hwnd, IntPtr.Zero, client.Left, client.Top, client.Width, client.Height,
            SWP_NOZORDER | SWP_NOACTIVATE | SWP_SHOWWINDOW);

        if (Layer.Topology == DesktopTopology.RaisedDesktop && Layer.DefView != IntPtr.Zero)
        {
            // Below the icons (DefView), above the shell's own WorkerW.
            User32.SetWindowPos(hwnd, Layer.DefView, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            EnsureWorkerWAtBottom();
        }

        _attached.Add(hwnd);
        Log.Info($"Attached 0x{hwnd:X} at {client} ({Layer.Topology})");
    }

    /// <summary>Places an overlay (widget) window in the layer directly above the wallpaper
    /// windows but still under the desktop icons.</summary>
    public void AttachOverlay(IntPtr hwnd, RECT screenBounds)
    {
        Attach(hwnd, screenBounds);
        AssertOverlayZOrder(hwnd);
    }

    /// <summary>Keeps an overlay above the wallpaper surfaces after a new wallpaper attach.</summary>
    public void AssertOverlayZOrder(IntPtr hwnd)
    {
        if (Layer.Topology == DesktopTopology.RaisedDesktop && Layer.DefView != IntPtr.Zero)
            User32.SetWindowPos(hwnd, Layer.DefView, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        else
            User32.SetWindowPos(hwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    /// <summary>Screen rect of the layer parent — children position relative to this.</summary>
    public RECT ParentScreenRect()
    {
        IntPtr parent = Layer.Topology == DesktopTopology.RaisedDesktop ? Layer.Progman : Layer.WorkerW;
        User32.GetWindowRect(parent, out var rect);
        return rect;
    }

    private void EnsureWorkerWAtBottom()
    {
        if (Layer.WorkerW != IntPtr.Zero && User32.IsWindow(Layer.WorkerW))
            User32.SetWindowPos(Layer.WorkerW, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    private void InstallLayerWatch()
    {
        if (_winEventHook != IntPtr.Zero || Layer.WorkerW == IntPtr.Zero) return;
        User32.GetWindowThreadProcessId(Layer.WorkerW, out uint pid);
        _winEventProc = OnWinEvent;
        _winEventHook = User32.SetWinEventHook(EVENT_OBJECT_DESTROY, EVENT_OBJECT_DESTROY,
            IntPtr.Zero, _winEventProc, pid, 0, WINEVENT_OUTOFCONTEXT);
    }

    private void OnWinEvent(IntPtr hook, uint eventId, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject != OBJID_WINDOW) return;
        if (hwnd != Layer.WorkerW && hwnd != Layer.Progman) return;
        Log.Warn($"Wallpaper layer window 0x{hwnd:X} destroyed — scheduling re-attach");
        NotifyLayerLost();
    }

    /// <summary>Explorer restarted (TaskbarCreated) or layer destroyed: re-probe and tell
    /// the engine to re-attach everything.</summary>
    public void NotifyLayerLost()
    {
        if (_winEventHook != IntPtr.Zero)
        {
            User32.UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }
        _attached.Clear();
        LayerLost?.Invoke();
    }

    public void ValidateLayer()
    {
        if (Layer.WorkerW != IntPtr.Zero && !User32.IsWindow(Layer.WorkerW))
        {
            Log.Warn("WorkerW handle went stale (session unlock?) — re-probing");
            NotifyLayerLost();
        }
    }

    /// <summary>Final cleanup: repaint the desktop so the original static wallpaper shows.
    /// Only called on exit — on 24H2 raised desktops this refresh destroys the live WorkerW.</summary>
    public static void RestoreDesktop()
    {
        User32.SystemParametersInfoW(SPI_SETDESKWALLPAPER, 0, IntPtr.Zero, SPIF_UPDATEINIFILE);
    }

    public void Dispose()
    {
        if (_winEventHook != IntPtr.Zero)
        {
            User32.UnhookWinEvent(_winEventHook);
            _winEventHook = IntPtr.Zero;
        }
    }
}
