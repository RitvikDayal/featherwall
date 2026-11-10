using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace FeatherWall.Desktop;

/// <summary>Keeps a FeatherWall entry in the user's own Start menu. Neither the release zip nor
/// winget's portable install creates one, so once the app was quit from the tray there was no
/// way back to it short of knowing where the exe lives. Written on every start and pointed at
/// whichever exe is running, so a moved folder or a different copy repairs it the next launch —
/// the same last-run-wins rule as <see cref="Autostart"/>.</summary>
public static class StartMenuShortcut
{
    private const uint SLGP_RAWPATH = 0x4;

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "FeatherWall.lnk");

    /// <summary>Writes the shortcut unless one already points at <paramref name="exePath"/>.
    /// Returns whether it wrote.</summary>
    public static bool Ensure(string exePath, string? lnkPath = null)
    {
        lnkPath ??= DefaultPath;
        if (string.Equals(ReadTarget(lnkPath), exePath, StringComparison.OrdinalIgnoreCase))
            return false;

        var link = (IShellLinkW)new ShellLink();
        try
        {
            link.SetPath(exePath);
            link.SetWorkingDirectory(Path.GetDirectoryName(exePath) ?? "");
            link.SetIconLocation(exePath, 0);
            link.SetDescription("Featherweight live wallpaper for Windows");
            Directory.CreateDirectory(Path.GetDirectoryName(lnkPath)!);
            ((IPersistFile)link).Save(lnkPath, fRemember: true);
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
        return true;
    }

    /// <summary>The exe a shortcut points at, or null when there is no shortcut there.</summary>
    public static string? ReadTarget(string lnkPath)
    {
        if (!File.Exists(lnkPath)) return null;

        var link = (IShellLinkW)new ShellLink();
        try
        {
            ((IPersistFile)link).Load(lnkPath, 0 /* STGM_READ */);
            var target = new StringBuilder(1024);
            link.GetPath(target, target.Capacity, IntPtr.Zero, SLGP_RAWPATH);
            return target.ToString();
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }
}

[ComImport, Guid("00021401-0000-0000-C000-000000000046")]
internal class ShellLink { }

// Declared in vtable order; only the setters and GetPath are called.
[ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cch, IntPtr pfd, uint fFlags);
    void GetIDList(out IntPtr ppidl);
    void SetIDList(IntPtr pidl);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
    void GetHotkey(out short pwHotkey);
    void SetHotkey(short wHotkey);
    void GetShowCmd(out int piShowCmd);
    void SetShowCmd(int iShowCmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cch, out int piIcon);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
    void Resolve(IntPtr hwnd, uint fFlags);
    void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
}
