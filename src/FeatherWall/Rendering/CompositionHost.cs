using FeatherWall.Common;
using FeatherWall.Interop;
using Vortice.Direct2D1;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DirectComposition;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace FeatherWall.Rendering;

/// <summary>D3D11 + DirectComposition presentation for one wallpaper window, holding a
/// small visual tree: a content surface (video/image) plus an optional overlay surface
/// (widgets) composed above it.
///
/// Why DirectComposition: on Windows 11 24H2+ "raised" desktops, Progman opts out of GDI
/// redirection (WS_EX_NOREDIRECTIONBITMAP) and the shell composes the wallpaper WorkerW via
/// the visual layer. Child-window redirection surfaces (GDI, blt-model presents, even
/// UpdateLayeredWindow content) are never composed there — verified empirically on build
/// 26200. Additionally, only render operations (Draw/Clear/video-processor blits) can write
/// flip-model backbuffers, so CPU bitmaps are drawn through Direct2D, and DWM composed only
/// the monitor-spanning window's target in testing — hence one hwnd with a visual tree
/// instead of one hwnd per element.</summary>
public sealed class CompositionHost : IDisposable
{
    public ID3D11Device Device { get; private set; } = null!;
    public ID3D11DeviceContext Context { get; private set; } = null!;

    private IDXGIFactory2 _dxgiFactory = null!;
    private ID2D1Factory _d2dFactory = null!;
    private IDCompositionDevice _dcompDevice = null!;
    private IDCompositionTarget _dcompTarget = null!;
    private IDCompositionVisual _rootVisual = null!;

    public CompositionSurface? Content { get; private set; }
    public CompositionSurface? Overlay { get; private set; }

    public CompositionHost(IntPtr hwnd)
    {
        D3D11.D3D11CreateDevice(null, DriverType.Hardware,
            DeviceCreationFlags.BgraSupport | DeviceCreationFlags.VideoSupport,
            null, out ID3D11Device? device).CheckError();
        Device = device!;
        Context = Device.ImmediateContext;

        using (var multithread = Context.QueryInterfaceOrNull<ID3D11Multithread>())
            multithread?.SetMultithreadProtected(true);

        using var dxgiDevice = Device.QueryInterface<IDXGIDevice>();
        using var adapter = dxgiDevice.GetAdapter();
        _dxgiFactory = adapter.GetParent<IDXGIFactory2>();
        _d2dFactory = D2D1.D2D1CreateFactory<ID2D1Factory>(FactoryType.MultiThreaded);

        _dcompDevice = DComp.DCompositionCreateDevice<IDCompositionDevice>(dxgiDevice);
        _dcompDevice.CreateTargetForHwnd(hwnd, true, out _dcompTarget);
        _rootVisual = _dcompDevice.CreateVisual();

        // DirectComposition composes this target in physical pixels, 1:1 with the window — DWM
        // applies no DPI scale to the visual tree. Everything downstream (window bounds, swapchain
        // sizes, widget offsets) is already physical, so the root transform stays identity.
        // Verified by covering the OS wallpaper with a solid colour and measuring what the live
        // layer actually paints: identity covers 100% of a 2560x1600 display at 150% scaling,
        // while a 96/dpi counter-scale leaves 55.7% of the screen bare.
        LogWindowDpi(hwnd);

        _dcompTarget.SetRoot(_rootVisual);
        _dcompDevice.Commit();
    }

    /// <summary>Recorded for bug reports: DPI scaling is where wallpaper layers usually go wrong.</summary>
    private static void LogWindowDpi(IntPtr hwnd)
    {
        try { Log.Info($"Composition: window DPI={User32.GetDpiForWindow(hwnd)}, visual tree composed 1:1 in physical pixels"); }
        catch { /* diagnostic only */ }
    }

    /// <summary>The main wallpaper surface (opaque). Recreatable for cover-crop layouts.</summary>
    public CompositionSurface CreateContent(int width, int height, int offsetX = 0, int offsetY = 0)
    {
        Content?.Dispose();
        Content = new CompositionSurface(this, width, height, premultipliedAlpha: false, offsetX, offsetY);
        _rootVisual.AddVisual(Content.Visual, false, Overlay?.Visual);
        _dcompDevice.Commit();
        return Content;
    }

