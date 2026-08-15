using FeatherWall.Config;

namespace FeatherWall.Rendering;

public readonly record struct FitRect(int X, int Y, int Width, int Height);

public static class FitCalculator
{
    /// <summary>Destination rectangle for drawing a src-sized frame into a dst-sized surface.
    /// Fill = cover (crop overflow, may have negative origin), Fit = letterbox, Stretch = distort.</summary>
    public static FitRect Compute(int srcW, int srcH, int dstW, int dstH, FitMode mode)
    {
        if (srcW <= 0 || srcH <= 0 || mode == FitMode.Stretch)
            return new FitRect(0, 0, dstW, dstH);

        double scale = mode == FitMode.Fill
            ? Math.Max((double)dstW / srcW, (double)dstH / srcH)
            : Math.Min((double)dstW / srcW, (double)dstH / srcH);

        int w = (int)Math.Round(srcW * scale);
        int h = (int)Math.Round(srcH * scale);
        return new FitRect((dstW - w) / 2, (dstH - h) / 2, w, h);
    }
}
