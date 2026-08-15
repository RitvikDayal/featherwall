using System.Security.Principal;
using FeatherWall.Common;
using FeatherWall.Config;
using FeatherWall.Desktop;
using FeatherWall.Interop;

namespace FeatherWall;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Contains("--diag"))
            return RunDiagnostics();

        if (args.Contains("--exit"))
        {
            var running = User32.FindWindowW("FeatherWallMessage", null);
            if (running != IntPtr.Zero)
                User32.PostMessageW(running, Win32Constants.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            return 0;
        }

        Log.Init();
        Log.Info($"FeatherWall starting (pid {Environment.ProcessId})");

        using var singleInstance = new Mutex(initiallyOwned: true, "FeatherWall.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            User32.MessageBoxW(IntPtr.Zero, "FeatherWall is already running — look for the feather icon in the tray.",
                "FeatherWall", 0x40);
            return 0;
        }

        using (var identity = WindowsIdentity.GetCurrent())
        {
            if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            {
                Log.Warn("Running elevated — wallpaper attach into explorer may misbehave across integrity levels.");
                User32.MessageBoxW(IntPtr.Zero,
                    "FeatherWall is running as administrator. Attaching to the desktop of the non-elevated shell may fail — " +
                    "please run it as a normal user.", "FeatherWall", 0x30 /* MB_ICONWARNING */);
            }
        }

        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);

        Engine? engine = null;
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            Log.Error("Unhandled exception", e.ExceptionObject as Exception);
            try { DesktopLayerHost.RestoreDesktop(); } catch { }
        };

        try
        {
            engine = new Engine(ConfigStore.Load());
            engine.Start();

            if (args.Contains("--settings"))
                engine.OpenSettings();

            while (User32.GetMessageW(out var msg, IntPtr.Zero, 0, 0) > 0)
            {
                User32.TranslateMessage(ref msg);
                User32.DispatchMessageW(ref msg);
            }
        }
        catch (Exception ex)
        {
            Log.Error("Fatal", ex);
            User32.MessageBoxW(IntPtr.Zero, $"FeatherWall hit a fatal error and will exit.\n\n{ex.Message}", "FeatherWall", 0x10);
            return 1;
        }
        finally
        {
            engine?.Dispose();
            DesktopLayerHost.RestoreDesktop();
            Log.Info("FeatherWall exited cleanly");
        }
        return 0;
    }

    private static int RunDiagnostics()
    {
        Kernel32.AttachConsole(Kernel32.ATTACH_PARENT_PROCESS);
        try
        {
            var layer = DesktopLayerHost.Probe();
            var lines = new List<string>
            {
                "",
                $"FeatherWall --diag ({Environment.OSVersion})",
                $"Topology : {layer.Topology}",
                $"Progman  : 0x{layer.Progman:X}",
                $"WorkerW  : 0x{layer.WorkerW:X}",
                $"DefView  : 0x{layer.DefView:X}",
            };
            lines.AddRange(MonitorTracker.Enumerate().Select(m =>
                $"Monitor  : {m.Device} {m.Bounds}{(m.Primary ? " (primary)" : "")}"));
            Console.WriteLine(string.Join(Environment.NewLine, lines));
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"--diag failed: {ex.Message}");
            return 1;
        }
    }
}