    /// <summary>A transparent surface composed above the content (clock and friends).</summary>
    public CompositionSurface CreateOverlay(int width, int height, int offsetX, int offsetY)
    {
        Overlay?.Dispose();
        Overlay = new CompositionSurface(this, width, height, premultipliedAlpha: true, offsetX, offsetY);
        _rootVisual.AddVisual(Overlay.Visual, true, Content?.Visual);
        _dcompDevice.Commit();
        return Overlay;
    }

    public void RemoveOverlay()
    {
        if (Overlay is null) return;
        _rootVisual.RemoveVisual(Overlay.Visual);
        Overlay.Dispose();
        Overlay = null;
        _dcompDevice.Commit();
    }

    internal (IDXGIFactory2 Dxgi, ID2D1Factory D2d, IDCompositionDevice DComp) Factories =>
        (_dxgiFactory, _d2dFactory, _dcompDevice);

    internal void RemoveVisual(IDCompositionVisual visual) => _rootVisual.RemoveVisual(visual);

    internal void Commit() => _dcompDevice.Commit();

    public void Dispose()
    {
        Content?.Dispose();
        Content = null;
        Overlay?.Dispose();
        Overlay = null;
        _rootVisual?.Dispose();
        _dcompTarget?.Dispose();
        _dcompDevice?.Dispose();
        _d2dFactory?.Dispose();
        _dxgiFactory?.Dispose();
        Context?.Dispose();
        Device?.Dispose();
    }
}

/// <summary>One flip-model composition swapchain bound to a DComp visual, with a D2D
/// render target for CPU-bitmap content.</summary>
public sealed class CompositionSurface : IDisposable
{
    private readonly CompositionHost _host;
    private readonly bool _premultiplied;
    private ID2D1RenderTarget? _d2dTarget;

    public IDXGISwapChain1 SwapChain { get; private set; }
    public ID3D11Texture2D BackBuffer { get; private set; }
    public ID3D11RenderTargetView Rtv { get; private set; }
    public IDCompositionVisual Visual { get; }

    public int Width { get; private set; }
    public int Height { get; private set; }

    internal CompositionSurface(CompositionHost host, int width, int height, bool premultipliedAlpha, int offsetX, int offsetY)
    {
        _host = host;
        _premultiplied = premultipliedAlpha;
        Width = Math.Max(width, 1);
        Height = Math.Max(height, 1);

        SwapChain = CreateSwapChain();
        BackBuffer = SwapChain.GetBuffer<ID3D11Texture2D>(0);
        Rtv = host.Device.CreateRenderTargetView(BackBuffer);

        Visual = host.Factories.DComp.CreateVisual();
        Visual.SetContent(SwapChain);
        Visual.SetOffsetX(offsetX);
        Visual.SetOffsetY(offsetY);
    }

    private IDXGISwapChain1 CreateSwapChain() =>
        _host.Factories.Dxgi.CreateSwapChainForComposition(_host.Device, new SwapChainDescription1
        {
            Width = (uint)Width,
            Height = (uint)Height,
            Format = Format.B8G8R8A8_UNorm,
            BufferCount = 2,
            BufferUsage = Usage.RenderTargetOutput,
            SampleDescription = new SampleDescription(1, 0),
            SwapEffect = SwapEffect.FlipDiscard,
            Scaling = Scaling.Stretch,
            AlphaMode = _premultiplied ? AlphaMode.Premultiplied : AlphaMode.Ignore,
        });

    public void SetOffset(int x, int y)
    {
        Visual.SetOffsetX(x);
        Visual.SetOffsetY(y);
        _host.Commit();
    }

    public void ClearBlack() => _host.Context.ClearRenderTargetView(Rtv, new Color4(0f, 0f, 0f, 1f));

