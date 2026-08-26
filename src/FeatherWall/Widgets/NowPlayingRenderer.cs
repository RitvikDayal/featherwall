using System.Drawing;
using System.Drawing.Text;
using FeatherWall.Config;

namespace FeatherWall.Widgets;

/// <summary>Lays out and paints the now-playing block: the record, then the title and the artist.
///
/// The typography is the clock's, not the info widget's. The clock pairs a large light time with a
/// small dimmed date; this pairs a near-white title with a smaller artist in spaced capitals. The
/// info widget's uniform 22 px text was the thing that made the widget look unrelated to the
/// clock sitting above it.</summary>
public static class NowPlayingRenderer
{
    public readonly record struct NowPlayingMetrics(Size Total, Rectangle DiscBox, Rectangle TitleBox, Rectangle ArtistBox);

    private const int DiscGap = 16;
    private const int LineGap = 3;
    private const int Pad = 6;

    /// <summary>How far a paused block fades. Matches the record's own dim so the two halves of
    /// the widget agree about the state.</summary>
    private const float PausedFade = 0.55f;

    private static readonly StringFormat Tight = new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.MeasureTrailingSpaces,
    };

    public static Font TitleFont(DiscConfig config, float scale) =>
        new("Segoe UI", Math.Max(config.TitleFontSize * scale, 7f), FontStyle.Regular, GraphicsUnit.Pixel);

    public static Font ArtistFont(DiscConfig config, float scale) =>
        new("Segoe UI", Math.Max(config.ArtistFontSize * scale, 6f), FontStyle.Regular, GraphicsUnit.Pixel);

    /// <summary>Empty when there is nothing playing. The disc and the text are measured together
    /// so the overlay paints from the boxes rather than recomputing this arithmetic.</summary>
    public static NowPlayingMetrics Measure(DiscConfig config, NowPlayingReading reading, float scale = 1f)
    {
        var disc = DiscRenderer.Measure(config, reading);
        if (disc.IsEmpty && string.IsNullOrWhiteSpace(reading.Title))
            return new NowPlayingMetrics(Size.Empty, Rectangle.Empty, Rectangle.Empty, Rectangle.Empty);

        if (!disc.IsEmpty)
            disc = new Size((int)Math.Round(disc.Width * scale), (int)Math.Round(disc.Height * scale));

        using var titleFont = TitleFont(config, scale);
        using var artistFont = ArtistFont(config, scale);
        using var g = Graphics.FromHwnd(IntPtr.Zero);

        string title = reading.Title ?? "";
        string artist = Artist(config, reading);

        var titleSize = title.Length > 0 ? g.MeasureString(title, titleFont, PointF.Empty, Tight) : SizeF.Empty;
        var artistSize = artist.Length > 0 ? MeasureTracked(g, artist, artistFont, config.ArtistLetterSpacing * scale) : SizeF.Empty;

        int textW = (int)Math.Ceiling(Math.Max(titleSize.Width, artistSize.Width));
        int textH = (int)Math.Ceiling(titleSize.Height)
                  + (artist.Length > 0 ? LineGap + (int)Math.Ceiling(artistSize.Height) : 0);

        int gap = disc.IsEmpty || textW == 0 ? 0 : DiscGap;
        int blockH = Math.Max(disc.Height, textH);

        var discBox = disc.IsEmpty
            ? Rectangle.Empty
            : new Rectangle(Pad, Pad + (blockH - disc.Height) / 2, disc.Width, disc.Height);

        int textX = Pad + (disc.IsEmpty ? 0 : disc.Width + gap);
        int textY = Pad + (blockH - textH) / 2;

        var titleBox = title.Length > 0
            ? new Rectangle(textX, textY, (int)Math.Ceiling(titleSize.Width), (int)Math.Ceiling(titleSize.Height))
            : Rectangle.Empty;
        var artistBox = artist.Length > 0
            ? new Rectangle(textX, textY + (int)Math.Ceiling(titleSize.Height) + LineGap,
                            (int)Math.Ceiling(artistSize.Width), (int)Math.Ceiling(artistSize.Height))
            : Rectangle.Empty;

        var total = new Size(textX + textW + Pad, blockH + Pad * 2);
        return new NowPlayingMetrics(total, discBox, titleBox, artistBox);
    }

    private static string Artist(DiscConfig config, NowPlayingReading reading) =>
        reading.Artist is null ? "" : config.ArtistUppercase ? reading.Artist.ToUpperInvariant() : reading.Artist;

    /// <summary>GDI+ has no letter-spacing, so tracked text is drawn a glyph at a time and has to
    /// be measured the same way.</summary>
    private static SizeF MeasureTracked(Graphics g, string text, Font font, float tracking)
    {
        float w = 0, h = 0;
        foreach (char c in text)
        {
            var s = g.MeasureString(c.ToString(), font, PointF.Empty, Tight);
            w += s.Width + tracking;
            h = Math.Max(h, s.Height);
        }
        return new SizeF(Math.Max(w - tracking, 0), h);
    }

    public static void Paint(Graphics g, in NowPlayingMetrics metrics, DiscConfig config,
                             NowPlayingReading reading, Bitmap? face, float turns, float progress, float scale = 1f)
    {
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        if (!metrics.DiscBox.IsEmpty && face is not null)
            DiscRenderer.Paint(g, metrics.DiscBox, config, face, turns, progress, reading.IsPlaying);

        using var titleFont = TitleFont(config, scale);
        using var artistFont = ArtistFont(config, scale);

        // Paused fades the words with the record. Dimming only the disc left bright text beside a
        // ghost of a record, which reads as a rendering fault rather than a state.
        float fade = reading.IsPlaying ? 1f : PausedFade;

        if (!metrics.TitleBox.IsEmpty)
            DrawShadowed(g, reading.Title!, titleFont, Color.FromArgb((int)(245 * fade), 246, 249, 255),
                         metrics.TitleBox.X, metrics.TitleBox.Y);

        if (metrics.ArtistBox.IsEmpty) return;
        int artistAlpha = (int)(255 * Math.Clamp(config.ArtistOpacity, 0f, 1f) * fade);
        DrawTracked(g, Artist(config, reading), artistFont, Color.FromArgb(artistAlpha, 226, 234, 246),
                    metrics.ArtistBox.X, metrics.ArtistBox.Y, config.ArtistLetterSpacing * scale);
    }

    private static void DrawShadowed(Graphics g, string text, Font font, Color colour, float x, float y)
    {
        using (var shadow = new SolidBrush(Color.FromArgb(140, 0, 0, 0)))
            g.DrawString(text, font, shadow, x + 1.3f, y + 1.3f, Tight);
        using var brush = new SolidBrush(colour);
        g.DrawString(text, font, brush, x, y, Tight);
    }

    private static void DrawTracked(Graphics g, string text, Font font, Color colour, float x, float y, float tracking)
    {
        foreach (char c in text)
        {
            string s = c.ToString();
            DrawShadowed(g, s, font, colour, x, y);
            x += g.MeasureString(s, font, PointF.Empty, Tight).Width + tracking;
        }
    }
}
