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
    public void Ensure_CreatesShortcutPointingAtTheExe()
    {
        var exe = @"C:\Apps\FeatherWall\featherwall.exe";

        Assert.True(StartMenuShortcut.Ensure(exe, Lnk));

        Assert.True(File.Exists(Lnk));
        Assert.Equal(exe, StartMenuShortcut.ReadTarget(Lnk), ignoreCase: true);
    }

    [Fact]
    public void Ensure_LeavesAMatchingShortcutAlone()
    {
        var exe = @"C:\Apps\FeatherWall\featherwall.exe";
        StartMenuShortcut.Ensure(exe, Lnk);
        // Backdated so a rewrite inside the file system's timestamp resolution still shows.
        File.SetLastWriteTimeUtc(Lnk, DateTime.UtcNow.AddDays(-1));
        var written = File.GetLastWriteTimeUtc(Lnk);

        Assert.False(StartMenuShortcut.Ensure(exe, Lnk));
        Assert.Equal(written, File.GetLastWriteTimeUtc(Lnk));
    }

    [Fact]
    public void Ensure_RepointsAShortcutLeftByAnotherCopy()
    {
        StartMenuShortcut.Ensure(@"C:\Old\featherwall.exe", Lnk);

        Assert.True(StartMenuShortcut.Ensure(@"C:\New\featherwall.exe", Lnk));
        Assert.Equal(@"C:\New\featherwall.exe", StartMenuShortcut.ReadTarget(Lnk), ignoreCase: true);
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