    public void Present()
    {
        var result = SwapChain.Present(0, PresentFlags.None);
        if (result.Code == Vortice.DXGI.ResultCode.DeviceRemoved.Code ||
            result.Code == Vortice.DXGI.ResultCode.DeviceReset.Code)
        {
            // GPU reset / driver update. The whole host owns the device; rebuilding just this
            // swapchain against the (also dead) device won't help — the engine's re-apply
            // path rebuilds everything. Log loudly.
            Log.Warn($"Present reported device loss (0x{result.Code:X8}) — wallpaper re-apply required");
            DeviceLost?.Invoke();
        }
    }

    public event Action? DeviceLost;

    /// <summary>Draws a premultiplied-BGRA bitmap over the whole surface via Direct2D.
    /// (Flip-model backbuffers only accept render operations — CPU copies are ignored.)</summary>
    public void PresentBitmap(System.Drawing.Bitmap bitmap)
    {
        if (_d2dTarget is null)
        {
            using var dxgiSurface = BackBuffer.QueryInterface<IDXGISurface>();
            _d2dTarget = _host.Factories.D2d.CreateDxgiSurfaceRenderTarget(dxgiSurface, new RenderTargetProperties(
                new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied)));
        }

        var bits = bitmap.LockBits(
            new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        try
        {
            using var d2dBitmap = _d2dTarget.CreateBitmap(
                new SizeI(bitmap.Width, bitmap.Height), bits.Scan0, (uint)bits.Stride,
                new BitmapProperties(new Vortice.DCommon.PixelFormat(Format.B8G8R8A8_UNorm, Vortice.DCommon.AlphaMode.Premultiplied)));
            _d2dTarget.BeginDraw();
            _d2dTarget.Clear(new Color4(0f, 0f, 0f, 0f));
            _d2dTarget.DrawBitmap(d2dBitmap, new Rect(0, 0, Width, Height), 1f, Vortice.Direct2D1.BitmapInterpolationMode.Linear, null);
            _d2dTarget.EndDraw();
        }
        finally
        {
            bitmap.UnlockBits(bits);
        }
        Present();
    }

    /// <summary>Reads back a region of the current backbuffer to a PNG — used to snapshot a
    /// frame of the live wallpaper for the static desktop-switch fallback. Call before Present
    /// (flip-model backbuffer contents are undefined afterward).</summary>
    public void SaveRegionPng(int cropX, int cropY, int w, int h, string path)
    {
        cropX = Math.Clamp(cropX, 0, Math.Max(Width - 1, 0));
        cropY = Math.Clamp(cropY, 0, Math.Max(Height - 1, 0));
        w = Math.Min(w, Width - cropX);
        h = Math.Min(h, Height - cropY);
        if (w <= 0 || h <= 0) return;

        var desc = new Texture2DDescription
        {
            Width = (uint)w,
            Height = (uint)h,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CPUAccessFlags = CpuAccessFlags.Read,
        };
        using var staging = _host.Device.CreateTexture2D(desc);
        var box = new Box(cropX, cropY, 0, cropX + w, cropY + h, 1);
        _host.Context.CopySubresourceRegion(staging, 0, 0, 0, 0, BackBuffer, 0, box);

        var map = _host.Context.Map(staging, 0, MapMode.Read, Vortice.Direct3D11.MapFlags.None);
        try
        {
            using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var bits = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                unsafe
                {
                    byte* src = (byte*)map.DataPointer;
                    byte* dst = (byte*)bits.Scan0;
                    for (int y = 0; y < h; y++)
                        Buffer.MemoryCopy(src + (long)y * map.RowPitch, dst + (long)y * bits.Stride, w * 4, w * 4);
                }
            }
            finally
            {
                bmp.UnlockBits(bits);
            }
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
            bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        }
        finally
        {
            _host.Context.Unmap(staging, 0);
        }
    }

    public void Dispose()
    {
        _d2dTarget?.Dispose();
        _d2dTarget = null;
        try { _host.RemoveVisual(Visual); } catch { /* host may be tearing down */ }
        Visual.Dispose();
        Rtv.Dispose();
        BackBuffer.Dispose();
        SwapChain.Dispose();
    }
}
