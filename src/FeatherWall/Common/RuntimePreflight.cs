using System.Runtime.InteropServices;

namespace FeatherWall.Common;

/// <summary>Checks the .NET **desktop** framework is present before anything needs it.
///
/// Scoped deliberately narrowly, because the obvious version of this is already handled.
/// Measured 2026-08-20: with no runtime at all, the .NET app host fails before any managed
/// code runs and prints its own `aka.ms/dotnet-core-applaunch` download link. Nothing here can
/// improve on that, and claiming to would be false.
///
/// What is left is the real gap: a machine with the **base** runtime but not the **Windows
/// Desktop** framework — the shape you get by installing the ASP.NET Core or console runtime
/// alone. The app host is satisfied, FeatherWall starts, and then WinForms fails somewhere
/// further in, at the tray or the settings panel, with an error that names none of this.
/// FeatherWall is framework-dependent on purpose (it is what holds the download to 7.5 MB), so
/// this failure mode belongs to the wedge rather than to a build flag.</summary>
public static class RuntimePreflight
{
    public const string DownloadUrl = "https://dotnet.microsoft.com/download/dotnet/10.0";

    /// <summary>Null when everything needed is present, otherwise a message to show the user.</summary>
    public static string? Check()
    {
        if (DesktopFrameworkPresent()) return null;

        return "FeatherWall needs the .NET 10 **desktop** runtime.\n\n" +
               "The base .NET runtime is installed on this machine, but the Windows Desktop " +
               "framework is not, and FeatherWall's tray icon and settings panel need it.\n\n" +
               "Get \"" + ".NET Desktop Runtime\" from:\n" + DownloadUrl + "\n\n" +
               "(FeatherWall ships framework-dependent on purpose — that is what keeps the download to 7.5 MB.)";
    }

    /// <summary>Probes the filesystem for the Windows Desktop shared framework.
    ///
    /// Deliberately NOT `typeof(System.Windows.Forms.Form)` in a try/catch, which is the obvious
    /// version and does not work: a type reference makes the JIT load the assembly when the
    /// enclosing method is compiled, so the failure happens *before* the try block is entered
    /// and the catch never runs. A preflight that crashes on exactly the case it exists to
    /// detect is worse than no preflight.
    ///
    /// Conservative by construction: every uncertain answer is "present", so a probe that cannot
    /// find the dotnet root never blocks a working install. It only says no when it positively
    /// found a runtime directory with no desktop framework in it.</summary>
    private static bool DesktopFrameworkPresent()
    {
        try
        {
            string? root = DotnetRoot();
            if (root is null) return true; // cannot tell — assume fine

            string shared = Path.Combine(root, "shared", "Microsoft.WindowsDesktop.App");
            if (!Directory.Exists(shared)) return false;

            int required = Environment.Version.Major;
            return Directory.EnumerateDirectories(shared)
                .Select(d => Path.GetFileName(d))
                .Any(name => int.TryParse(name.Split('.').FirstOrDefault(), out int major) && major >= required);
        }
        catch (Exception ex)
        {
            Log.Warn($"Runtime preflight inconclusive, continuing ({ex.GetType().Name}: {ex.Message})");
            return true;
        }
    }

    /// <summary>DOTNET_ROOT wins, then the directory the host itself was loaded from.</summary>
    private static string? DotnetRoot()
    {
        if (Environment.GetEnvironmentVariable("DOTNET_ROOT") is { Length: > 0 } explicitRoot && Directory.Exists(explicitRoot))
            return explicitRoot;

        // RuntimeEnvironment gives .../shared/Microsoft.NETCore.App/<ver>/ — walk up to the root.
        var dir = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
        for (int i = 0; i < 3 && dir?.Parent is not null; i++) dir = dir.Parent;
        return dir?.Exists == true ? dir.FullName : null;
    }

    /// <summary>MessageBox without touching WinForms, since the whole point is that WinForms may
    /// be the thing that is missing.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    public static void Report(string message) => MessageBoxW(IntPtr.Zero, message, "FeatherWall", 0x10 /* MB_ICONERROR */);
}
