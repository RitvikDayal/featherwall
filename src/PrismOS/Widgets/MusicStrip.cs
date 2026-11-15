using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using PrismOS.Widgets.Themes;
using PrismOS.Common;

namespace PrismOS.Widgets;

/// <summary>The modern now-playing pill: album art, small-caps source app, title, artist and
/// previous / play-pause / next buttons, on a rounded dark card. One renderer for every style,
/// so Classic and the group themes share the face instead of each growing their own.
///
/// The three buttons are drawn here and hit-tested by an invisible clickable window the
/// overlays own (the composition tree itself is click-through by construction, so clicks need
/// a real window of their own).</summary>
public static class MusicStrip
{
    public const int ButtonCount = 3;

    // Base proportions in pixels at scale 1 (the Windows 11 media pill).
    private const float Height = 64f, ArtSidePx = 48f, PadPx = 8f, ArtGapPx = 10f, LineGapPx = 2f;
    private const float SourcePx = 8.5f, TitlePx = 12.5f, ArtistPx = 9.5f;
    private const float ButtonPx = 28f, ButtonGapPx = 6f, ButtonsRightPx = 8f, TextButtonGapPx = 12f;
    private const float MaxTextWidthPx = 190f;

    /// <summary>Everything a painter or a hit-test needs, in panel pixels. All boxes are
    /// relative to the panel's top-left.</summary>
    public readonly record struct Metrics(
        Size Size, int Pad, int ArtSide, int ArtGap,
        int SourceTop, int TitleTop, int ArtistTop,
        string SourceText, string TitleText, string? ArtistText,
        Rectangle ArtBox, Rectangle EqBox, Rectangle PrevBox, Rectangle PlayBox, Rectangle NextBox)
    {
        /// <summary>The clickable button rects, in order: previous, play/pause, next.</summary>
        public Rectangle Button(int index) => index switch
        {
            0 => PrevBox, 1 => PlayBox, _ => NextBox,
        };

        public bool TryHitButton(Point panelPoint, out int index)
        {
            for (int i = 0; i < ButtonCount; i++)
                if (Button(i).Contains(panelPoint)) { index = i; return true; }
            index = -1;
            return false;
        }
    }

