namespace PrismOS.Rendering;

public interface IWallpaperRenderer : IDisposable
{
    void Load(string path);
    void Pause();
    void Resume();
    /// <summary>GDI paint path (WM_PAINT). D3D-backed renderers ignore this.</summary>
    void Paint(IntPtr hdc);

    /// <summary>Lock: ensure the on-screen frame is frame 0 and snap the wallpaper to the
    /// frozen wake state (slightly scaled-in, slightly dimmed). No-op when no wake transition is requested.</summary>
    void FreezeFrameAtZero();

    /// <summary>Unlock: ease the frozen state back to a neutral scale and full brightness over
    /// ~1.4 s, and for video wallpapers start playback at 0.2x then ramp to 1.0x over ~650 ms.</summary>
    void WakeTransition();
}

