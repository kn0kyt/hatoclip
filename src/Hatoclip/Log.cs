using System.Globalization;
using System.Text;

namespace Hatoclip;

/// <summary>
/// Minimal daily-rolling file logger: %LOCALAPPDATA%\Hatoclip\logs\hatoclip-YYYYMMDD.log.
/// NEVER pass clipboard contents or the connection key to this class.
/// </summary>
public static class Log
{
    private static readonly object Gate = new();
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} {ex}");

    public static void Write(string level, string message)
    {
        try
        {
            var now = DateTime.Now;
            var line = $"{now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)} [{level}] {message}{Environment.NewLine}";
            var file = Path.Combine(AppPaths.LogDirectory, $"hatoclip-{now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.log");
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.LogDirectory);
                File.AppendAllText(file, line, Utf8NoBom);
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }
}