    /// <summary><paramref name="maxWidth"/> caps the whole pill — the text column shrinks
    /// first, so position never spills off the work area at large scales.</summary>
    public static Metrics Measure(Graphics g, NowPlayingReading r, string? source, float scale, float maxWidth = float.MaxValue)
    {
        float height = Height * scale, art = ArtSidePx * scale, pad = PadPx * scale;
        float secX = pad + art + ArtGapPx * scale;

        // Everything the pill spends on things other than the text: art, gaps, the
        // equalizer column and the transport buttons. The leftover goes to text, clamped
        // so the pill is never wider than the caller's cap.
        float eqWidth = 16f * scale, eqGap = 10f * scale;
        float buttonsW = ButtonPx * scale * 3 + ButtonGapPx * scale * 2;
        float overhead = secX + eqGap + eqWidth + eqGap + TextButtonGapPx * scale + buttonsW + ButtonsRightPx * scale;
        float textWidth = Math.Min(MaxTextWidthPx * scale, Math.Max(maxWidth - overhead, 8f * scale));

        using var sourceFont = ThemeFonts.Create(ThemeFonts.TextSemibold, Math.Max(SourcePx * scale, 7f));
        using var titleFont = ThemeFonts.Create(ThemeFonts.TextSemibold, TitlePx * scale);
        using var artistFont = ThemeFonts.Create(ThemeFonts.TextSemibold, Math.Max(ArtistPx * scale, 7f));

        float sourcePx = sourceFont.Size, titlePx = titleFont.Size, artistPx = artistFont.Size;
        string sourceText = Fit(g, (string.IsNullOrWhiteSpace(source) ? "NOW PLAYING" : source).ToUpper(CultureInfo.InvariantCulture),
            sourceFont, textWidth, sourcePx * 0.15f);
        string titleText = Fit(g, r.Title ?? "", titleFont, textWidth, 0f);
        string? artistText = string.IsNullOrWhiteSpace(r.Artist)
            ? null
            : Fit(g, r.Artist!, artistFont, textWidth, artistPx * 0.15f);

        var sourceSize = ThemeFonts.MeasureTracked(g, sourceText, sourceFont, sourcePx * 0.15f);
        var titleSize = ThemeFonts.MeasureTracked(g, titleText, titleFont, 0f);
        var artistSize = artistText is null ? SizeF.Empty : ThemeFonts.MeasureTracked(g, artistText, artistFont, artistPx * 0.15f);

        float textHeight = sourceSize.Height + LineGapPx * scale + titleSize.Height + LineGapPx * scale + artistSize.Height;
        float srcY = (height - textHeight) / 2f + 0.5f * scale;
        float titleY = srcY + sourceSize.Height + LineGapPx * scale;
        float artistY = titleY + titleSize.Height + LineGapPx * scale;

        float btn = ButtonPx * scale, gap = ButtonGapPx * scale;
        float btnSize = btn * (ButtonCount - 1) + gap * (ButtonCount - 1) + btn;
        float total = secX + textWidth + eqGap + eqWidth + eqGap + TextButtonGapPx * scale + btnSize + ButtonsRightPx * scale;

        float btnY = (height - btn) / 2f;
        float eqX = secX + textWidth + eqGap;
        float prevX = eqX + eqWidth + eqGap + TextButtonGapPx * scale;
        float playX = prevX + btn + gap;
        float nextX = playX + btn + gap;

        float eqH = Math.Min(34f * scale, height * 0.52f);
        return new Metrics(
            new Size((int)Math.Ceiling(total), (int)Math.Ceiling(height)),
            (int)Math.Ceiling(pad), (int)Math.Ceiling(art), (int)Math.Ceiling(ArtGapPx * scale),
            (int)srcY, (int)titleY, (int)artistY,
            sourceText, titleText, artistText,
            new Rectangle((int)pad, (int)((height - art) / 2f), (int)art, (int)art),
            new Rectangle((int)eqX, (int)((height - eqH) / 2f), (int)eqWidth, (int)eqH),
            new Rectangle((int)prevX, (int)btnY, (int)btn, (int)btn),
            new Rectangle((int)playX, (int)btnY, (int)btn, (int)btn),
            new Rectangle((int)nextX, (int)btnY, (int)btn, (int)btn));
    }

    /// <summary><paramref name="levels"/> carries the live 12-band meter (see LoopbackMeter).
    /// Null keeps the pre-audio behaviour (deterministic sine) so previews and tests still render.</summary>
    public static void Paint(Graphics g, in Metrics m, NowPlayingReading r, Bitmap? art, float scale, float eqPhase, int cardAlpha = 216, float[]? levels = null)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        // Card
        using var path = RoundedRect(new RectangleF(0, 0, m.Size.Width, m.Size.Height), Height * scale * 0.30f);
        using (var brush = new SolidBrush(Color.FromArgb(Math.Clamp(cardAlpha, 0, 255), 26, 30, 39)))
            g.FillPath(brush, path);
        using (var border = new Pen(Color.FromArgb(26, 255, 255, 255), 1f))
            g.DrawPath(border, path);

        // Artwork
        using var artPath = RoundedRect(m.ArtBox, ArtSidePx * scale * 0.18f);
        if (art is not null)
        {
            g.SetClip(artPath, CombineMode.Replace);
            double s = Math.Max((double)m.ArtBox.Width / art.Width, (double)m.ArtBox.Height / art.Height);
            int w = Math.Max(1, (int)Math.Round(art.Width * s)), h = Math.Max(1, (int)Math.Round(art.Height * s));
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(art, m.ArtBox.X + (m.ArtBox.Width - w) / 2, m.ArtBox.Y + (m.ArtBox.Height - h) / 2, w, h);
            g.ResetClip();
        }
        else
        {
            // No thumbnail from the media session: an accent-tinted tile with the
            // note glyph, instead of a black box with a play icon.
            using var brush = new LinearGradientBrush(m.ArtBox, Color.FromArgb(255, 58, 44, 84), Color.FromArgb(255, 16, 18, 26), 135f);
            g.FillPath(brush, artPath);
        }

