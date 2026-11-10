using FeatherWall.Desktop;

namespace FeatherWall.Tests;

// Real .lnk files through the real shell COM object, in a temp folder — a fake would only prove
// the fake. CI runs on windows-latest, so these execute there too.
public class StartMenuShortcutTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("fw-shortcut-").FullName;
    private string Lnk => Path.Combine(_dir, "FeatherWall.lnk");

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Ensure_FirstRun_CreatesShortcutPointingAtTheExe()
    {
        var exe = @"C:\Apps\FeatherWall\featherwall.exe";

        Assert.Equal(ShortcutChange.Created, StartMenuShortcut.Ensure(exe, alreadyAdded: false, Lnk));

        Assert.True(File.Exists(Lnk));
        Assert.Equal(exe, StartMenuShortcut.ReadTarget(Lnk), ignoreCase: true);
    }

    [Fact]
    public void Ensure_LeavesAMatchingShortcutAlone()
    {
        var exe = @"C:\Apps\FeatherWall\featherwall.exe";
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
        StartMenuShortcut.Ensure(@"C:\Old\featherwall.exe", alreadyAdded: false, Lnk);

        Assert.Equal(ShortcutChange.Repointed, StartMenuShortcut.Ensure(@"C:\New\featherwall.exe", alreadyAdded: true, Lnk));
        Assert.Equal(@"C:\New\featherwall.exe", StartMenuShortcut.ReadTarget(Lnk), ignoreCase: true);
    }

    [Fact]
    public void Ensure_AfterTheUserDeletedIt_LeavesItDeleted()
    {
        var exe = @"C:\Apps\FeatherWall\featherwall.exe";
        StartMenuShortcut.Ensure(exe, alreadyAdded: false, Lnk);
        File.Delete(Lnk);

        Assert.Equal(ShortcutChange.None, StartMenuShortcut.Ensure(exe, alreadyAdded: true, Lnk));
        Assert.False(File.Exists(Lnk));
    }

    [Fact]
    public void Ensure_RepairsAnUnreadableShortcut()
    {
        var exe = @"C:\Apps\FeatherWall\featherwall.exe";
        File.WriteAllBytes(Lnk, [0x4C, 0x00, 0x00, 0x00, 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02]);

        Assert.Equal(ShortcutChange.Repointed, StartMenuShortcut.Ensure(exe, alreadyAdded: true, Lnk));
        Assert.Equal(exe, StartMenuShortcut.ReadTarget(Lnk), ignoreCase: true);
    }

    [Theory]
    [InlineData(@"C:\Apps\FeatherWall\featherwall.exe", true)]
    [InlineData(@"C:\Apps\FeatherWall\FeatherWall.EXE", true)]
    [InlineData(@"C:\Program Files\dotnet\dotnet.exe", false)] // `dotnet featherwall.dll`
    public void IsOwnExe_OnlyForFeatherWallItself(string path, bool expected)
    {
        Assert.Equal(expected, StartMenuShortcut.IsOwnExe(path));
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

        Assert.Equal(Path.Combine(programs, "FeatherWall.lnk"), StartMenuShortcut.DefaultPath);
    }
}
