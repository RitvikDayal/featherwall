using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace FeatherWall.Desktop;

/// <summary>Keeps a FeatherWall entry in the user's own Start menu. Neither the release zip nor
/// winget's portable install creates one, so once the app was quit from the tray there was no
/// way back to it short of knowing where the exe lives.
///
/// Created once, on the first run. After that it is only kept pointing at whichever exe is
/// running, so a moved folder or a different copy repairs it the next launch — but an entry the
/// user deleted stays deleted. The first version recreated it on every start, which overrode
/// that choice at every sign-in.</summary>
public static class StartMenuShortcut
{
    private const uint SLGP_RAWPATH = 0x4;

    /// <summary>The startup step: brings the shortcut in line and says whether the config needs
    /// saving because the flag was just set. Does nothing at all when the config could not be read
    /// — the flag is then a default rather than the user's, and saving would write defaults over a
    /// file they can still fix.</summary>
    public static (ShortcutChange Change, bool MarkAdded) Reconcile(string exePath, Config.AppConfig config,
                                                                    bool configUnreadable, string? lnkPath = null)
    {
        if (configUnreadable) return (ShortcutChange.None, false);
        lnkPath ??= DefaultPath;
        var change = Ensure(exePath, config.StartMenuShortcutAdded, lnkPath);
        bool markAdded = !config.StartMenuShortcutAdded && File.Exists(lnkPath);
        if (markAdded) config.StartMenuShortcutAdded = true;
        return (change, markAdded);
    }

    public static string DefaultPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "FeatherWall.lnk");

    /// <summary>Only featherwall.exe gets a shortcut. Run as <c>dotnet featherwall.dll</c>, the
    /// process path is dotnet.exe, and a "FeatherWall" entry would launch that with no arguments.</summary>
    public static bool IsOwnExe(string path) =>
        string.Equals(Path.GetFileName(path), "featherwall.exe", StringComparison.OrdinalIgnoreCase);

    /// <summary>Creates the shortcut if it has never been added, repoints one that points
    /// elsewhere or cannot be read, and otherwise leaves things alone — including a shortcut the
    /// user has deleted after <paramref name="alreadyAdded"/> was recorded.</summary>
    public static ShortcutChange Ensure(string exePath, bool alreadyAdded, string? lnkPath = null)
    {
        lnkPath ??= DefaultPath;
        if (!File.Exists(lnkPath))
        {
            if (alreadyAdded) return ShortcutChange.None;
            Write(exePath, lnkPath);
            return ShortcutChange.Created;
        }

        string? existing;
        try { existing = ReadTarget(lnkPath); }
        catch (COMException) { existing = null; } // corrupt .lnk: rewrite it rather than warn forever

        if (string.Equals(existing, exePath, StringComparison.OrdinalIgnoreCase))
            return ShortcutChange.None;
        Write(exePath, lnkPath);
        return ShortcutChange.Repointed;
    }

    private static void Write(string exePath, string lnkPath)
    {
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

/// <summary>What <see cref="StartMenuShortcut.Ensure"/> did.</summary>
public enum ShortcutChange { None, Created, Repointed }
