using System.Collections.Concurrent;
using System.Text;
using FeatherWall.Common;
using FeatherWall.Config;
using FeatherWall.Desktop;
using FeatherWall.Gallery;
using FeatherWall.Interop;
using FeatherWall.Playback;
using FeatherWall.Rendering;
using FeatherWall.Tray;
using FeatherWall.Widgets;
using static FeatherWall.Interop.Win32Constants;

namespace FeatherWall;

/// <summary>Wires everything together: desktop layer, per-monitor wallpaper windows,
/// clock widget, pause monitor, tray UI, config persistence.</summary>
public sealed class Engine : IDisposable
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff" };

    private readonly AppConfig _config;
    private readonly DesktopLayerHost _host = new();
    private readonly GalleryService _gallery = new();
    private readonly Dictionary<string, WallpaperWindow> _windows = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<Action> _mainThreadActions = new();
    private readonly DeviceLossGuard _deviceLoss = new();
    private readonly uint _taskbarCreatedMessage = User32.RegisterWindowMessageW("TaskbarCreated");

    private MessageWindow? _messageWindow;
    private PowerNotifications? _power;
    private TrayIcon? _tray;
    private ClockOverlay? _clock;
    private PlaybackMonitor? _playback;
    private bool _reapplying;
    private string _originalWallpaper = "";

    private static string StaticDir => Path.Combine(Log.Directory, "static");
    private static string OriginalWallpaperFile => Path.Combine(Log.Directory, "original-wallpaper.txt");
    private static string OriginalDesktopWallpapersFile => Path.Combine(Log.Directory, "original-vd-wallpapers.tsv");
    private static string StaticPath(string device) =>
        Path.Combine(StaticDir, new string(device.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()) + ".png");

    public Engine(AppConfig config) => _config = config;

    public void Start()
    {
        _messageWindow = new MessageWindow(this);
        WtsApi32.WTSRegisterSessionNotification(_messageWindow.Hwnd, WtsApi32.NOTIFY_FOR_THIS_SESSION);
        _power = new PowerNotifications(_messageWindow.Hwnd);

        SaveOriginalWallpaper();

        // An explorer restart or display change rebuilds the layer for reasons unrelated to the
        // GPU, and produces a fresh device anyway — so it clears any device-loss failure history.
        _host.LayerLost += () => RunOnMainThread(() => { _deviceLoss.Reset(); ReapplyAll(); });
        _host.EnsureLayer();

        _tray = new TrayIcon(_messageWindow.Hwnd);

        _playback = new PlaybackMonitor(() => _config.Pause);
        _playback.PauseStateChanged += OnPauseStateChanged;

        ApplyFromConfig();

        if (_windows.Count == 0)
            _tray.ShowBalloon("FeatherWall is running", "Right-click the tray icon to choose a wallpaper.");
    }

    // ---- wallpaper application ----------------------------------------------------------

    private void ApplyFromConfig()
    {
        foreach (var monitor in MonitorTracker.Enumerate())
        {
            var path = _config.WallpaperFor(monitor.Device);
            if (path is null) continue;
            if (!File.Exists(path))
            {
                Log.Warn($"Configured wallpaper missing: {path}");
                continue;
            }
            try
            {
                ApplyToMonitor(monitor, path);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to apply wallpaper on {monitor.Device}", ex);
            }
        }
        RecreateClock();
    }

    private void ApplyToMonitor(MonitorInfo monitor, string path)
    {
        // Reuse the existing window + composition host when we have one: no re-attach,
        // no flash of the shell wallpaper, and the clock overlay never blinks.
        if (!_windows.TryGetValue(monitor.Device, out var window))
        {
            window = new WallpaperWindow(monitor);
            window.DeviceLost += OnDeviceLost;
            _host.Attach(window.Hwnd, monitor.Bounds);
            window.EnsureHost();
            _windows[monitor.Device] = window;
        }
        var host = window.EnsureHost();

        // When the first frame is captured, set it as the OS static wallpaper so a
        // virtual-desktop switch (or Task View) paints a matching frame instead of the
        // previous wallpaper — no flash before our live layer returns.
        var staticPath = StaticPath(monitor.Device);
        var captured = monitor;
        Action onStatic = () => RunOnMainThread(() =>
        {
            try { DesktopWallpaper.SetForMonitor(captured.Device, captured.Bounds, staticPath); }
            catch (Exception ex) { Log.Error("Set static fallback failed", ex); }

            // Point every virtual desktop at this frame so switching desktops paints a
            // matching wallpaper during the slide animation instead of each desktop's own.
            if (captured.Primary)
            {
                try { VirtualDesktopWallpaper.SetAll(staticPath); }
                catch (Exception ex) { Log.Error("Set per-desktop fallback failed", ex); }
            }
        });

        IWallpaperRenderer renderer = ImageExtensions.Contains(Path.GetExtension(path))
            ? new ImageRenderer(host, monitor.Bounds.Width, monitor.Bounds.Height, _config.Fit, staticPath, onStatic)
            : new VideoRenderer(host, monitor.Bounds.Width, monitor.Bounds.Height, _config.Fit,
                _config.MuteVideo, _config.Volume, staticPath, onStatic);
        window.SetRenderer(renderer);
        renderer.Load(path);

        _playback?.Invalidate(); // re-evaluate pause state for the fresh renderer
        Log.Info($"Wallpaper applied on {monitor.Device}: {path}");
    }

    public void ApplyUserSelection(string path, string monitorDevice = "*")
    {
        bool anyApplied = false;
        foreach (var monitor in MonitorTracker.Enumerate())
        {
            if (monitorDevice != "*" && !string.Equals(monitor.Device, monitorDevice, StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                ApplyToMonitor(monitor, path);
                anyApplied = true;
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to apply {path} on {monitor.Device}", ex);
                _tray?.ShowBalloon("Couldn't set wallpaper", $"{Path.GetFileName(path)}: {ex.Message}");
            }
        }
        if (anyApplied)
        {
            _config.Assign(monitorDevice, path);
            ConfigStore.Save(_config);
            if (_clock is null) RecreateClock();
        }
    }

    /// <summary>Full rebuild: display change, explorer restart, or layer destruction.</summary>
    /// <summary>A pushed power/display notification. Windows already knows the panel went dark
    /// or the laptop came off AC; this is the app being told instead of polling to find out.
    /// The pause poll still runs for the foreground-window cases it cannot be told about — this
    /// only removes work, it does not add a second source of truth.</summary>
    private void OnPowerSettingChanged(IntPtr lParam)
    {
        if (!PowerNotifications.TryRead(lParam, out var setting, out byte value)) return;

        if (setting == PowerNotifications.ConsoleDisplayState)
        {
            bool off = value == PowerNotifications.DisplayOff;
            Log.Info($"Display state: {value switch
            {
                PowerNotifications.DisplayOff => "off",
                PowerNotifications.DisplayOn => "on",
                PowerNotifications.DisplayDimmed => "dimmed",
                _ => $"unknown ({value})",
            }}");

            if (_playback is { } playback) playback.DisplayOff = off;
            // Dimmed still shows the wallpaper, so only a true off suspends the clock.
            _clock?.SetSuspended(off);
        }
        else if (setting == PowerNotifications.PowerSavingStatus || setting == PowerNotifications.AcDcPowerSource)
        {
            // The pause poll reads battery saver directly; nudging it just makes the transition
            // land immediately instead of up to 500 ms later.
            _playback?.Invalidate();
        }
    }

    /// <summary>A surface reported DXGI device removal or reset — a GPU driver update, a TDR, or
    /// an adapter change. The device belongs to the composition host, so nothing recovers in
    /// place: the whole layer is rebuilt, which is what ReapplyAll already does for a lost
    /// wallpaper layer.
    ///
    /// Until 2026-08-20 this event was raised by CompositionSurface.Present and nothing
    /// subscribed to it, so a driver update logged a warning and left a dead device presenting
    /// nothing until the user re-applied by hand.</summary>
    private void OnDeviceLost()
    {
        if (!_deviceLoss.TryBegin())
        {
            if (_deviceLoss.GaveUp)
                Log.Error($"GPU device lost {DeviceLossGuard.MaxConsecutiveAttempts} times in a row — " +
                          "giving up on automatic recovery. Re-apply the wallpaper from the tray once the display driver is stable.");
            return;
        }

        RunOnMainThread(() =>
        {
            try
            {
                Log.Warn("GPU device lost — rebuilding the composition tree");
                ReapplyAll();
            }
            finally
            {
                _deviceLoss.Complete();
            }
        });
    }

    private void ReapplyAll()
    {
        if (_reapplying) return;
        _reapplying = true;
        try
        {
            Log.Info("Re-applying all wallpapers");
            _clock?.Dispose(); // before the windows — its timer renders into their hosts
            _clock = null;
            foreach (var window in _windows.Values) window.Dispose();
            _windows.Clear();
            VideoRenderer.ReclaimMediaPipeline(); // disposed players hold their MF threads until collected
            _host.EnsureLayer();
            ApplyFromConfig();
        }
        catch (Exception ex)
        {
            Log.Error("Re-apply failed", ex);
        }
        finally
        {
            _reapplying = false;
        }
    }

    private void RecreateClock()
    {
        _clock?.Dispose();
        _clock = null;
        if (!_config.Clock.Enabled) return;

        var monitors = MonitorTracker.Enumerate();
        var target = monitors.FirstOrDefault(m =>
                string.Equals(m.Device, _config.Clock.Monitor, StringComparison.OrdinalIgnoreCase))
            ?? monitors.FirstOrDefault(m => m.Primary) ?? monitors.FirstOrDefault();
        if (target is null) return;

        try
        {
            // The clock rides the monitor's wallpaper composition tree; without a
            // wallpaper it gets a bare surface window of its own.
            if (!_windows.TryGetValue(target.Device, out var window))
            {
                window = new WallpaperWindow(target);
                window.DeviceLost += OnDeviceLost;
                _host.Attach(window.Hwnd, target.Bounds);
                window.EnsureHost();
                _windows[target.Device] = window;
            }
            _clock = new ClockOverlay(window.EnsureHost(), _config.Clock, target,
                MonitorTracker.DpiScale(target, monitors));
        }
        catch (Exception ex)
        {
            Log.Error("Clock widget failed", ex);
        }
    }

    // ---- pause/resume ---------------------------------------------------------------------

    private void OnPauseStateChanged(string monitorDevice, PauseReason reason)
    {
        // Fires on the monitor's timer thread; _windows belongs to the main thread.
        RunOnMainThread(() =>
        {
            if (!_windows.TryGetValue(monitorDevice, out var window) || window.Renderer is null) return;
            if (reason == PauseReason.None)
            {
                window.Renderer.Resume();
                Log.Info($"Resumed {monitorDevice}");
            }
            else
            {
                window.Renderer.Pause();
                Log.Info($"Paused {monitorDevice}: {reason}");
            }
        });
    }

    // ---- main-thread marshaling -------------------------------------------------------------

    public void RunOnMainThread(Action action)
    {
        _mainThreadActions.Enqueue(action);
        if (_messageWindow is not null)
            User32.PostMessageW(_messageWindow.Hwnd, MessageWindow.RunActionsMessage, IntPtr.Zero, IntPtr.Zero);
    }

    private void DrainMainThreadActions()
    {
        while (_mainThreadActions.TryDequeue(out var action))
        {
            try { action(); }
            catch (Exception ex) { Log.Error("Posted action failed", ex); }
        }
    }

    // ---- tray menu ---------------------------------------------------------------------------

    private const uint CmdExit = 1;
    private const uint CmdPick = 2;
    private const uint CmdClockEnabled = 3;
    private const uint CmdClock24h = 4;
    private const uint CmdClockSeconds = 5;
    private const uint CmdClockDate = 6;
    private const uint CmdMute = 7;
    private const uint CmdAutostart = 8;
    private const uint CmdOpenConfig = 9;
    private const uint CmdDiagnostics = 10;
    private const uint CmdPauseFullscreen = 11;
    private const uint CmdPauseBattery = 12;
    private const uint CmdSettings = 13;
    private const uint CmdFitBase = 20;      // + (int)FitMode
    private const uint CmdAnchorBase = 30;   // + (int)ClockAnchor
    private const uint CmdGalleryBase = 2000;

    private void ShowTrayMenu()
    {
        if (_messageWindow is null) return;

        IntPtr menu = User32.CreatePopupMenu();
        IntPtr fitMenu = User32.CreatePopupMenu();
        IntPtr clockMenu = User32.CreatePopupMenu();
        IntPtr anchorMenu = User32.CreatePopupMenu();
        IntPtr galleryMenu = User32.CreatePopupMenu();
        IntPtr pauseMenu = User32.CreatePopupMenu();
        try
        {
            User32.AppendMenuW(menu, MF_STRING, CmdPick, "Set wallpaper…");

            foreach (var (entry, index) in _gallery.Manifest.Entries.Select((e, i) => (e, i)))
            {
                bool cached = _gallery.LocalPathIfDownloaded(entry) is not null;
                string label = cached
                    ? $"{entry.Title}"
                    : $"{entry.Title}  ({entry.SizeLabel} download)";
                User32.AppendMenuW(galleryMenu, MF_STRING, CmdGalleryBase + (uint)index, label);
            }
            User32.AppendMenuW(menu, MF_POPUP, (nuint)galleryMenu, "Gallery (public-domain)");

            foreach (var mode in Enum.GetValues<FitMode>())
                User32.AppendMenuW(fitMenu, MF_STRING | (_config.Fit == mode ? MF_CHECKED : 0),
                    CmdFitBase + (uint)mode, mode.ToString());
            User32.AppendMenuW(menu, MF_POPUP, (nuint)fitMenu, "Fit mode");

            User32.AppendMenuW(menu, MF_STRING | (_config.MuteVideo ? MF_CHECKED : 0), CmdMute, "Mute video");
            User32.AppendMenuW(menu, MF_SEPARATOR, 0, null);

            User32.AppendMenuW(clockMenu, MF_STRING | (_config.Clock.Enabled ? MF_CHECKED : 0), CmdClockEnabled, "Show clock");
            User32.AppendMenuW(clockMenu, MF_STRING | (_config.Clock.TwentyFourHour ? MF_CHECKED : 0), CmdClock24h, "24-hour");
            User32.AppendMenuW(clockMenu, MF_STRING | (_config.Clock.ShowSeconds ? MF_CHECKED : 0), CmdClockSeconds, "Show seconds");
            User32.AppendMenuW(clockMenu, MF_STRING | (_config.Clock.ShowDate ? MF_CHECKED : 0), CmdClockDate, "Show date");
            foreach (var anchor in Enum.GetValues<ClockAnchor>())
                User32.AppendMenuW(anchorMenu, MF_STRING | (_config.Clock.Anchor == anchor ? MF_CHECKED : 0),
                    CmdAnchorBase + (uint)anchor, PrettyAnchor(anchor));
            User32.AppendMenuW(clockMenu, MF_POPUP, (nuint)anchorMenu, "Position");
            User32.AppendMenuW(menu, MF_POPUP, (nuint)clockMenu, "Clock");

            User32.AppendMenuW(pauseMenu, MF_STRING | (_config.Pause.OnFullscreen ? MF_CHECKED : 0), CmdPauseFullscreen, "Pause when a fullscreen app is active");
            User32.AppendMenuW(pauseMenu, MF_STRING | (_config.Pause.OnBatterySaver ? MF_CHECKED : 0), CmdPauseBattery, "Pause on battery saver");
            User32.AppendMenuW(menu, MF_POPUP, (nuint)pauseMenu, "Auto-pause");

            User32.AppendMenuW(menu, MF_SEPARATOR, 0, null);
            User32.AppendMenuW(menu, MF_STRING, CmdSettings, "Settings…");
            User32.AppendMenuW(menu, MF_STRING | (Autostart.IsEnabled() ? MF_CHECKED : 0), CmdAutostart, "Run at startup");
            User32.AppendMenuW(menu, MF_STRING, CmdOpenConfig, "Open config file");
            User32.AppendMenuW(menu, MF_STRING, CmdDiagnostics, "Diagnostics");
            User32.AppendMenuW(menu, MF_SEPARATOR, 0, null);
            User32.AppendMenuW(menu, MF_STRING, CmdExit, "Exit FeatherWall");

            User32.GetCursorPos(out var pt);
            User32.SetForegroundWindow(_messageWindow.Hwnd);
            int cmd = User32.TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, _messageWindow.Hwnd, IntPtr.Zero);
            if (cmd > 0) HandleCommand((uint)cmd);
        }
        finally
        {
            User32.DestroyMenu(menu); // destroys submenus too
        }
    }

    private static string PrettyAnchor(ClockAnchor anchor) => anchor switch
    {
        ClockAnchor.TopLeft => "Top left",
        ClockAnchor.TopCenter => "Top center",
        ClockAnchor.TopRight => "Top right",
        ClockAnchor.CenterLeft => "Center left",
        ClockAnchor.Center => "Center",
        ClockAnchor.CenterRight => "Center right",
        ClockAnchor.BottomLeft => "Bottom left",
        ClockAnchor.BottomCenter => "Bottom center",
        _ => "Bottom right",
    };

    private void HandleCommand(uint cmd)
    {
        switch (cmd)
        {
            case CmdExit:
                User32.PostQuitMessage(0);
                return;
            case CmdPick:
                if (FilePicker.PickMedia(_messageWindow!.Hwnd) is { } path)
                    ApplyUserSelection(path);
                return;
            case CmdClockEnabled:
                _config.Clock.Enabled = !_config.Clock.Enabled;
                SaveAndRefreshClock();
                return;
            case CmdClock24h:
                _config.Clock.TwentyFourHour = !_config.Clock.TwentyFourHour;
                SaveAndRefreshClock();
                return;
            case CmdClockSeconds:
                _config.Clock.ShowSeconds = !_config.Clock.ShowSeconds;
                SaveAndRefreshClock();
                return;
            case CmdClockDate:
                _config.Clock.ShowDate = !_config.Clock.ShowDate;
                SaveAndRefreshClock();
                return;
            case CmdMute:
                _config.MuteVideo = !_config.MuteVideo;
                ApplyAudioSettings();
                return;
            case CmdSettings:
                OpenSettings();
                return;
            case CmdAutostart:
                Autostart.SetEnabled(!Autostart.IsEnabled());
                return;
            case CmdOpenConfig:
                ConfigStore.Save(_config);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ConfigStore.ConfigPath) { UseShellExecute = true });
                return;
            case CmdDiagnostics:
                User32.MessageBoxW(IntPtr.Zero, Diagnostics(), "FeatherWall diagnostics", 0x40 /* MB_ICONINFORMATION */);
                return;
            case CmdPauseFullscreen:
                _config.Pause.OnFullscreen = !_config.Pause.OnFullscreen;
                ConfigStore.Save(_config);
                return;
            case CmdPauseBattery:
                _config.Pause.OnBatterySaver = !_config.Pause.OnBatterySaver;
                ConfigStore.Save(_config);
                return;
        }

        if (cmd >= CmdFitBase && cmd < CmdFitBase + 10)
        {
            _config.Fit = (FitMode)(cmd - CmdFitBase);
            ConfigStore.Save(_config);
            ReapplyAll();
            return;
        }
        if (cmd >= CmdAnchorBase && cmd < CmdAnchorBase + 10)
        {
            _config.Clock.Anchor = (ClockAnchor)(cmd - CmdAnchorBase);
            SaveAndRefreshClock();
            return;
        }
        if (cmd >= CmdGalleryBase && cmd < CmdGalleryBase + (uint)_gallery.Manifest.Entries.Count)
        {
            ApplyGalleryEntry(_gallery.Manifest.Entries[(int)(cmd - CmdGalleryBase)]);
        }
    }

    private void SaveOriginalWallpaper()
    {
        try
        {
            Directory.CreateDirectory(Log.Directory);
            if (File.Exists(OriginalWallpaperFile))
            {
                // A prior run left this behind (crash / kill) — it holds the TRUE original.
                _originalWallpaper = File.ReadAllText(OriginalWallpaperFile).Trim();
            }
            else
            {
                _originalWallpaper = DesktopWallpaper.GetCurrent();
                File.WriteAllText(OriginalWallpaperFile, _originalWallpaper);
                Log.Info($"Saved original wallpaper: {(_originalWallpaper.Length == 0 ? "(none)" : _originalWallpaper)}");
            }

            // Per-desktop wallpapers (Windows 11) — save once so we can restore them.
            if (!File.Exists(OriginalDesktopWallpapersFile))
            {
                var perDesktop = VirtualDesktopWallpaper.ReadAll();
                File.WriteAllLines(OriginalDesktopWallpapersFile,
                    perDesktop.Select(kv => $"{kv.Key}\t{kv.Value}"));
                Log.Info($"Saved {perDesktop.Count} per-desktop wallpaper(s)");
            }
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not save original wallpaper: {ex.Message}");
        }
    }

    private void RestoreOriginalWallpaper()
    {
        try
        {
            if (File.Exists(OriginalDesktopWallpapersFile))
            {
                var saved = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(OriginalDesktopWallpapersFile))
                {
                    int tab = line.IndexOf('\t');
                    if (tab > 0) saved[line[..tab]] = line[(tab + 1)..];
                }
                VirtualDesktopWallpaper.Restore(saved);
                File.Delete(OriginalDesktopWallpapersFile);
            }

            if (!string.IsNullOrEmpty(_originalWallpaper))
                DesktopWallpaper.RestoreCurrent(_originalWallpaper);
            if (File.Exists(OriginalWallpaperFile))
                File.Delete(OriginalWallpaperFile);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not restore original wallpaper: {ex.Message}");
        }
    }

    private void SaveAndRefreshClock()
    {
        ConfigStore.Save(_config);
        RecreateClock();
    }

    // ---- settings panel hooks ------------------------------------------------------------

    public AppConfig Config => _config;

    /// <summary>Credit for the wallpaper currently in use, or null when it is the user's own file.
    /// Surfaced in the settings panel so gallery media is actually attributed on screen.</summary>
    public GalleryEntry? CurrentWallpaperCredit =>
        _gallery.EntryForPath(_config.WallpaperFor(
            MonitorTracker.Enumerate().FirstOrDefault(m => m.Primary)?.Device ?? "*"));

    public void SaveConfig() => ConfigStore.Save(_config);

    /// <summary>Clock appearance changed: persist and rebuild the overlay (cheap).</summary>
    public void RefreshClock() => SaveAndRefreshClock();

    /// <summary>Fit mode (or similar) changed: persist and swap renderers in place —
    /// windows and hosts are reused, so there is no flicker.</summary>
    public void RefreshWallpapers()
    {
        ConfigStore.Save(_config);
        foreach (var monitor in MonitorTracker.Enumerate())
        {
            var path = _config.WallpaperFor(monitor.Device);
            if (path is null || !File.Exists(path)) continue;
            try
            {
                ApplyToMonitor(monitor, path);
            }
            catch (Exception ex)
            {
                Log.Error($"Refresh failed on {monitor.Device}", ex);
            }
        }
    }

    public void ApplyAudioSettings()
    {
        ConfigStore.Save(_config);
        foreach (var window in _windows.Values)
            if (window.Renderer is VideoRenderer video)
            {
                video.IsMuted = _config.MuteVideo;
                video.Volume = _config.Volume;
            }
    }

    private Tray.SettingsForm? _settingsForm;

    public void OpenSettings()
    {
        try
        {
            if (_settingsForm is { IsDisposed: false })
            {
                _settingsForm.Activate();
                return;
            }
            Log.Info("Opening settings panel");
            using var form = new Tray.SettingsForm(this);
            _settingsForm = form;
            try
            {
                form.ShowDialog();
            }
            finally
            {
                _settingsForm = null;
            }
        }
        catch (Exception ex)
        {
            Log.Error("Settings panel failed", ex);
            _tray?.ShowBalloon("Settings failed", ex.Message);
        }
    }

    private void ApplyGalleryEntry(GalleryEntry entry)
    {
        if (_gallery.LocalPathIfDownloaded(entry) is { } cached)
        {
            ApplyUserSelection(cached);
            return;
        }

        _tray?.ShowBalloon("Downloading wallpaper", $"{entry.Title} ({entry.SizeLabel}) — it will apply automatically.");
        _ = Task.Run(async () =>
        {
            try
            {
                var file = await _gallery.EnsureDownloadedAsync(entry);
                RunOnMainThread(() => ApplyUserSelection(file));
            }
            catch (Exception ex)
            {
                Log.Error($"Gallery download failed: {entry.Id}", ex);
                _tray?.ShowBalloon("Download failed", ex.Message);
            }
        });
    }

    // ---- diagnostics --------------------------------------------------------------------------

    public string Diagnostics()
    {
        var sb = new StringBuilder();
        var layer = _host.Layer;
        sb.AppendLine($"FeatherWall {typeof(Engine).Assembly.GetName().Version}");
        sb.AppendLine($"Topology: {layer.Topology}");
        sb.AppendLine($"Progman: 0x{layer.Progman:X}  WorkerW: 0x{layer.WorkerW:X}  DefView: 0x{layer.DefView:X}");
        foreach (var monitor in MonitorTracker.Enumerate())
            sb.AppendLine($"Monitor {monitor.Device}: {monitor.Bounds}{(monitor.Primary ? " (primary)" : "")}");
        sb.AppendLine($"Attached surfaces: {_windows.Count}   Clock: {(_clock is null ? "off" : "on")}");
        sb.AppendLine($"Config: {ConfigStore.ConfigPath}");
        sb.AppendLine($"Log: {Path.Combine(Log.Directory, "featherwall.log")}");
        return sb.ToString();
    }

    // ---- message window -----------------------------------------------------------------------

    private sealed class MessageWindow : Win32Window
    {
        public const uint RunActionsMessage = WM_APP + 2;
        private readonly Engine _engine;

        public MessageWindow(Engine engine)
        {
            _engine = engine;
            CreateWindow("FeatherWallMessage", 0, 0, 0, 0, 0, 0, IntPtr.Zero);
        }

        protected override IntPtr HandleMessage(uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == TrayIcon.CallbackMessage)
            {
                uint mouse = (uint)(lParam.ToInt64() & 0xFFFF);
                if (mouse is WM_RBUTTONUP or WM_CONTEXTMENU)
                    _engine.ShowTrayMenu();
                else if (mouse == WM_LBUTTONDBLCLK)
                    _engine.OpenSettings();
                return IntPtr.Zero;
            }
            if (msg == RunActionsMessage)
            {
                _engine.DrainMainThreadActions();
                return IntPtr.Zero;
            }
            if (msg == _engine._taskbarCreatedMessage)
            {
                Log.Warn("Explorer restarted (TaskbarCreated) — refreshing tray + re-attaching");
                _engine._tray?.Refresh();
                _engine.RunOnMainThread(_engine.ReapplyAll);
                return IntPtr.Zero;
            }
            switch (msg)
            {
                case WM_CLOSE: // polite shutdown (taskkill without /F, system shutdown)
                    User32.PostQuitMessage(0);
                    return IntPtr.Zero;
                case WM_DISPLAYCHANGE:
                    Log.Info("Display change — re-applying");
                    _engine.RunOnMainThread(_engine.ReapplyAll);
                    return IntPtr.Zero;
                case WM_WTSSESSION_CHANGE:
                    if (_engine._playback is { } playback)
                    {
                        if ((int)wParam == WTS_SESSION_LOCK) playback.SessionLocked = true;
                        else if ((int)wParam == WTS_SESSION_UNLOCK)
                        {
                            playback.SessionLocked = false;
                            _engine._host.ValidateLayer();
                        }
                    }
                    return IntPtr.Zero;
                case WM_POWERBROADCAST when (int)wParam == PBT_APMRESUMEAUTOMATIC:
                    Log.Info("Resumed from sleep — refreshing clock and validating layer");
                    _engine._clock?.Refresh();
                    _engine._host.ValidateLayer();
                    return IntPtr.Zero;
                case WM_POWERBROADCAST when (int)wParam == PBT_POWERSETTINGCHANGE:
                    _engine.OnPowerSettingChanged(lParam);
                    return IntPtr.Zero;
            }
            return base.HandleMessage(msg, wParam, lParam);
        }
    }

    public void Dispose()
    {
        RestoreOriginalWallpaper();
        _playback?.Dispose();
        _clock?.Dispose(); // before the windows — its timer renders into their hosts
        _clock = null;
        foreach (var window in _windows.Values) window.Dispose();
        _windows.Clear();
        _tray?.Dispose();
        _power?.Dispose(); // before the window — the handles are registered against its hwnd
        _power = null;
        if (_messageWindow is not null)
        {
            WtsApi32.WTSUnRegisterSessionNotification(_messageWindow.Hwnd);
            _messageWindow.Dispose();
        }
        _host.Dispose();
    }
}

public static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "FeatherWall";

    public static bool IsEnabled()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is not null;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is { } exe)
            key.SetValue(ValueName, $"\"{exe}\"");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
