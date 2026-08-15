using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using FeatherWall.Interop;
using static FeatherWall.Interop.Win32Constants;

namespace FeatherWall.Common;

/// <summary>Base for our raw Win32 windows: registers one window class per subclass name
/// and routes messages to the owning instance.</summary>
public abstract class Win32Window : IDisposable
{
    private static readonly ConcurrentDictionary<IntPtr, Win32Window> Instances = new();
    private static readonly HashSet<string> RegisteredClasses = [];
    private static readonly User32.WndProc StaticWndProcDelegate = StaticWndProc; // rooted for the process lifetime
    private static readonly object RegisterSync = new();

    public IntPtr Hwnd { get; private set; }

    protected void CreateWindow(string className, uint style, uint exStyle, int x, int y, int w, int h, IntPtr parent)
    {
        EnsureClassRegistered(className);
        Hwnd = User32.CreateWindowExW(exStyle, className, null, style, x, y, w, h, parent, IntPtr.Zero,
            Kernel32.GetModuleHandleW(null), IntPtr.Zero);
        if (Hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"CreateWindowExW({className}) failed: {Marshal.GetLastWin32Error()}");
        Instances[Hwnd] = this;
    }

    private static void EnsureClassRegistered(string className)
    {
        lock (RegisterSync)
        {
            if (!RegisteredClasses.Add(className)) return;
            var wc = new WNDCLASSEX
            {
                Size = (uint)Marshal.SizeOf<WNDCLASSEX>(),
                WndProc = Marshal.GetFunctionPointerForDelegate(StaticWndProcDelegate),
                Instance = Kernel32.GetModuleHandleW(null),
                Cursor = User32.LoadCursorW(IntPtr.Zero, new IntPtr(32512) /* IDC_ARROW */),
                ClassName = className,
            };
            if (User32.RegisterClassExW(ref wc) == 0)
                throw new InvalidOperationException($"RegisterClassExW({className}) failed: {Marshal.GetLastWin32Error()}");
        }
    }

    private static IntPtr StaticWndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (Instances.TryGetValue(hwnd, out var window))
        {
            var result = window.HandleMessage(msg, wParam, lParam);
            if (msg == WM_DESTROY)
                Instances.TryRemove(hwnd, out _);
            return result;
        }
        return User32.DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    protected virtual IntPtr HandleMessage(uint msg, IntPtr wParam, IntPtr lParam) =>
        User32.DefWindowProcW(Hwnd, msg, wParam, lParam);

    public virtual void Dispose()
    {
        if (Hwnd != IntPtr.Zero)
        {
            User32.DestroyWindow(Hwnd);
            Instances.TryRemove(Hwnd, out _);
            Hwnd = IntPtr.Zero;
        }
        GC.SuppressFinalize(this);
    }
}