        // Text column
        float x = m.Pad + m.ArtSide + m.ArtGap;
        using var sourceFont = ThemeFonts.Create(ThemeFonts.TextSemibold, Math.Max(SourcePx * scale, 7f));
        using var titleFont = ThemeFonts.Create(ThemeFonts.TextSemibold, TitlePx * scale);
        using var artistFont = ThemeFonts.Create(ThemeFonts.TextSemibold, Math.Max(ArtistPx * scale, 7f));
        using var dim = new SolidBrush(Color.FromArgb(150, 255, 255, 255));
        using var subtle = new SolidBrush(Color.FromArgb(108, 255, 255, 255));
        using var ink = new SolidBrush(Color.FromArgb(245, 255, 255, 255));
        ThemeFonts.DrawTracked(g, m.SourceText, sourceFont, subtle, x, m.SourceTop, Math.Max(SourcePx * scale, 7f) * 0.15f);
        ThemeFonts.DrawTracked(g, m.TitleText, titleFont, ink, x, m.TitleTop, 0f);
        if (m.ArtistText is not null)
            ThemeFonts.DrawTracked(g, m.ArtistText, artistFont, dim, x, m.ArtistTop, Math.Max(ArtistPx * scale, 7f) * 0.15f);

        // Equalizer column: real WASAPI-loopback spectrum when the engine supplies it,
        // a playful sine otherwise (previews and tests / no audio device).
        if (levels is not null) PaintEqualizer(g, m.EqBox, levels, r.IsPlaying);
        else PaintEqualizerSine(g, m.EqBox, eqPhase, scale, r.IsPlaying);

