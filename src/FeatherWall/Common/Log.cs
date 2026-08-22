using System.Diagnostics;
using System.Globalization;

namespace FeatherWall.Common;

/// <summary>Tiny append-only file logger — %LOCALAPPDATA%\FeatherWall\featherwall.log.</summary>
public static class Log
{
    private static readonly object Sync = new();
    private static string? _path;

    public static string Directory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FeatherWall");

    public static void Init()
    {
        System.IO.Directory.CreateDirectory(Directory);
        _path = Path.Combine(Directory, "featherwall.log");
        try
        {
            if (File.Exists(_path) && new FileInfo(_path).Length > 1_000_000)
                File.Delete(_path);
        }
        catch { /* best effort */ }
    }

    public static void Info(string message) => Write("INF", message);
    public static void Warn(string message) => Write("WRN", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERR", ex is null ? message : $"{message}: {ex}");

    private static int _writesSinceSizeCheck;

    private static void Write(string level, string message)
    {
        // InvariantCulture: '-' and ':' are culture-sensitive placeholders in a custom format
        // string, so on a machine whose culture uses different date or time separators this line
        // came out in a different shape. The log is parsed — scripts/bench/Measure-App.ps1 reads
        // pause events out of it to decide whether a benchmark row is honest — so its timestamp
        // is a format, not decoration, and it should not vary by locale.
        var line = $"{DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} [{level}] {message}";
        Debug.WriteLine(line);
        if (_path is null) return;
        lock (Sync)
        {
            try
            {
                // Long-lived process: keep the log bounded even mid-session.
                if (++_writesSinceSizeCheck >= 1000)
                {
                    _writesSinceSizeCheck = 0;
                    if (File.Exists(_path) && new FileInfo(_path).Length > 5_000_000)
                        File.Delete(_path);
                }
                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch { /* best effort */ }
        }
    }
}
