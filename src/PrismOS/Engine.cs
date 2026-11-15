using System.Collections.Concurrent;
using System.Text;
using PrismOS.Common;
using PrismOS.Config;
using PrismOS.Desktop;
using PrismOS.Gallery;
using PrismOS.Interop;
using PrismOS.Playback;
using PrismOS.Rendering;
using PrismOS.Tray;
using PrismOS.Widgets;
using PrismOS.Widgets.Themes;
using static PrismOS.Interop.Win32Constants;

namespace PrismOS;

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

    /// <summary>Each monitor's wallpaper accent, sampled from the static frame once per wallpaper
    /// and only when a theme uses it.</summary>
    private readonly AccentCache _accents = new(png =>
    {
        using var stream = File.OpenRead(png);
        using var frame = new System.Drawing.Bitmap(stream);
        return Accent.Sample(frame);
    });

    /// <summary>The style each monitor's widgets were last built with. Main thread only.</summary>
    private readonly Dictionary<string, WidgetStyle> _stylesInUse = new(StringComparer.OrdinalIgnoreCase);
    private readonly uint _taskbarCreatedMessage = User32.RegisterWindowMessageW("TaskbarCreated");

    private MessageWindow? _messageWindow;
    private PowerNotifications? _power;
    private TrayIcon? _tray;
    private ClockOverlay? _clock;
    private InfoOverlay? _info;
    private HaloOverlay? _halo;
    private NowPlayingOverlay? _record;

    /// <summary>A group theme: one overlay for the whole set, in place of the four
    /// above. Exactly one of the two arrangements exists at a time.</summary>
    private GroupOverlay? _group;

    /// <summary>Which monitor the record lives on. Pause events arrive per monitor, and without
    /// this a second display going behind a window would stop a record on the first.</summary>
    private string? _recordDevice;
    private BatterySource? _battery;
    private NowPlayingSource? _nowPlaying;
    private PlaybackMonitor? _playback;
    private bool _reapplying;
    private string _originalWallpaper = "";

    private static string StaticDir => Path.Combine(Log.Directory, "static");
    private static string OriginalWallpaperFile => Path.Combine(Log.Directory, "original-wallpaper.txt");
    private static string OriginalDesktopWallpapersFile => Path.Combine(Log.Directory, "original-vd-wallpapers.tsv");
    private static string StaticPath(string device) =>
        Path.Combine(StaticDir, new string(device.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()) + ".png");

    /// <summary>True when config.json existed but could not be read: the defaults in use are not
    /// the user's settings, and nothing may save over the file on start-up's account.</summary>
    private readonly bool _configUnreadable;

    public Engine(AppConfig config, bool configUnreadable = false)
    {
        _config = config;
        _configUnreadable = configUnreadable;
    }

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

        // A second balloon replaces the first, so the notice wins when both would show.
        if (UpgradeNotice.ShouldShow(_config, _configUnreadable))
        {
            _config.Widgets.ThemesNoticeShown = true;
            ConfigStore.Save(_config);
            Log.Info("Showing the new widget styles notice");
            _tray.ShowBalloon(UpgradeNotice.Title, UpgradeNotice.Text, OpenSettings);
        }
        else if (_windows.Count == 0)
        {
            _tray.ShowBalloon("PrismOS is running", "Right-click the tray icon to choose a wallpaper.");
        }
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
        RecreateWidgets();
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

            // Rebuilt only when this monitor's accent actually changed: a re-apply of the same
            // wallpaper is answered from the cache and costs nothing.
            bool usesAccent = _config.Widgets.ResolveTheme() != WidgetTheme.Classic;
            if (_accents.OnStaticFrame(captured.Device, path, staticPath, usesAccent))
            {
                if (WidgetStyles.ShouldRebuild(_stylesInUse, captured.Device, StyleFor(captured.Device, record: false)))
                    RecreateWidgets();
                AccentChanged?.Invoke();
            }
        });

        IWallpaperRenderer renderer;
        if (ImageExtensions.Contains(Path.GetExtension(path)))
        {
            renderer = new ImageRenderer(host, monitor.Bounds.Width, monitor.Bounds.Height, _config.Fit, staticPath, onStatic);
        }
        else
        {
            var video = new VideoRenderer(host, monitor.Bounds.Width, monitor.Bounds.Height, _config.Fit,
                _config.MuteVideo, _config.Volume, staticPath, onStatic);
            video.PlaybackFailed += OnPlaybackFailed;
            renderer = video;
        }
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
            if (_clock is null && _info is null && _halo is null && _record is null && _group is null) RecreateWidgets();
            WallpaperChanged?.Invoke();
        }
    }

    /// <summary>Full rebuild: display change, explorer restart, or layer destruction.</summary>
    /// <summary>Playback failed twice on a file. Before this, an undecodable codec produced a
    /// log line and a black desktop with nothing shown to the user — one of the two worst first
    /// impressions the product can make, and the one a stranger from a download link is most
    /// likely to hit. A tray balloon rather than a dialog: the wallpaper is decoration, and a
    /// modal box over someone's desktop is a worse bug than the one being reported.</summary>
    private void OnPlaybackFailed(string path, string detail)
    {
        RunOnMainThread(() =>
        {
            string codec = detail == "unsupported or missing decoder" ? "unsupported" : detail;
            // No codec identifier reaches here: MediaPlayerFailedEventArgs carries no FourCC, and
            // the container extension does not identify the codec — an .mp4 can hold HEVC. Feeding
            // the extension to StoreExtensionFor only ever produced the generic branch anyway, so
            // the guidance says so plainly rather than pretending to know which extension to name.
            const string hint = "HEVC, VP9 and AV1 need their free extension from the Microsoft Store; anything else needs re-encoding to H.264.";

            Log.Warn($"Surfacing playback failure to the user: {Path.GetFileName(path)} ({codec})");
            _tray?.ShowBalloon($"Cannot play {Path.GetFileName(path)}", $"{char.ToUpper(codec[0])}{codec[1..]} video. {hint}");
        });
    }

    /// <summary>A pushed power/display notification. Windows already knows the panel went dark
    /// or the laptop came off AC; this is the app being told instead of polling to find out.
    /// The pause poll still runs for the foreground-window cases it cannot be told about — this
    /// only removes work, it does not add a second source of truth.</summary>
    private void OnPowerSettingChanged(IntPtr lParam)
    {
        if (!PowerNotifications.TryRead(lParam, out var setting, out byte value)) return;

        if (PowerNotifications.IsDisplayState(setting))
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
            _info?.SetSuspended(off);
            _halo?.SetSuspended(off);
            _record?.SetSuspended(off);
            _group?.SetSuspended(off);
        }
        else if (setting == PowerNotifications.PowerSavingStatus || setting == PowerNotifications.AcDcPowerSource)
        {
            // The pause poll reads battery saver directly; nudging it just makes the transition
            // land immediately instead of up to 500 ms later.
            _playback?.Invalidate();
        }

        // Outside the chain above: the battery source filters for the two settings that can
        // change its text, and an AC transition is one of them, so it must see that branch too.
        _battery?.OnPowerSettingChanged(setting);
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
                // A device lost again *during* the rebuild is reported while the guard is still
                // in flight, so nothing else will raise it a second time — the replacement surface
                // is already dead and will never present again to say so. The guard held that
                // signal; act on it. Re-entry is bounded by the same attempt budget, and
                // RunOnMainThread only ever enqueues, so this is a queued retry, not recursion.
                if (_deviceLoss.Complete())
                {
                    Log.Warn("GPU device lost again during recovery — rebuilding once more");
                    OnDeviceLost();
                }
            }
        });
    }

    /// <summary>Public wrapper for call sites that need a full re-attach (settings blur
    /// toggle). Rebuilds the wallpaper layer and every widget.</summary>
    public void ReapplyWallpaperAndWidgets() => ReapplyAll();

    private void ReapplyAll()
    {
        if (_reapplying) return;
        _reapplying = true;
        try
        {
            Log.Info("Re-applying all wallpapers");
            _clock?.Dispose(); // before the windows — its timer renders into their hosts
            _clock = null;
            _info?.Dispose();
            _info = null;
            _halo?.Dispose();
            _halo = null;
            _record?.Dispose();
            _record = null;
            _group?.Dispose();
            _group = null;
            _recordDevice = null;
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

    private void RecreateWidgets()
    {
        _stylesInUse.Clear();
        var theme = _config.Widgets.ResolveTheme();
        if (WidgetsConfig.IsGroup(theme))
        {
            RecreateGroup(theme);
            return;
        }
        _group?.Dispose();
        _group = null;
        RecreateClock();
        RecreateInfo();
    }

    /// <summary>A group theme replaces the four per-widget overlays with one. The sources are kept
    /// exactly as the info widget keeps them: created from Info.Sources, disposed only when the
    /// widget is switched off.</summary>
    private void RecreateGroup(WidgetTheme theme)
    {
        _clock?.Dispose();
        _clock = null;
        _info?.Dispose();
        _info = null;
        _halo?.Dispose();
        _halo = null;
        _record?.Dispose();
        _record = null;
        _group?.Dispose();
        _group = null;
        _recordDevice = null;

        // The text lines are gated by Info.Enabled; the record and the battery are not — they
        // are driven by their own sources and their own toggles, and a user who switches off
        // the info text in favour of art-only over a wallpaper should keep the record.
        // A source only exists when it is listed in Info.Sources, so a user can drop the
        // record by removing "nowPlaying" from the list; battery by removing "battery" or
        // turning the halo off.
        bool info = _config.Info.Enabled;
        bool wantsSources = _config.Info.Sources.Count > 0;
        if (wantsSources) ResolveSources();
        else DisposeSources();

        if (!_config.Clock.Enabled && !info && !wantsSources) return;

        var monitors = MonitorTracker.Enumerate();
        var target = monitors.FirstOrDefault(m =>
                string.Equals(m.Device, _config.Widgets.Monitor, StringComparison.OrdinalIgnoreCase))
            ?? monitors.FirstOrDefault(m => m.Primary) ?? monitors.FirstOrDefault();
        if (target is null) return;

        try
        {
            if (!_windows.TryGetValue(target.Device, out var window))
            {
                window = new WallpaperWindow(target);
                window.DeviceLost += OnDeviceLost;
                _host.Attach(window.Hwnd, target.Bounds);
                window.EnsureHost();
                _windows[target.Device] = window;
            }
            Log.Info($"Widget group: {WidgetsConfig.ConfigName(theme)}, clock={_config.Clock.Enabled}, info={info}, sources=[{string.Join(", ", _config.Info.Sources)}]");
            _group = new GroupOverlay(window.EnsureHost(), GroupThemes.For(theme), _config, target,
                _nowPlaying, _battery,
                MonitorTracker.DpiScale(target, monitors), StyleFor(target.Device), MonitorStillPath);
            _recordDevice = target.Device;
            _nowPlaying?.Refresh();
        }
        catch (Exception ex)
        {
            Log.Error("Widget group failed", ex);
        }
    }

    /// <summary>Rebuilds the info widget. The sources OUTLIVE it.
    ///
    /// They were originally rebuilt with the overlay, on the argument that this path only runs on
    /// a display change or a settings apply and so is rare. That was wrong about settings: a
    /// NumericUpDown raises ValueChanged on every increment, so one drag on a spinner rebuilt the
    /// media-session manager dozens of times and made the widget blink out and back on each one.
    /// The sources now read their config live and are disposed only when the widget is switched
    /// off or the engine goes down.</summary>
    private void RecreateInfo()
    {
        _info?.Dispose();
        _info = null;
        _halo?.Dispose();
        _halo = null;
        _record?.Dispose();
        _record = null;
        _group?.Dispose();
        _group = null;
        _recordDevice = null;
        Log.Info($"Info widget: enabled={_config.Info.Enabled}, sources=[{string.Join(", ", _config.Info.Sources)}]");
        // The info text is gated by Info.Enabled; the record and the detached halo are not —
        // a user who switches the text off in favour of art-only keeps the record, because the
        // record is its own toggle. A source exists only while it is listed in Info.Sources.
        if (!_config.Info.Enabled && !_config.Info.Disc.Enabled && !(_config.Info.Halo is { Enabled: true, Detached: true }))
        {
            DisposeSources();
            return;
        }

        var monitors = MonitorTracker.Enumerate();
        var target = monitors.FirstOrDefault(m =>
                string.Equals(m.Device, _config.Info.Monitor, StringComparison.OrdinalIgnoreCase))
            ?? monitors.FirstOrDefault(m => m.Primary) ?? monitors.FirstOrDefault();
        if (target is null) return;

        try
        {
            if (!_windows.TryGetValue(target.Device, out var window))
            {
                window = new WallpaperWindow(target);
                window.DeviceLost += OnDeviceLost;
                _host.Attach(window.Hwnd, target.Bounds);
                window.EnsureHost();
                _windows[target.Device] = window;
            }
            // ResolveSources first: it is what creates _battery, and the overlay needs it.
            var sources = ResolveSources();
            double dpi = MonitorTracker.DpiScale(target, monitors);
            var style = StyleFor(target.Device);
            if (_config.Info.Enabled)
                _info = new InfoOverlay(window.EnsureHost(), _config.Info, target, sources, _battery, dpi, style);

            // Detached: the halo gets its own overlay instead of being drawn into the info
            // bitmap. Requires the battery source to exist — no battery in Sources, no ring,
            // the same rule attached mode follows.
            if (_config.Info.Halo is { Enabled: true, Detached: true } && _battery is not null)
                _halo = new HaloOverlay(window.EnsureHost(), _config.Info, target, _battery, dpi, style);

            // The record replaces the now-playing text line entirely; it is its own block with its
            // own anchor, not an item in the list.
            if (_config.Info.Disc.Enabled && _nowPlaying is not null)
            {
                _record = new NowPlayingOverlay(window.EnsureHost(), _config.Info, target, _nowPlaying, dpi, style,
                    MonitorStillPath, _config.Widgets.Blur);
                _recordDevice = target.Device;
                Log.Info($"Record: created (rotate={_config.Info.Disc.Rotate}, size={_config.Info.Disc.Size})");
            }
            else
            {
                Log.Info($"Record: not created (disc.enabled={_config.Info.Disc.Enabled}, nowPlaying source={_nowPlaying is not null})");
            }
            // The character budget may have just changed, and the surviving source is still
            // holding text formatted to the old one.
            _nowPlaying?.Refresh();
        }
        catch (Exception ex)
        {
            Log.Error("Info widget failed", ex);
        }
    }

    /// <summary>Config source names to sources, in the configured order. An unknown name is
    /// logged and skipped rather than throwing, so a config written by a later version still
    /// starts this one.</summary>
    /// <summary>True when this machine has a battery. Cached: a machine without one today is not
    /// going to grow one between settings panels, and every battery surface hides itself behind
    /// this — the page in Settings, the rows on the Info page, and the battery source the widgets
    /// read. The config keeps a desktop's settings untouched.</summary>
    public bool BatteryAvailable { get; } = BatterySource.IsAvailable();

    private IReadOnlyList<IWidgetSource> ResolveSources()
    {
        var ordered = new List<IWidgetSource>();
        foreach (var name in _config.Info.Sources)
        {
            switch (name.Trim().ToLowerInvariant())
            {
                case "battery":
                    if (!BatteryAvailable)
                    {
                        Common.Log.Info("Battery source skipped: this machine reports no battery");
                        break;
                    }
                    ordered.Add(_battery ??= new BatterySource());
                    break;
                case "nowplaying":
                    ordered.Add(_nowPlaying ??= new NowPlayingSource(() => _config.Info.MaxCharacters, RunOnMainThread));
                    break;
                default:
                    Log.Warn($"Unknown info source '{name}' ignored");
                    break;
            }
        }
        return ordered;
    }

    private void DisposeSources()
    {
        _battery?.Dispose();
        _battery = null;
        _nowPlaying?.Dispose();
        _nowPlaying = null;
    }

    /// <summary>The theme, and the accent of the monitor a widget sits on. Classic is returned
    /// without touching the accent cache, so it never triggers a sample.</summary>
    private WidgetStyle StyleFor(string monitorDevice, bool record = true)
    {
        var theme = _config.Widgets.ResolveTheme();
        var style = theme == WidgetTheme.Classic
            ? WidgetStyle.Classic
            : new WidgetStyle(theme, Accent.Resolve(_config.Widgets.Accent, _accents.For(monitorDevice)),
                              _config.Widgets.ScaleFor(_config.Clock));
        if (record) _stylesInUse[monitorDevice] = style;
        return style;
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
                MonitorTracker.DpiScale(target, monitors), StyleFor(target.Device));
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
                if (IsRecordMonitor(monitorDevice)) { _record?.SetCovered(false); _group?.SetCovered(false); }
                Log.Info($"Resumed {monitorDevice}");
            }
            else
            {
                window.Renderer.Pause();
                // The record stops for exactly the reasons the wallpaper does. Turning behind a
                // maximised window is the clearest possible waste of a frame clock.
                if (IsRecordMonitor(monitorDevice)) { _record?.SetCovered(true); _group?.SetCovered(true); }
                Log.Info($"Paused {monitorDevice}: {reason}");
            }
        });
    }

    private bool IsRecordMonitor(string monitorDevice) =>
        _recordDevice is not null &&
        string.Equals(_recordDevice, monitorDevice, StringComparison.OrdinalIgnoreCase);

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
    private const uint CmdOpen = 2;
    private const uint CmdRefresh = 3;

    /// <summary>The tray menu is deliberately three items. All management lives in the app:
    /// clicking PrismOS opens the window with everything, and right-clicking is not a second,
    /// worse control panel.</summary>
    private void ShowTrayMenu()
    {
        if (_messageWindow is null) return;

        IntPtr menu = User32.CreatePopupMenu();
        try
        {
            User32.AppendMenuW(menu, MF_STRING, CmdOpen, "Open PrismOS");
            User32.AppendMenuW(menu, MF_STRING, CmdRefresh, "Refresh");
            User32.AppendMenuW(menu, MF_SEPARATOR, 0, null);
            User32.AppendMenuW(menu, MF_STRING, CmdExit, "Exit PrismOS");

            User32.GetCursorPos(out var pt);
            User32.SetForegroundWindow(_messageWindow.Hwnd);
            int cmd = User32.TrackPopupMenuEx(menu, TPM_RIGHTBUTTON | TPM_RETURNCMD, pt.X, pt.Y, _messageWindow.Hwnd, IntPtr.Zero);
            if (cmd > 0) HandleCommand((uint)cmd);
        }
        finally
        {
            User32.DestroyMenu(menu);
        }
    }

    private void HandleCommand(uint cmd)
    {
        switch (cmd)
        {
            case CmdExit:
                User32.PostQuitMessage(0);
                return;
            case CmdOpen:
                OpenSettings();
                return;
            case CmdRefresh:
                Log.Info("Refresh requested from tray menu");
                ReapplyAll();
                return;
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

    private void RestoreOriginalWallpaper() => RestoreSavedWallpaper(_originalWallpaper);

    /// <summary>Puts back the wallpaper saved at startup and deletes the saved copy. Static, and
    /// reads the saved path itself when not given one, for the uninstaller: Velopack force-stops the
    /// app before the uninstall hook runs, so the app's own exit never got here.</summary>
    public static void RestoreSavedWallpaper(string? original = null)
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

            original ??= File.Exists(OriginalWallpaperFile) ? File.ReadAllText(OriginalWallpaperFile).Trim() : null;
            if (!string.IsNullOrEmpty(original))
                DesktopWallpaper.RestoreCurrent(original);
            if (File.Exists(OriginalWallpaperFile))
                File.Delete(OriginalWallpaperFile);
        }
        catch (Exception ex)
        {
            Log.Warn($"Could not restore original wallpaper: {ex.Message}");
        }
    }

    private void SaveAndRefreshWidgets()
    {
        ConfigStore.Save(_config);
        RecreateWidgets();
    }

    /// <summary>A clock-only change. Deliberately does not touch the info widget: every clock
    /// control in the settings panel lands here, once per spinner increment, and rebuilding the
    /// other widget's overlay for each of those is work nobody asked for.</summary>
    private void SaveAndRefreshClockOnly()
    {
        ConfigStore.Save(_config);
        if (WidgetsConfig.IsGroup(_config.Widgets.ResolveTheme())) RecreateWidgets();
        else RecreateClock();
    }

    // ---- settings panel hooks ------------------------------------------------------------

    public AppConfig Config => _config;

    /// <summary>Credit for the wallpaper currently in use, or null when it is the user's own file.
    /// Surfaced in the settings panel so gallery media is actually attributed on screen.</summary>
    public GalleryEntry? CurrentWallpaperCredit => CurrentWallpaperCreditFor(
        MonitorTracker.Enumerate().FirstOrDefault(m => m.Primary)?.Device ?? "*");

    /// <summary>The gallery entry behind the wallpaper on a given display, or null for the
    /// user's own file. Per display, because a second display can be showing something else —
    /// the credit belongs to what that display is showing.</summary>
    public GalleryEntry? CurrentWallpaperCreditFor(string monitorDevice) =>
        _gallery.EntryForPath(_config.WallpaperFor(monitorDevice));

    /// <summary>The gallery entries, for the settings panel's gallery picker.</summary>
    public IReadOnlyList<GalleryEntry> GalleryEntries => _gallery.Manifest.Entries;

    /// <summary>Whether a gallery entry is already on disk, so applying it costs no download.</summary>
    public bool IsGalleryDownloaded(GalleryEntry entry) => _gallery.LocalPathIfDownloaded(entry) is not null;

    /// <summary>Downloads a gallery entry if it is not cached, and returns the local file.</summary>
    public Task<string> DownloadGalleryEntryAsync(GalleryEntry entry, IProgress<double>? progress = null) =>
        _gallery.EnsureDownloadedAsync(entry, progress);

    /// <summary>The file to preview for a display: the image itself, or the video file. The
    /// settings preview renders both — a video plays live in the panel — so it needs the source,
    /// not a captured still. Null when nothing is set there or the file is gone.</summary>
    public string? WallpaperPreviewPath(string monitorDevice)
    {
        var path = _config.WallpaperFor(monitorDevice);
        return path is not null && File.Exists(path) ? path : null;
    }

    /// <summary>Raised when the wallpaper changes, so an open settings panel redraws its preview
    /// instead of showing the wallpaper that was there when it opened.</summary>
    public event Action? WallpaperChanged;

    public void SaveConfig() => ConfigStore.Save(_config);

    /// <summary>Puts every setting back to its default and applies the result at once.
    ///
    /// The widgets are rebuilt rather than merely refreshed, because the reset changes which
    /// theme is in use and which widgets exist at all: the group themes draw one overlay where
    /// the others draw four, and nothing short of a rebuild swaps between them.</summary>
    public void ResetSettingsToDefaults()
    {
        _config.ResetToDefaults();
        ConfigStore.Save(_config);
        RecreateWidgets();
        RefreshWallpapers();
        ApplyAudioSettings();
        AccentChanged?.Invoke();
        WidgetsChanged?.Invoke();
        Log.Info("Settings reset to defaults");
    }

    /// <summary>Clock appearance changed: persist and rebuild the overlay (cheap).</summary>
    public void RefreshWidgets() => SaveAndRefreshWidgets();

    /// <summary>The one way a theme is chosen, from the tray or the Style page. Applies at once.</summary>
    public void SetWidgetTheme(WidgetTheme theme, string from = "settings")
    {
        _config.Widgets.Theme = WidgetsConfig.ConfigName(theme);
        Log.Info($"Widget style: {_config.Widgets.Theme} ({from})");
        SaveAndRefreshWidgets();
        WidgetsChanged?.Invoke();
    }

    /// <summary>"auto" or #RRGGBB, as the config takes it.</summary>
    public void SetWidgetAccent(string accent)
    {
        _config.Widgets.Accent = accent;
        Log.Info($"Widget accent: {accent}");
        SaveAndRefreshWidgets();
        WidgetsChanged?.Invoke();
    }

    /// <summary>The accent the themes would use on the clock's monitor, whichever theme is active,
    /// and where it came from: "from wallpaper", "default" (nothing sampled yet, or a wallpaper
    /// with no colour), or "fixed".</summary>
    public (System.Drawing.Color Colour, string Source) CurrentAccent()
    {
        var sampled = _accents.For(PreviewDevice());
        return (Accent.Resolve(_config.Widgets.Accent, sampled), Accent.Source(_config.Widgets.Accent, sampled));
    }

    /// <summary>Raised on the main thread when a monitor's sampled accent arrives or changes, so an
    /// open Style page can show it rather than the default it opened with.</summary>
    public event Action? AccentChanged;

    /// <summary>Raised on the main thread after a style or accent is chosen, from the tray or the
    /// Style page, so an open Settings window shows it.</summary>
    public event Action? WidgetsChanged;

    /// <summary>The static frame of the clock's monitor, for the Style page's tiles. Null before
    /// any frame has been written.</summary>
    public string? PreviewBackdropPath()
    {
        var path = StaticPath(PreviewDevice());
        return File.Exists(path) ? path : null;
    }

    /// <summary>The still that stands in for a video wallpaper: the frame the engine captured
    /// when the wallpaper loaded. Per monitor, so "*" is the primary's. Null if none was written
    /// yet.</summary>
    public string? WallpaperStillPath(string monitorDevice)
    {
        string device = monitorDevice == "*"
            ? MonitorTracker.Enumerate().FirstOrDefault(m => m.Primary)?.Device ?? monitorDevice
            : monitorDevice;
        var frame = StaticPath(device);
        return File.Exists(frame) ? frame : null;
    }

    /// <summary>The blur source for a monitor's widgets: the wallpaper image file, or the
    /// static frame captured once a video wallpaper has been played at least once. Null when
    /// there is nothing to frost with.</summary>
    public string? MonitorStillPath(MonitorInfo monitor)
    {
        var path = _config.WallpaperFor(monitor.Device);
        if (path is null) return null;
        if (ImageExtensions.Contains(Path.GetExtension(path))) return File.Exists(path) ? path : null;
        var frame = StaticPath(monitor.Device);
        return File.Exists(frame) ? frame : null;
    }

    /// <summary>The monitor to preview, for call sites that want the same device the
    /// wallpaper targets (re-applied on resume).</summary>
    private string PreviewDevice()
    {
        var monitors = MonitorTracker.Enumerate();
        string wanted = WidgetsConfig.IsGroup(_config.Widgets.ResolveTheme()) ? _config.Widgets.Monitor : _config.Clock.Monitor;
        var target = monitors.FirstOrDefault(m =>
                string.Equals(m.Device, wanted, StringComparison.OrdinalIgnoreCase))
            ?? monitors.FirstOrDefault(m => m.Primary) ?? monitors.FirstOrDefault();
        return target?.Device ?? "";
    }

    /// <summary>For the settings panel's clock and date pages.</summary>
    public void RefreshClock() => SaveAndRefreshClockOnly();

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

    public void ApplyGalleryEntry(GalleryEntry entry)
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
        sb.AppendLine($"PrismOS {typeof(Engine).Assembly.GetName().Version}");
        sb.AppendLine($"Topology: {layer.Topology}");
        sb.AppendLine($"Progman: 0x{layer.Progman:X}  WorkerW: 0x{layer.WorkerW:X}  DefView: 0x{layer.DefView:X}");
        foreach (var monitor in MonitorTracker.Enumerate())
            sb.AppendLine($"Monitor {monitor.Device}: {monitor.Bounds}{(monitor.Primary ? " (primary)" : "")}");
        sb.AppendLine($"Attached surfaces: {_windows.Count}   Clock: {(_clock is null ? "off" : "on")}   Info: {(_info is null ? "off" : "on")}   Halo: {(_halo is null ? "attached/off" : "detached")}   Record: {(_record is null ? "off" : "on")}   Group: {(_group is null ? "off" : "on")}");
        sb.AppendLine($"Config: {ConfigStore.ConfigPath}");
        sb.AppendLine($"Log: {Path.Combine(Log.Directory, "prismos.log")}");
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
            CreateWindow("PrismOSMessage", 0, 0, 0, 0, 0, 0, IntPtr.Zero);
        }

        protected override IntPtr HandleMessage(uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == TrayIcon.CallbackMessage)
            {
                uint mouse = (uint)(lParam.ToInt64() & 0xFFFF);
                const uint NIN_BALLOONUSERCLICK = 0x0405;
                if (mouse is >= 0x0402 and <= NIN_BALLOONUSERCLICK)
                    Log.Info($"Tray balloon event 0x{mouse:X}");
                // A timeout does not clear the click: with Do Not Disturb on, Windows reports the
                // balloon timed out 15 ms after showing it and files it in the notification centre,
                // where it is still clickable. Only a newer balloon replaces the action.
                if (mouse == NIN_BALLOONUSERCLICK)
                    _engine._tray?.Clicks.OnClick();
                else if (mouse is WM_RBUTTONUP or WM_CONTEXTMENU)
                    _engine.ShowTrayMenu();
                else if (mouse == WM_LBUTTONDBLCLK || mouse == WM_LBUTTONUP)
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
                    Log.Info("Resumed from sleep — refreshing widgets and validating layer");
                    _engine._clock?.Refresh();
                    // Sources first: repainting the overlay alone would redraw the values it was
                    // holding when the machine went to sleep. No power notification arrives for
                    // the change that happened while it was off.
                    _engine._battery?.Refresh();
                    _engine._nowPlaying?.Refresh();
                    _engine._info?.Refresh();
                    _engine._halo?.Refresh();
                    _engine._record?.Refresh();
                    _engine._group?.Refresh();
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
        _info?.Dispose();
        _info = null;
        _halo?.Dispose();
        _halo = null;
        _record?.Dispose();
        _record = null;
        _group?.Dispose();
        _group = null;
        _recordDevice = null;
        DisposeSources();
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
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "PrismOS";

    public static bool IsEnabled()
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is not null;
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(RunKey);
        if (enabled && Environment.ProcessPath is { } exe)
            // --silent: a login start attaches the wallpaper in the background and does not
            // open the window. Opening PrismOS yourself is how it opens the window.
            key.SetValue(ValueName, $"\"{exe}\" --silent");
        else
            key.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>An earlier version registered the bare exe for login start, which would pop the
    /// settings window at every sign-in. Rewrite the value with --silent once at startup.</summary>
    public static void EnsureSilentFlag()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is not string value || value.Contains("--silent", StringComparison.OrdinalIgnoreCase)) return;
            key.SetValue(ValueName, $"{value} --silent");
        }
        catch { /* best effort; worst case the window opens once at login */ }
    }
}
