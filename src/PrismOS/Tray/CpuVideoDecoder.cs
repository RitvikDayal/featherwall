using System.Runtime.InteropServices;
using PrismOS.Common;
using Vortice.MediaFoundation;

namespace PrismOS.Tray;

/// <summary>CPU-decoded, looping video for the settings panel preview.
///
/// Decoded entirely through the software Media Foundation path: MF is started, the source
/// reader is told to bypass DXVA (SourceReaderAttributeKeys.DisableDxva) and to insert its
/// software video processor so the H.264/HEVC payloads go to the CPU, and frames arrive as
/// RGB32 system memory. No D3D device, no DirectComposition visuals, nothing associates the
/// process with the GPU — which is the only reliable way to keep the NVIDIA App overlay from
/// flagging the preview.
///
/// The decode runs on a background thread at native size (the software video processor
/// refuses scaled requests outright); frames reach the panel as RGB32 bitmaps and GDI+
/// downscales them on paint. The UI thread never sees a Media Foundation call.</summary>
public sealed class CpuVideoDecoder : IDisposable
{
    private readonly string _path;
    private readonly Thread _thread;
    private readonly CancellationTokenSource _stop = new();
    private volatile bool _paused;
    private readonly Lock _gate = new();

    private Bitmap? _frame;
    private int _width = -1, _height = -1;
    private bool _started;

    public event Action? FrameReady;

    /// <summary>Set when the file cannot be opened or decoded at all — the UI shows the message
    /// instead of a black hole in the panel.</summary>
    public string? Error { get; private set; }

    public CpuVideoDecoder(string path)
    {
        _path = path;
        _thread = new Thread(DecodeLoop) { IsBackground = true, Name = "CpuVideoPreview" };
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_started) return;
            _started = true;
        }
        _thread.Start();
    }

    public void Pause() => _paused = true;
    public void Resume() => _paused = false;

    /// <summary>A private copy of the latest frame, or null before the first one lands.
    /// The caller owns the copy — the decode thread swaps and disposes frames as it goes,
    /// so handing out the internal bitmap would let it be disposed mid-draw.</summary>
    public Bitmap? GetFrame()
    {
        var current = Volatile.Read(ref _frame);
        return current is null ? null : new Bitmap(current);
    }

    private void DecodeLoop()
    {
        try
        {
            MediaFactory.MFStartup(useLightVersion: false);
            using var reader = OpenReader(_path);
            if (reader is null) return; // Error already set

            while (!_stop.IsCancellationRequested)
            {
                if (_paused) { Thread.Sleep(50); continue; }

                var sample = reader.ReadSample((int)SourceReaderIndex.FirstVideoStream,
                    SourceReaderControlFlag.None, out _, out var flags, out _);

                if ((flags & SourceReaderFlag.EndOfStream) != 0)
                {
                    // Loop forever: go back to the start and keep going.
                    reader.SetCurrentPosition(0);
                    continue;
                }
                if (sample is null) continue;

                long durationHns = sample.SampleDuration;
                using (sample)
                {
                    var bitmap = ToBitmap(sample);
                    if (bitmap is not null)
                    {
                        var previous = Interlocked.Exchange(ref _frame, bitmap);
                        previous?.Dispose();
                        FrameReady?.Invoke();
                    }
                }

                int waitMs = (int)(durationHns / 10_000); // 100-ns units → ms
                if (waitMs > 0) Thread.Sleep(Math.Min(waitMs, 250));
            }
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Log.Warn($"CPU video preview failed for {_path}: {ex.Message}");
            FrameReady?.Invoke();
        }
    }

    /// <summary>Opens the file as an RGB32 software-decoded source, scaled down for the panel.
    /// Returns null (with <see cref="Error"/> set) for anything unsupported.</summary>
    private IMFSourceReader? OpenReader(string path)
    {
        using var attributes = MediaFactory.MFCreateAttributes(4);
        attributes.Set(SourceReaderAttributeKeys.DisableDxva, true);
        attributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing, true);

        var reader = MediaFactory.MFCreateSourceReaderFromURL(path, attributes);
        var native = reader.GetNativeMediaType((int)SourceReaderIndex.FirstVideoStream, 0);
        ulong packedSize = native.GetUInt64(MediaTypeAttributeKeys.FrameSize);
        uint width = (uint)(packedSize >> 32);
        uint height = (uint)(packedSize & 0xFFFFFFFF);
        native.Dispose();

        // The software video processor refuses every scaled request on this machine
        // (MF_E_INVALIDMEDIATYPE for any frame size but the native one), so the preview
        // decodes at native size and GDI+ downscales on paint.
        _width = (int)width;
        _height = (int)height;

        using var outType = MediaFactory.MFCreateMediaType();
        outType.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        outType.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
        outType.Set(MediaTypeAttributeKeys.FrameSize, MediaFactory.PackSize((uint)_width, (uint)_height));
        outType.Set(MediaTypeAttributeKeys.FrameRate, MediaFactory.PackRatio(30, 1));
        outType.Set(MediaTypeAttributeKeys.DefaultStride, (ulong)(_width * 4));
        reader.SetCurrentMediaType((int)SourceReaderIndex.FirstVideoStream, outType);
        // The preview is muted; do not spend cycles on the audio path.
        reader.SetStreamSelection((int)SourceReaderIndex.FirstAudioStream, false);
        return reader;
    }

    /// <summary>Copies the contiguous RGB32 sample into a Bitmap. RGB32 in memory is B,G,R,X —
    /// the layout GDI+ calls Format32bppRgb.</summary>
    private Bitmap? ToBitmap(IMFSample sample)
    {
        if (_width <= 0 || _height <= 0) return null;

        using var buffer = sample.ConvertToContiguousBuffer();
        IntPtr data = IntPtr.Zero;
        int currentLength = 0;
        try
        {
            buffer.Lock(out data, out _, out currentLength);
        }
        catch (Exception ex)
        {
            Log.Warn($"CPU video sample could not be locked: {ex.Message}");
            return null;
        }

        int width = _width, height = _height;
        int srcStride = width * 4;
        if (currentLength < (long)srcStride * height) { buffer.Unlock(); return null; }

        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        var rect = new Rectangle(0, 0, width, height);
        var dest = bitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.WriteOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppRgb);
        try
        {
            unsafe
            {
                byte* src = (byte*)data.ToPointer();
                byte* dst = (byte*)dest.Scan0.ToPointer();
                for (int y = 0; y < height; y++)
                    Buffer.MemoryCopy(src + (long)y * srcStride, dst + (long)y * dest.Stride, dest.Stride, srcStride);
            }
        }
        finally
        {
            bitmap.UnlockBits(dest);
            buffer.Unlock();
        }
        return bitmap;
    }

    public void Dispose()
    {
        _stop.Cancel();
        try { _thread.Join(1500); } catch { /* best effort */ }
        var leftover = Interlocked.Exchange(ref _frame, null);
        leftover?.Dispose();
        _stop.Dispose();
    }
}
