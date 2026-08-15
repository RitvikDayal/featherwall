using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace FeatherWall.Tray;

public sealed record Palette(
    Color Window, Color Card, Color Border, Color Text,
    Color Subtle, Color Accent, Color Control, Color Preview);

/// <summary>Follows the Windows app-theme setting so the settings panel doesn't flashbang anyone
/// running a dark desktop — which, for a live-wallpaper app, is most people.</summary>
public static class Theme
{
    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static bool IsDark { get; } = AppsUseLightTheme() == 0;

    public static Palette Colors { get; } = IsDark
        ? new Palette(
            Window: Color.FromArgb(0x20, 0x20, 0x20),
            Card: Color.FromArgb(0x2B, 0x2B, 0x2B),
            Border: Color.FromArgb(0x3D, 0x3D, 0x3D),
            Text: Color.FromArgb(0xF2, 0xF2, 0xF2),
            Subtle: Color.FromArgb(0x9B, 0x9B, 0x9B),
            Accent: Color.FromArgb(0x4C, 0x9A, 0xFF),
            Control: Color.FromArgb(0x38, 0x38, 0x38),
            Preview: Color.FromArgb(0x14, 0x16, 0x1A))
        : new Palette(
            Window: Color.FromArgb(0xF3, 0xF3, 0xF3),
            Card: Color.White,
            Border: Color.FromArgb(0xE0, 0xE0, 0xE0),
            Text: Color.FromArgb(0x1A, 0x1A, 0x1A),
            Subtle: Color.FromArgb(0x60, 0x60, 0x60),
            Accent: Color.FromArgb(0x00, 0x5F, 0xB8),
            Control: Color.White,
            Preview: Color.FromArgb(0x24, 0x26, 0x2B));

    private static int AppsUseLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") as int? ?? 1;
        }
        catch { return 1; }
    }

    /// <summary>Paints the non-client title bar to match; silently ignored pre-20H1.</summary>
    public static void ApplyTitleBar(IntPtr hwnd)
    {
        try
        {
            int dark = IsDark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        }
        catch { /* cosmetic only */ }
    }

    public static void StyleInput(Control control)
    {
        control.BackColor = Colors.Control;
        control.ForeColor = Colors.Text;
        if (control is ComboBox combo)
        {
            combo.FlatStyle = FlatStyle.Flat;
        }
        else if (control is CheckBox check)
        {
            check.FlatStyle = FlatStyle.Flat;
            check.BackColor = Colors.Card;
            check.FlatAppearance.BorderColor = Colors.Border;
            check.FlatAppearance.CheckedBackColor = Colors.Accent;
        }
    }
}
