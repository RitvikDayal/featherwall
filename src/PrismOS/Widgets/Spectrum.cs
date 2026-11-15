namespace PrismOS.Widgets;

/// <summary>Pure DSP for the live audio meter: a Hann window and a radix-2 FFT,
/// a 12-band log-frequency power-to-magnitude map with attack/release envelope.
/// Nothing here touches hardware or COM, so the math is unit-testable headless.</summary>
public static class Spectrum
{
    public const int FftSize = 1024;
    public const int BandCount = 12;

    /// <summary>Log-spaced band edges, Hz. Chosen so the low bands (kick/bass) get
    /// more resolution and the high bands behave.</summary>
    public static readonly float[] BandEdges =
        { 40f, 80f, 130f, 210f, 340f, 550f, 850f, 1300f, 2100f, 3400f, 5500f, 9000f, 20000f };

    /// <summary>Per-band fixed sensitivity floor, the analogue of Windhawk's BAND_SENSITIVITY:
    /// high frequencies need a push to read as loud, lows don't.</summary>
    private static readonly float[] BandFloor =
        { 0.30f, 0.24f, 0.16f, 0.11f, 0.075f, 0.05f, 0.032f, 0.022f, 0.014f, 0.009f, 0.006f, 0.004f };

    /// <summary>Attack multiplier (instant) and gravity/decay per processed frame.</summary>
    private static readonly float[] Gravity =
        { 0.045f, 0.040f, 0.036f, 0.032f, 0.030f, 0.028f, 0.027f, 0.026f, 0.026f, 0.025f, 0.024f, 0.023f };

    static readonly float[] _hann = BuildHann();

    static float[] BuildHann()
    {
        var w = new float[FftSize];
        for (int i = 0; i < FftSize; i++)
            w[i] = 0.5f * (1f - MathF.Cos(2f * MathF.PI * i / (FftSize - 1)));
        return w;
    }

    /// <summary>Input: time-domain mono samples (Hann-windowed copy into re/im); output:
    /// magnitudes in each of the 12 bands, normalised 0..1 with an envelope applied.
    /// The envelope state (one float per band) is carried by the caller.</summary>
    public static void Magnitudes(float[] timeDomain, float sampleRate, float[] bands, float sensitivity)
    {
        var re = new float[FftSize];
        var im = new float[FftSize];
        for (int i = 0; i < FftSize; i++)
        {
            re[i] = timeDomain[i] * _hann[i];
            im[i] = 0f;
        }
        Fft(re, im);

        // Convert UI slider (0..3) into a linear gain. Same shape as Windhawk's:
        // 1.0 = neutral, silent stays silent, loud stays loud, middle boosted.
        float t = sensitivity;
        float gain = t <= 1f ? 0.25f + t * t * 2.75f : 3f + (t - 1f) * 4f;

        for (int b = 0; b < BandCount; b++)
        {
            int start = Math.Clamp((int)Math.Floor(BandEdges[b] * FftSize / sampleRate), 1, FftSize / 2 - 1);
            int end = Math.Clamp((int)Math.Floor(BandEdges[b + 1] * FftSize / sampleRate), start + 1, FftSize / 2);

            double sumSq = 0;
            int count = 0;
            for (int k = start; k < end; k++)
            {
                sumSq += re[k] * (double)re[k] + im[k] * (double)im[k];
                count++;
            }
            float rms = count > 0 ? MathF.Sqrt((float)(sumSq / count)) : 0f;

            // Normalise: scale by FftSize/2 (a full-scale sine fills re[] near FftSize/2).
            float mag = Math.Min(1f, Math.Max(0f,
                (rms / (FftSize * 0.5f)) / BandFloor[b] * gain));

            // Fast attack / slow release, carried in the bands array itself.
            float current = bands[b];
            bands[b] = mag >= current ? mag : Math.Max(0f, current - Gravity[b]);
        }
    }

    /// <summary>In-place iterative radix-2 Cooley–Tukey FFT. Sizes must be powers of two
    /// and match the arrays.</summary>
    public static void Fft(float[] re, float[] im)
    {
        int n = re.Length;
        // bit-reversal permutation
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            int halfLen = len / 2;
            float angStep = -2f * MathF.PI / len;
            for (int i = 0; i < n; i += len)
            {
                float wr = 1f, wi = 0f;
                float wpr = MathF.Cos(angStep), wpi = MathF.Sin(angStep);
                for (int j = 0; j < halfLen; j++)
                {
                    int u = i + j, v = i + j + halfLen;
                    float vr = re[v] * wr - im[v] * wi;
                    float vi = re[v] * wi + im[v] * wr;
                    re[v] = re[u] - vr;
                    im[v] = im[u] - vi;
                    re[u] += vr;
                    im[u] += vi;
                    float nwr = wr * wpr - wi * wpi;
                    wi = wr * wpi + wi * wpr;
                    wr = nwr;
                }
            }
        }
    }
}
