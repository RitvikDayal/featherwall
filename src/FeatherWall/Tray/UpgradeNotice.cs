using FeatherWall.Config;

namespace FeatherWall.Tray;

/// <summary>The one-time tray balloon telling an upgrader the widgets have new styles.
///
/// Shown once, to someone who upgraded and is still on Classic; saving that it was shown happens
/// at once, clicked or not. A fresh install never sees it — it already starts on a new theme. A
/// config that failed to load is never touched: showing the notice means saving, and saving over
/// an unreadable file replaces the user's settings with defaults.</summary>
public static class UpgradeNotice
{
    public const string Title = "New widget styles";
    public const string Text = "Five looks for the clock, music and battery. Click to choose.";

    public static bool ShouldShow(AppConfig config, bool configUnreadable) =>
        !configUnreadable
        && !config.IsFreshInstall
        && config.Widgets.ResolveTheme() == WidgetTheme.Classic
        && !config.Widgets.ThemesNoticeShown;
}

/// <summary>What clicking the current tray balloon does. One-shot, and set by every balloon — a
/// balloon shown without an action clears the previous one, so a click on an error or a download
/// balloon can never run the notice's "open settings".</summary>
public sealed class BalloonClick
{
    private Action? _pending;

    public void Set(Action? onClick) => _pending = onClick;

    public void OnClick()
    {
        var action = _pending;
        _pending = null;
        action?.Invoke();
    }
}
