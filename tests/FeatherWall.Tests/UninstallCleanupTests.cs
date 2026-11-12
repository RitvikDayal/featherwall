using FeatherWall.Config;
using FeatherWall.Desktop;
using Microsoft.Win32;

namespace FeatherWall.Tests;

// Real .lnk files and a real HKCU key under a throwaway name, as in StartMenuShortcutTests.
public class UninstallCleanupTests : IDisposable
{
    private const string InstallDir = @"C:\Users\me\AppData\Local\FeatherWallApp\current";
    private readonly string _dir = Directory.CreateTempSubdirectory("fw-uninstall-").FullName;
    private readonly string _runKey = $@"Software\FeatherWallTests\{Guid.NewGuid():N}";
    private string Lnk => Path.Combine(_dir, "FeatherWall.lnk");

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
        Registry.CurrentUser.DeleteSubKeyTree(_runKey, throwOnMissingSubKey: false);
    }

    [Theory]
    [InlineData(@"C:\Users\me\AppData\Local\FeatherWallApp\current\featherwall.exe")]
    [InlineData("\"C:\\Users\\me\\AppData\\Local\\FeatherWallApp\\current\\featherwall.exe\"")]
    [InlineData(@"c:\users\ME\appdata\local\featherwallapp\CURRENT\featherwall.exe")]
    public void PointsInto_TheInstalledExe(string stored) =>
        Assert.True(UninstallCleanup.PointsInto(stored, InstallDir));

    [Theory]
    [InlineData(@"C:\Projects\featherwall\bin\Release\featherwall.exe")]
    [InlineData(@"C:\Users\me\AppData\Local\FeatherWallApp\current2\featherwall.exe")]
    [InlineData(@"C:\Users\me\AppData\Local\FeatherWallApp\current")]
    [InlineData("")]
    [InlineData(null)]
    public void PointsInto_NotForAnythingElse(string? stored) =>
        Assert.False(UninstallCleanup.PointsInto(stored, InstallDir));

    [Fact]
    public void RemoveShortcut_DeletesOneThatOpensTheInstalledCopy()
    {
        StartMenuShortcut.Ensure(Path.Combine(InstallDir, "featherwall.exe"), alreadyAdded: false, Lnk);

        Assert.True(UninstallCleanup.RemoveShortcut(InstallDir, Lnk));
        Assert.False(File.Exists(Lnk));
    }

    [Fact]
    public void RemoveShortcut_KeepsOneThatOpensAnotherCopy()
    {
        StartMenuShortcut.Ensure(@"C:\Tools\FeatherWall\featherwall.exe", alreadyAdded: false, Lnk);

        Assert.False(UninstallCleanup.RemoveShortcut(InstallDir, Lnk));
        Assert.True(File.Exists(Lnk));
    }

    [Fact]
    public void RemoveShortcut_NoShortcutIsFine() =>
        Assert.False(UninstallCleanup.RemoveShortcut(InstallDir, Lnk));

    // config.json lives in %APPDATA% and survives the uninstall. Left saying "added", a reinstall
    // would read the missing shortcut as one the user deleted and never make it again.
    [Fact]
    public void ForgetShortcut_LetsAReinstallMakeItAgain()
    {
        var config = Path.Combine(_dir, "config.json");
        ConfigStore.Save(new AppConfig { StartMenuShortcutAdded = true }, config);

        UninstallCleanup.ForgetShortcut(config);

        Assert.False(ConfigStore.Load(config, out _).StartMenuShortcutAdded);
    }

    [Fact]
    public void ForgetShortcut_LeavesAnUnreadableConfigAsItIs()
    {
        var config = Path.Combine(_dir, "config.json");
        File.WriteAllText(config, "{ not json");

        UninstallCleanup.ForgetShortcut(config);

        Assert.Equal("{ not json", File.ReadAllText(config));
    }

    [Fact]
    public void ForgetShortcut_WritesNoConfigWhereThereWasNone()
    {
        var config = Path.Combine(_dir, "config.json");

        UninstallCleanup.ForgetShortcut(config);

        Assert.False(File.Exists(config));
    }

    [Fact]
    public void RemoveAutostart_DeletesAnEntryThatStartsTheInstalledCopy()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(_runKey))
            key.SetValue(Autostart.ValueName, $"\"{Path.Combine(InstallDir, "featherwall.exe")}\"");

        Assert.True(UninstallCleanup.RemoveAutostart(InstallDir, _runKey));
        using var after = Registry.CurrentUser.OpenSubKey(_runKey);
        Assert.Null(after?.GetValue(Autostart.ValueName));
    }

    [Fact]
    public void RemoveAutostart_KeepsAnEntryThatStartsAnotherCopy()
    {
        using (var key = Registry.CurrentUser.CreateSubKey(_runKey))
            key.SetValue(Autostart.ValueName, "\"C:\\Tools\\FeatherWall\\featherwall.exe\"");

        Assert.False(UninstallCleanup.RemoveAutostart(InstallDir, _runKey));
        using var after = Registry.CurrentUser.OpenSubKey(_runKey);
        Assert.Equal("\"C:\\Tools\\FeatherWall\\featherwall.exe\"", after?.GetValue(Autostart.ValueName));
    }
}
