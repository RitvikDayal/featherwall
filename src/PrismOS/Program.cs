using System.Security.Principal;
using PrismOS.Common;
using PrismOS.Config;
using PrismOS.Desktop;
using PrismOS.Interop;
using Velopack;

namespace PrismOS;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Setup.exe installs through Velopack, which starts the app with --veloapp-* arguments at
        // install and uninstall time; Run() handles those and exits.
        if (UninstallCleanup.IsInstallerHook(args))
            VelopackApp.Build()
                .OnBeforeUninstallFastCallback(_ =>
                {
                    Log.Init();
                    Log.Info($"Uninstalling the copy in {AppContext.BaseDirectory}");
                    UninstallCleanup.Run(AppContext.BaseDirectory);
                })
                .Run();

        if (args.Contains("--diag"))
            return RunDiagnostics();

        if (args.Contains("--set-lockscreen"))
        {
            // Elevated re-launch: write the policy keys for the file we were given and exit.
            // Nothing else to do here — no wallpaper engine, no window.
            Log.Init();
            int idx = Array.IndexOf(args, "--set-lockscreen");
            string? image = idx + 1 < args.Length ? args[idx + 1] : null;
            if (image is null || !File.Exists(image))
            {
                Log.Error("--set-lockscreen: missing or unreadable image argument");
                return 1;
            }
            var result = Desktop.LockScreenWallpaper.SetElevated(image);
            Log.Info($"Elevated lock screen write: {result}");
            return result == Desktop.LockScreenWallpaper.Result.Ok ? 0 : 1;        }

        if (args.Contains("--exit"))
        {
            var running = User32.FindWindowW("PrismOSMessage", null);
            if (running != IntPtr.Zero)
                User32.PostMessageW(running, Win32Constants.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            return 0;
        }

        // Before Log.Init writes this run's log: an earlier run's log is how an upgrade is told apart
        // from a fresh install when no config.json was ever saved. The pre-rename FeatherWall folders
        // are copied in first, so they count as "ran before" too.
        bool migrated = LegacyMigration.Migrate();
        bool ranBefore = migrated || ConfigStore.RanBefore(Log.Directory);
        Autostart.EnsureSilentFlag();

        Log.Init();
        Log.Info($"PrismOS starting (pid {Environment.ProcessId})");
        if (migrated) Log.Info("Migrated settings and data from the pre-rename FeatherWall folders");


        using var singleInstance = new Mutex(initiallyOwned: true, "PrismOS.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            // Launching it again, from the Start menu or with --settings, opens the running copy's
            // settings, as a double-click on its tray icon does.
            // This process has the foreground; hand it over so the settings window comes to the front
            // instead of flashing in the taskbar.
            var running = User32.FindWindowW("PrismOSMessage", null);
            bool posted = false;
            if (running != IntPtr.Zero)
            {
                User32.GetWindowThreadProcessId(running, out uint runningPid);
                User32.AllowSetForegroundWindow(runningPid);
                posted = User32.PostMessageW(running, Tray.TrayIcon.CallbackMessage, IntPtr.Zero, (IntPtr)Win32Constants.WM_LBUTTONDBLCLK);
            }
            // Not found, or refused (an elevated copy will not take messages from this one).
            if (!posted)
                User32.MessageBoxW(IntPtr.Zero, "PrismOS is already running.", "PrismOS", 0x40);
            return 0;
        }

        using (var identity = WindowsIdentity.GetCurrent())
        {
            if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
            {
                Log.Warn("Running elevated — wallpaper attach into explorer may misbehave across integrity levels.");
                User32.MessageBoxW(IntPtr.Zero,
                    "PrismOS is running as administrator. Attaching to the desktop of the non-elevated shell may fail — " +
                    "please run it as a normal user.", "PrismOS", 0x30 /* MB_ICONWARNING */);
            }
        }

        var config = ConfigStore.LoadAtStartup(ConfigStore.ConfigPath, ranBefore, out bool configUnreadable);
        if (config.IsFreshInstall) Log.Info("Fresh install: starting config saved");

        // --no-shortcut keeps a dev build from repointing the Start menu entry at bin\.
        if (!args.Contains("--no-shortcut") && Environment.ProcessPath is { } exe && StartMenuShortcut.IsOwnExe(exe))
        {
            try
            {
                if (configUnreadable)
                    Log.Info("Config unreadable: leaving the Start menu entry and config.json as they are");
                var (change, markAdded) = StartMenuShortcut.Reconcile(exe, config, configUnreadable);
                if (change != ShortcutChange.None)
                    Log.Info($"Start menu shortcut {change.ToString().ToLowerInvariant()}: {exe}");
                if (markAdded) ConfigStore.Save(config);
            }
            catch (Exception ex)
            {
                Log.Warn($"Start menu shortcut step failed: {ex.Message}");
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
            engine = new Engine(config, configUnreadable);
            engine.Start();

            // Opening PrismOS like an app opens the window; --silent is the login start, which
            // attaches the wallpaper in the background and stays out of the way.
            if (!args.Contains("--silent"))
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
            User32.MessageBoxW(IntPtr.Zero, $"PrismOS hit a fatal error and will exit.\n\n{ex.Message}", "PrismOS", 0x10);
            return 1;
        }
        finally
        {
            engine?.Dispose();
            DesktopLayerHost.RestoreDesktop();
            Log.Info("PrismOS exited cleanly");
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
                $"PrismOS --diag ({Environment.OSVersion})",
                $"Runtime  : .NET {Environment.Version}",
                $"Topology : {layer.Topology}",
                $"Progman  : 0x{layer.Progman:X}",
                $"WorkerW  : 0x{layer.WorkerW:X}",
                $"DefView  : 0x{layer.DefView:X}",
            };
            // DPI is printed per monitor because issues #3 and #4 are "verify on hardware this
            // machine does not have" — a contributor with a second display can paste this.
            var monitors = MonitorTracker.Enumerate();
            lines.Add($"PrimaryDpi: {MonitorTracker.PrimaryDpi(monitors)}");
            lines.AddRange(monitors.Select(m =>
                $"Monitor  : {m.Device} {m.Bounds}{(m.Primary ? " (primary)" : "")} " +
                $"dpi={m.Dpi} ({m.Dpi * 100 / Shcore.DefaultDpi}%) widgetScale={MonitorTracker.DpiScale(m, monitors):0.####}"));
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
