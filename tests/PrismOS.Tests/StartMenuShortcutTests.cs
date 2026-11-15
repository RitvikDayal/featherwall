using PrismOS.Config;
using PrismOS.Desktop;

namespace PrismOS.Tests;

// Real .lnk files through the real shell COM object, in a temp folder — a fake would only prove
// the fake. CI runs on windows-latest, so these execute there too.
public class StartMenuShortcutTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("fw-shortcut-").FullName;
    private string Lnk => Path.Combine(_dir, "PrismOS.lnk");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Ensure_FirstRun_CreatesShortcutPointingAtTheExe()
    {
        var exe = @"C:\Apps\PrismOS\prismos.exe";

        Assert.Equal(ShortcutChange.Created, StartMenuShortcut.Ensure(exe, alreadyAdded: false, Lnk));

        Assert.True(File.Exists(Lnk));
        Assert.Equal(exe, StartMenuShortcut.ReadTarget(Lnk), ignoreCase: true);
    }

    [Fact]
    public void Ensure_LeavesAMatchingShortcutAlone()
    {
        var exe = @"C:\Apps\PrismOS\prismos.exe";
        StartMenuShortcut.Ensure(exe, alreadyAdded: false, Lnk);
        // Backdated so a rewrite inside the file system's timestamp resolution still shows.
        File.SetLastWriteTimeUtc(Lnk, DateTime.UtcNow.AddDays(-1));
        var written = File.GetLastWriteTimeUtc(Lnk);

        Assert.Equal(ShortcutChange.None, StartMenuShortcut.Ensure(exe, alreadyAdded: true, Lnk));
        Assert.Equal(written, File.GetLastWriteTimeUtc(Lnk));
    }

    [Fact]
    public void Ensure_RepointsAShortcutLeftByAnotherCopy()
    {
        StartMenuShortcut.Ensure(@"C:\Old\prismos.exe", alreadyAdded: false, Lnk);

        Assert.Equal(ShortcutChange.Repointed, StartMenuShortcut.Ensure(@"C:\New\prismos.exe", alreadyAdded: true, Lnk));
        Assert.Equal(@"C:\New\prismos.exe", StartMenuShortcut.ReadTarget(Lnk), ignoreCase: true);
    }

    [Fact]
    public void Ensure_AfterTheUserDeletedIt_LeavesItDeleted()
    {
        var exe = @"C:\Apps\PrismOS\prismos.exe";
        StartMenuShortcut.Ensure(exe, alreadyAdded: false, Lnk);
        File.Delete(Lnk);

        Assert.Equal(ShortcutChange.None, StartMenuShortcut.Ensure(exe, alreadyAdded: true, Lnk));
        Assert.False(File.Exists(Lnk));
    }

    [Fact]
    public void Ensure_RepairsAnUnreadableShortcut()
    {
        var exe = @"C:\Apps\PrismOS\prismos.exe";
        File.WriteAllBytes(Lnk, [0x4C, 0x00, 0x00, 0x00, 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02]);

        Assert.Equal(ShortcutChange.Repointed, StartMenuShortcut.Ensure(exe, alreadyAdded: true, Lnk));
        Assert.Equal(exe, StartMenuShortcut.ReadTarget(Lnk), ignoreCase: true);
    }

    [Theory]
    [InlineData(@"C:\Apps\PrismOS\prismos.exe", true)]
    [InlineData(@"C:\Apps\PrismOS\PrismOS.EXE", true)]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe", false)] // `dotnet prismos.dll`
    public void IsOwnExe_OnlyForPrismOSItself(string path, bool expected)
    {
        Assert.Equal(expected, StartMenuShortcut.IsOwnExe(path));
    }

    [Fact]
    public void Reconcile_UnreadableConfig_TouchesNothing()
    {
        // Load fell back to defaults, so the flag reads false whatever the user's file said: acting
        // on it would recreate a deleted entry, and saving would write defaults over their settings.
        var config = new AppConfig();

        var (change, markAdded) = StartMenuShortcut.Reconcile(@"C:\Apps\PrismOS\prismos.exe", config,
                                                              configUnreadable: true, Lnk);

        Assert.Equal(ShortcutChange.None, change);
        Assert.False(markAdded);
        Assert.False(File.Exists(Lnk));
    }

    [Fact]
    public void Reconcile_ShortcutFromAnOlderBuild_IsMarkedAdded()
    {
        var exe = @"C:\Apps\PrismOS\prismos.exe";
        StartMenuShortcut.Ensure(exe, alreadyAdded: false, Lnk);
        var config = new AppConfig { StartMenuShortcutAdded = false };

        var (change, markAdded) = StartMenuShortcut.Reconcile(exe, config, configUnreadable: false, Lnk);

        Assert.Equal(ShortcutChange.None, change);
        Assert.True(markAdded);
        Assert.True(config.StartMenuShortcutAdded);
    }

    [Fact]
    public void Reconcile_AlreadyMarked_DoesNotAskForASave()
    {
        var exe = @"C:\Apps\PrismOS\prismos.exe";
        StartMenuShortcut.Ensure(exe, alreadyAdded: false, Lnk);

        var (_, markAdded) = StartMenuShortcut.Reconcile(exe, new AppConfig { StartMenuShortcutAdded = true },
                                                         configUnreadable: false, Lnk);

        Assert.False(markAdded);
    }

    [Fact]
    public void ReadTarget_IsNullWhenThereIsNoShortcut()
    {
        Assert.Null(StartMenuShortcut.ReadTarget(Lnk));
    }

    [Fact]
    public void DefaultPath_IsTheUsersOwnStartMenu()
    {
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.Programs);

        Assert.Equal(Path.Combine(programs, "PrismOS.lnk"), StartMenuShortcut.DefaultPath);
    }
}
