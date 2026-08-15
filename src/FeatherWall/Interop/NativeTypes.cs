using System.Runtime.InteropServices;

namespace FeatherWall.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct POINT
{
    public int X;
    public int Y;
}

[StructLayout(LayoutKind.Sequential)]
public struct SIZE
{
    public int Cx;
    public int Cy;
}

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public readonly int Width => Right - Left;
    public readonly int Height => Bottom - Top;

    public RECT(int left, int top, int right, int bottom)
    {
        Left = left; Top = top; Right = right; Bottom = bottom;
    }

    public readonly bool IntersectsWith(in RECT other) =>
        Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;

    public readonly long IntersectionArea(in RECT other)
    {
        long w = Math.Min(Right, other.Right) - Math.Max(Left, other.Left);
        long h = Math.Min(Bottom, other.Bottom) - Math.Max(Top, other.Top);
        return w <= 0 || h <= 0 ? 0 : w * h;
    }

    public readonly long Area => (long)Width * Height;

    public override readonly string ToString() => $"({Left},{Top})-({Right},{Bottom}) {Width}x{Height}";
}

[StructLayout(LayoutKind.Sequential)]
public struct MSG
{
    public IntPtr Hwnd;
    public uint Message;
    public IntPtr WParam;
    public IntPtr LParam;
    public uint Time;
    public POINT Pt;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct WNDCLASSEX
{
    public uint Size;
    public uint Style;
    public IntPtr WndProc;
    public int ClsExtra;
    public int WndExtra;
    public IntPtr Instance;
    public IntPtr Icon;
    public IntPtr Cursor;
    public IntPtr Background;
    [MarshalAs(UnmanagedType.LPWStr)] public string? MenuName;
    [MarshalAs(UnmanagedType.LPWStr)] public string ClassName;
    public IntPtr IconSm;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct MONITORINFOEX
{
    public uint Size;
    public RECT Monitor;
    public RECT Work;
    public uint Flags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
}

[StructLayout(LayoutKind.Sequential)]
public struct BLENDFUNCTION
{
    public byte BlendOp;
    public byte BlendFlags;
    public byte SourceConstantAlpha;
    public byte AlphaFormat;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct NOTIFYICONDATA
{
    public uint Size;
    public IntPtr Hwnd;
    public uint Id;
    public uint Flags;
    public uint CallbackMessage;
    public IntPtr Icon;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
    public uint State;
    public uint StateMask;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
    public uint TimeoutOrVersion;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
    public uint InfoFlags;
    public Guid GuidItem;
    public IntPtr BalloonIcon;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
public struct OPENFILENAME
{
    public uint StructSize;
    public IntPtr HwndOwner;
    public IntPtr Instance;
    [MarshalAs(UnmanagedType.LPWStr)] public string? Filter;
    [MarshalAs(UnmanagedType.LPWStr)] public string? CustomFilter;
    public uint MaxCustFilter;
    public uint FilterIndex;
    public IntPtr File;
    public uint MaxFile;
    [MarshalAs(UnmanagedType.LPWStr)] public string? FileTitle;
    public uint MaxFileTitle;
    [MarshalAs(UnmanagedType.LPWStr)] public string? InitialDir;
    [MarshalAs(UnmanagedType.LPWStr)] public string? Title;
    public uint Flags;
    public ushort FileOffset;
    public ushort FileExtension;
    [MarshalAs(UnmanagedType.LPWStr)] public string? DefExt;
    public IntPtr CustData;
    public IntPtr Hook;
    [MarshalAs(UnmanagedType.LPWStr)] public string? TemplateName;
    public IntPtr Reserved0;
    public uint Reserved1;
    public uint FlagsEx;
}

[StructLayout(LayoutKind.Sequential)]
public struct SYSTEM_POWER_STATUS
{
    public byte ACLineStatus;
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte SystemStatusFlag; // 1 = battery saver on
    public uint BatteryLifeTime;
    public uint BatteryFullLifeTime;
}
