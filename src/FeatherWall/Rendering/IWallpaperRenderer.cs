namespace FeatherWall.Rendering;

public interface IWallpaperRenderer : IDisposable
{
    void Load(string path);
    void Pause();
    void Resume();
    /// <summary>GDI paint path (WM_PAINT). D3D-backed renderers ignore this.</summary>
    void Paint(IntPtr hdc);
}