        // Transport buttons
        DrawPrevButton(g, m.PrevBox);
        DrawPlayButton(g, m.PlayBox, r.IsPlaying);
        DrawNextButton(g, m.NextBox);
    }

    private static string Fit(Graphics g, string text, Font font, float maxWidth, float tracking)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        if (ThemeFonts.MeasureTracked(g, text, font, tracking).Width <= maxWidth) return text;
        for (int len = text.Length - 1; len >= 1; len--)
        {
            string candidate = text[..len] + "…";
            if (ThemeFonts.MeasureTracked(g, candidate, font, tracking).Width <= maxWidth) return candidate;
        }
        return "…";
    }

    /// <summary>The real spectrum: one bar per band (12). Bars are filled solid white-ish with
    /// the current band's magnitude, so the shape dances with the music itself.</summary>
    private static void PaintEqualizer(Graphics g, Rectangle box, float[] bands, bool playing)
    {
        float gap = Math.Max(2f, box.Width / 56f);
        int n = bands.Length;
        float barW = (box.Width - gap * (n - 1)) / n;
        float maxH = box.Height;
        using var brush = new SolidBrush(Color.FromArgb(255, 255, 255, 255));
        for (int i = 0; i < n; i++)
        {
            float mag = Math.Clamp(playing ? bands[i] : 0f, 0f, 1f);
            float h = maxH * (0.10f + 0.90f * mag);
            float x = box.X + i * (barW + gap);
            brush.Color = Color.FromArgb((int)(140 + 115 * mag), 255, 255, 255);
            g.FillRectangle(brush, x, box.Bottom - h, barW, h);
        }
    }

    private static readonly float[] _flat = new float[12];

    private static void PaintEqualizerSine(Graphics g, Rectangle box, float phase, float scale, bool playing)
    {
        // Base-level (not audio-reactive) bars: each band is a sum of incommensurate
        // sines with slowly drifting phases, so they read as live audio without any
        // capture device or FFT. Settles low and still when paused.
        float amp = playing ? 1f : 0.16f;
        int bars = Math.Max(5, (int)(box.Width / Math.Max(3.4f * scale, 2.4f)));
        float gap = Math.Max(1.6f * scale, 1.2f);
        float barW = (box.Width - (bars - 1) * gap) / bars;
        float maxH = box.Height;
        for (int i = 0; i < bars; i++)
        {
            float band = amp * (0.55f
                + 0.28f * MathF.Sin(phase * 12.7f + i * 1.31f)
                + 0.17f * MathF.Sin(phase * 4.3f + i * 2.07f + 1.1f));
            float norm = Math.Clamp(band, 0.08f, 1f);
            float h = maxH * norm;
            float x = box.X + i * (barW + gap);
            using var brush = new SolidBrush(Color.FromArgb(245, 255, 255, 255));
            g.FillRectangle(brush, x, box.Bottom - h, barW, h);
        }
    }

    private static GraphicsPath RoundedRect(RectangleF rect, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static GraphicsPath RoundedRect(Rectangle rect, float radius) =>
        RoundedRect(new RectangleF(rect.X, rect.Y, rect.Width, rect.Height), radius);

    private static void DrawPrevButton(Graphics g, RectangleF box)
    {
        using var circle = new SolidBrush(Color.FromArgb(26, 255, 255, 255));
        g.FillEllipse(circle, box);
        float cx = box.X + box.Width / 2f, cy = box.Y + box.Height / 2f;
        float rad = box.Width * 0.26f;
        using var ink = new SolidBrush(Color.FromArgb(232, 255, 255, 255));
        g.FillRectangle(ink, cx - rad, cy - rad * 0.72f, rad * 0.28f, rad * 1.44f);
        var tri = new PointF[]
        {
            new(cx - rad * 0.34f, cy), new(cx + rad * 0.6f, cy - rad * 0.78f), new(cx + rad * 0.6f, cy + rad * 0.78f),
        };
        g.FillPolygon(ink, tri);
    }

    private static void DrawPlayButton(Graphics g, RectangleF box, bool playing)
    {
        using var circle = new SolidBrush(Color.FromArgb(240, 255, 255, 255));
        g.FillEllipse(circle, box);
        float cx = box.X + box.Width / 2f, cy = box.Y + box.Height / 2f;
        float rad = box.Width * 0.26f;
        var dark = new SolidBrush(Color.FromArgb(255, 26, 30, 39));
        if (playing)
        {
            g.FillRectangle(dark, cx - rad * 0.72f, cy - rad * 0.72f, rad * 0.46f, rad * 1.44f);
            g.FillRectangle(dark, cx + rad * 0.26f, cy - rad * 0.72f, rad * 0.46f, rad * 1.44f);
        }
        else
        {
            var tri = new PointF[]
            {
                new(cx - rad * 0.52f, cy - rad * 0.82f), new(cx - rad * 0.52f, cy + rad * 0.82f), new(cx + rad * 0.78f, cy),
            };
            g.FillPolygon(dark, tri);
        }
        dark.Dispose();
    }

    private static void DrawNextButton(Graphics g, RectangleF box)
    {
        using var circle = new SolidBrush(Color.FromArgb(26, 255, 255, 255));
        g.FillEllipse(circle, box);
        float cx = box.X + box.Width / 2f, cy = box.Y + box.Height / 2f;
        float rad = box.Width * 0.26f;
        using var ink = new SolidBrush(Color.FromArgb(232, 255, 255, 255));
        float right = cx + rad;
        g.FillRectangle(ink, right - rad * 0.28f, cy - rad * 0.72f, rad * 0.28f, rad * 1.44f);
        var tri = new PointF[]
        {
            new(cx + rad * 0.34f, cy), new(cx - rad * 0.6f, cy - rad * 0.78f), new(cx - rad * 0.6f, cy + rad * 0.78f),
        };
        g.FillPolygon(ink, tri);
    }
}
