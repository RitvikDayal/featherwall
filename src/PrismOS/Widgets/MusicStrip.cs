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
        Rectangle ArtBox, Rectangle PrevBox, Rectangle PlayBox, Rectangle NextBox)
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

        // The pill never resizes with the title: it clips into a fixed column like the
        // Windows 11 media widget, so the panel does not jitter as tracks change. On a
        // capped monitor the column shrinks, not the buttons or the art.
        float fixedCols = pad + art + ArtGapPx * scale + TextButtonGapPx * scale + ButtonPx * scale * 3 + ButtonGapPx * scale * 2 + ButtonsRightPx * scale + pad;
        float textWidth = Math.Min(MaxTextWidthPx * scale, Math.Max(maxWidth - fixedCols, 48f * scale));

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
        float buttonsW = btn * (ButtonCount - 1) + gap * (ButtonCount - 1) + btn;
        float total = secX + textWidth + TextButtonGapPx * scale + buttonsW + ButtonsRightPx * scale;

        float btnY = (height - btn) / 2f;
        float prevX = secX + textWidth + TextButtonGapPx * scale;
        float playX = prevX + btn + gap;
        float nextX = playX + btn + gap;

        return new Metrics(
            new Size((int)Math.Ceiling(total), (int)Math.Ceiling(height)),
            (int)Math.Ceiling(pad), (int)Math.Ceiling(art), (int)Math.Ceiling(ArtGapPx * scale),
            (int)srcY, (int)titleY, (int)artistY,
            sourceText, titleText, artistText,
            new Rectangle((int)pad, (int)((height - art) / 2f), (int)art, (int)art),
            new Rectangle((int)prevX, (int)btnY, (int)btn, (int)btn),
            new Rectangle((int)playX, (int)btnY, (int)btn, (int)btn),
            new Rectangle((int)nextX, (int)btnY, (int)btn, (int)btn));
    }

    public static void Paint(Graphics g, in Metrics m, NowPlayingReading r, Bitmap? art, float scale, float eqPhase)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        // Card
        using var path = RoundedRect(new RectangleF(0, 0, m.Size.Width, m.Size.Height), Height * scale * 0.30f);
        using (var brush = new SolidBrush(Color.FromArgb(216, 26, 30, 39)))
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
            using (var brush = new SolidBrush(Color.FromArgb(255, 44, 50, 63)))
                g.FillPath(brush, artPath);
            using var glyph = new SolidBrush(Color.FromArgb(150, 255, 255, 255));
            float cx = m.ArtBox.X + m.ArtBox.Width / 2f, cy = m.ArtBox.Y + m.ArtBox.Height / 2f;
            float rad = m.ArtBox.Width * 0.22f;
            var tri = new PointF[]
            {
                new(cx - rad * 0.7f, cy - rad), new(cx - rad * 0.7f, cy + rad), new(cx + rad, cy),
            };
            g.FillPolygon(glyph, tri);
        }

        // Equalizer bars over the art while something is playing
        if (r.IsPlaying) PaintEqualizer(g, m.ArtBox, eqPhase, scale);

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

    private static void PaintEqualizer(Graphics g, Rectangle artBox, float phase, float scale)
    {
        float barW = Math.Max(2.4f * scale, 1.6f), gap = Math.Max(1.8f * scale, 1.2f);
        const int bars = 4;
        float totalW = bars * barW + (bars - 1) * gap;
        float x = artBox.X + (artBox.Width - totalW) / 2f;
        float baseY = artBox.Bottom - 5f * scale;
        float maxH = artBox.Height * 0.52f;
        using var brush = new SolidBrush(Color.FromArgb(245, 255, 255, 255));
        for (int i = 0; i < bars; i++)
        {
            float t = phase * MathF.PI * 2f + i * 1.7f;
            float norm = 0.28f + 0.72f * (0.5f + 0.5f * MathF.Sin(t * 2.3f + i) * MathF.Sin(t * 1.1f + 0.6f));
            float h = maxH * norm;
            g.FillRectangle(brush, x + i * (barW + gap), baseY - h, barW, h);
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
