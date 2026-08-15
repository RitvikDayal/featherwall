using System.Runtime.InteropServices;
using Windows.Graphics.DirectX.Direct3D11;

namespace FeatherWall.Interop;

/// <summary>Bridges a DXGI surface (our swapchain backbuffer) to the WinRT
/// IDirect3DSurface that MediaPlayer.CopyFrameToVideoSurface expects.</summary>
public static class Direct3DInterop
{
    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11SurfaceFromDXGISurface", ExactSpelling = true)]
    private static extern int CreateDirect3D11SurfaceFromDXGISurface(IntPtr dxgiSurface, out IntPtr inspectable);

    public static IDirect3DSurface CreateSurfaceFromDxgi(IntPtr dxgiSurfacePtr)
    {
        int hr = CreateDirect3D11SurfaceFromDXGISurface(dxgiSurfacePtr, out IntPtr inspectable);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            return WinRT.MarshalInterface<IDirect3DSurface>.FromAbi(inspectable);
        }
        finally
        {
            Marshal.Release(inspectable);
        }
    }
}
