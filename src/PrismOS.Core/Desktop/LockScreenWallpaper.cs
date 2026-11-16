using System.Drawing.Imaging;
using Microsoft.Win32;

namespace PrismOS.Desktop;

/// <summary>The lock screen is a static image that Windows paints from a policy key. Live
/// wallpapers can use the video's captured still frame instead — the same frame the desktop
/// falls back to.
///
/// The only supported way to actually set it on any current Windows 10/11 build is the
/// `HKLM Personalization CSP` policy keys, which standard users can't write. We attempt the
/// write on every apply (admin once, then the image sticks); on refusal we ask once per launch
/// and the user can click the balloon to elevate a helper for that single write.</summary>
public static class LockScreenWallpaper
{
    private const string CspSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\PersonalizationCSP";
    private const string SpotlightSubKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";

    public enum Result { Ok, NeedsElevation, Failed }

    /// <summary>Writes the wallpaper image the config wants as the lock screen background.
    /// Converts odd formats to JPEG at <paramref name="destPath"/> so Windows can always read them.</summary>
    public static Result Set(string sourcePath, string destPath)
    {
        try
        {
            ValidateSource(sourcePath);

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            using (var img = System.Drawing.Image.FromFile(sourcePath))
                img.Save(destPath, ImageFormat.Jpeg);

            // Per-user: stop Windows Spotlight rotating lock screens over this image.
            // Mirrors what Settings writes when a person picks "Picture".
            try
            {
                using var cu = Registry.CurrentUser.CreateSubKey(SpotlightSubKey, writable: true);
                cu?.SetValue("RotatingLockScreenEnabled", 0, RegistryValueKind.DWord);
                cu?.SetValue("RotatingLockScreenOverlayEnabled", 0, RegistryValueKind.DWord);
            }
            catch (Exception ex) { Common.Log.Warn($"Lock screen: could not clear Spotlight keys: {ex.Message}"); }

            // HKLM short-circuit: if the policy already points at the path we just wrote,
            // the lock screen is configured and no admin is needed this launch.
            try
            {
                using var read = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                    .OpenSubKey(CspSubKey, writable: false);
                if (read?.GetValue("LockScreenImagePath") is string existing
                    && string.Equals(existing, destPath, StringComparison.OrdinalIgnoreCase)
                    && read.GetValue("LockScreenImageStatus") is int status && status == 1)
                    return Result.Ok;
            }
            catch { /* HKLM read can also be blocked; fall through to the write attempt */ }

            // HKLM: the image itself. Needs admin on every consumer build.
            using (var lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                                     .CreateSubKey(CspSubKey, writable: true))
            {
                if (lm is null) return Result.NeedsElevation;
                lm.SetValue("LockScreenImagePath", destPath, RegistryValueKind.String);
                lm.SetValue("LockScreenImageUrl", destPath, RegistryValueKind.String);
                lm.SetValue("LockScreenImageStatus", 1, RegistryValueKind.DWord);
            }
            return Result.Ok;
        }
        catch (UnauthorizedAccessException) { return Result.NeedsElevation; }
        catch (Exception ex) { Common.Log.Warn($"Lock screen write failed: {ex.Message}"); return Result.Failed; }
    }

    /// <summary>The elevated helper invoked with --set-lockscreen: write the policy keys for
    /// <paramref name="image"/> and return success/failure for the launcher.</summary>
    public static Result SetElevated(string image)
    {
        try
        {
            using var lm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                                     .CreateSubKey(CspSubKey, writable: true);
            if (lm is null) return Result.NeedsElevation;
            lm.SetValue("LockScreenImagePath", image, RegistryValueKind.String);
            lm.SetValue("LockScreenImageUrl", image, RegistryValueKind.String);
            lm.SetValue("LockScreenImageStatus", 1, RegistryValueKind.DWord);
            using var cu = Registry.CurrentUser.CreateSubKey(SpotlightSubKey, writable: true);
            cu?.SetValue("RotatingLockScreenEnabled", 0, RegistryValueKind.DWord);
            cu?.SetValue("RotatingLockScreenOverlayEnabled", 0, RegistryValueKind.DWord);
            return Result.Ok;
        }
        catch (Exception ex) { Common.Log.Warn($"Elevated lock screen write failed: {ex.Message}"); return Result.Failed; }
    }

    /// <summary>Re-launches prismos with --set-lockscreen. Windows shows the UAC prompt;
    /// this returns after the elevated process exits.</summary>
    public static Result SetViaElevation(string image)
    {
        try
        {
            if (Environment.ProcessPath is not { } exe) return Result.Failed;
            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exe,
                Arguments = $"--set-lockscreen \"{image}\"",
                Verb = "runas",
                UseShellExecute = true,
            };
            using var process = System.Diagnostics.Process.Start(start);
            process?.WaitForExit(10_000);
            return process?.HasExited == true && process.ExitCode == 0 ? Result.Ok : Result.Failed;
        }
        catch (System.ComponentModel.Win32Exception) { return Result.NeedsElevation; } // user cancelled UAC
        catch (Exception ex) { Common.Log.Warn($"Lock screen elevation failed: {ex.Message}"); return Result.Failed; }
    }

    /// <summary>Stable local path for the converted image. Inside the app's data folder,
    /// so Windows can resolve it but nobody else has to clean it up — replacing in place
    /// each apply means a single stale file tracks whenever the app runs.</summary>
    public static string AppImagePath => Path.Combine(Common.Log.Directory, "lockscreen.jpg");

    private static void ValidateSource(string sourcePath)
    {
        if (!File.Exists(sourcePath)) throw new FileNotFoundException(sourcePath);
    }
}
