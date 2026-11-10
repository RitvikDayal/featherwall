using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using FeatherWall.Config;
using FeatherWall.Widgets;
using FeatherWall.Widgets.Themes;

namespace FeatherWall.Tests;

/// <summary>Editorial's layout decisions. The look itself is checked by rendering it and looking;
/// these pin the arithmetic and the settings it must ignore.</summary>
public class EditorialTests
{
    private static readonly DateTime Noon = new(2026, 11, 10, 12, 30, 0);
    private static int Pad(float scale) => (int)Math.Ceiling(ShadowRecipe.Default.Pad * scale);

    private static void AssertInset(Rectangle content, Size total, int pad)
    {
        Assert.True(content.Left >= pad, $"left {content.Left} < {pad}");
        Assert.True(content.Top >= pad, $"top {content.Top} < {pad}");
        Assert.True(total.Width - content.Right >= pad, $"right {total.Width - content.Right} < {pad}");
        Assert.True(total.Height - content.Bottom >= pad, $"bottom {total.Height - content.Bottom} < {pad}");
    }

    [Fact]
    public void Clock_DateSitsAboveTime()
    {
        var m = Editorial.MeasureClock(new ClockConfig(), Noon, 1f);
        Assert.False(m.DateBox.IsEmpty);
        Assert.True(m.DateBox.Bottom <= m.TimeBox.Top);
    }

    [Fact]
    public void Clock_NoDate_NoDateBox()
    {
        var with = Editorial.MeasureClock(new ClockConfig(), Noon, 1f);
        var without = Editorial.MeasureClock(new ClockConfig { ShowDate = false }, Noon, 1f);
        Assert.True(without.DateBox.IsEmpty);
        Assert.True(without.Total.Height < with.Total.Height);
    }

    [Fact]
    public void Clock_DateIsUppercase()
    {
        var m = Editorial.MeasureClock(new ClockConfig(), Noon, 1f);
        Assert.Equal(m.Date!.ToUpper(CultureInfo.CurrentCulture), m.Date);
    }

    [Fact]
    public void Clock_ScalesWithFontSize()
    {
        var one = Editorial.MeasureClock(new ClockConfig(), Noon, Editorial.Scale(new ClockConfig(), 1.0));
        var big = new ClockConfig { FontSize = 220 };
        var two = Editorial.MeasureClock(big, Noon, Editorial.Scale(big, 1.0));
        Assert.InRange(two.Total.Width, one.Total.Width * 1.9, one.Total.Width * 2.1);
    }

    [Fact]
    public void Clock_IgnoresClassicOnlySettings()
    {
        var plain = Editorial.MeasureClock(new ClockConfig(), Noon, 1f);
        var styled = Editorial.MeasureClock(
            new ClockConfig { FontFamily = "Consolas", Color = "#FF0000", Separator = false }, Noon, 1f);
        Assert.Equal(plain.Total, styled.Total);
    }

    [Fact]
    public void Surfaces_IncludeShadowPad()
    {
        const float scale = 1.5f;
        int pad = Pad(scale);

        var clock = Editorial.MeasureClock(new ClockConfig(), Noon, scale);
        AssertInset(Rectangle.Union(clock.DateBox, clock.TimeBox), clock.Total, pad);

        var record = Editorial.MeasureRecord(new DiscConfig(),
            new NowPlayingReading("Bairan", "Banjaare", true, "t1", 0.4), scale);
        AssertInset(Rectangle.Union(record.DiscBox, record.ArtistBox), record.Total, pad);

        var halo = Editorial.MeasureHalo(new HaloConfig(), new BatteryReading(76, BatteryState.OnBattery), scale);
        var info = Editorial.MeasureInfo(new InfoConfig(), [], scale, halo);
        AssertInset(info.HaloBox, info.Total, pad);
    }

    [Fact]
    public void Halo_IgnoresHaloSize()
    {
        var reading = new BatteryReading(76, BatteryState.OnBattery);
        Assert.Equal(Editorial.MeasureHalo(new HaloConfig { Size = 44 }, reading, 1f),
                     Editorial.MeasureHalo(new HaloConfig { Size = 90 }, reading, 1f));
    }

    [Fact]
    public void HaloColour_HighIsAccent_LowStaysRed()
    {
        var accent = Color.FromArgb(255, 0xC0, 0x8B, 0xFF);
        var h = new HaloConfig();
        Assert.Equal(accent, Editorial.HaloColour(80, BatteryState.OnBattery, h, accent));
        Assert.Equal(ColorTranslator.FromHtml("#FF4D4D").ToArgb(),
                     Editorial.HaloColour(10, BatteryState.OnBattery, h, accent).ToArgb());
        Assert.Equal(ColorTranslator.FromHtml("#FF9A3C").ToArgb(),
                     Editorial.HaloColour(35, BatteryState.OnBattery, h, accent).ToArgb());
        Assert.Equal(accent, Editorial.HaloColour(100, BatteryState.Charged, h, accent));
        Assert.Equal(accent, Editorial.HaloColour(10, BatteryState.OnBattery, new HaloConfig { ColorByLevel = false }, accent));
    }

    [Fact]
    public void Fonts_FallBackWithoutVariable()
    {
        var real = ThemeFonts.IsInstalled;
        try
        {
            ThemeFonts.IsInstalled = n => !n.StartsWith("Segoe UI Variable", StringComparison.Ordinal);
            using var font = ThemeFonts.Create(ThemeFonts.DisplayLight, 20);
            Assert.Equal("Segoe UI Light", font.Name);
        }
        finally
        {
            ThemeFonts.IsInstalled = real;
        }
    }

    [Fact]
    public void Fonts_VariableResolvesWhereInstalled()
    {
        // CI's runner image may not carry Segoe UI Variable (it ships with Windows 11).
        if (!ThemeFonts.IsInstalled("Segoe UI Variable Text Semibold")) return;
        using var font = ThemeFonts.Create(ThemeFonts.TextSemibold, 20);
        Assert.StartsWith("Segoe UI Variable Text", font.Name);
    }

    [Fact]
    public void Shadow_SolidEllipse_CastsOutside_ButNotInside()
    {
        // The record's face turns on a layer beneath the rim and words. Its shadow is drawn on
        // the layer above, so it must stop at the face's edge or it would darken the artwork.
        using var b = new Bitmap(60, 60, PixelFormat.Format32bppPArgb);
        BakedShadow.DrawBehind(b, ShadowRecipe.Default, 1f, new RectangleF(15, 15, 30, 30));
        Assert.True(b.GetPixel(30, 47).A > 0);
        Assert.Equal(0, b.GetPixel(30, 30).A);
    }
}
