using System.Globalization;
using System.Text;

namespace Hatoclip;

/// <summary>
/// Minimal daily-rolling file logger: %LOCALAPPDATA%\Hatoclip\logs\hatoclip-YYYYMMDD.log.
/// Keeps one writer open for the current day, caps each day's file at <see cref="MaxFileBytes"/>
/// and deletes files older than <see cref="RetentionDays"/> days at startup.
/// NEVER pass clipboard contents or the connection key to this class.
/// </summary>
public static class Log
{
    public const int RetentionDays = 14;
    public const long MaxFileBytes = 5 * 1024 * 1024;

    private const string FilePrefix = "hatoclip-";
    private const string FileExtension = ".log";
    private const string DateFormat = "yyyyMMdd";

    private static readonly object Gate = new();
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);

    private static StreamWriter? _writer;
    private static string? _writerDate;
    private static long _writerBytes;
    private static bool _limitReached;

    /// <summary>False disables all file output (the unit tests turn it off). Defaults to true.</summary>
    internal static bool Enabled { get; set; } = true;

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex is null ? message : $"{message} {ex}");

    public static void Write(string level, string message)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            var now = DateTime.Now;
            var line = $"{Timestamp(now)} [{level}] {message}{Environment.NewLine}";
            var date = now.ToString(DateFormat, CultureInfo.InvariantCulture);
            lock (Gate)
            {
                var writer = GetWriterLocked(date);
                if (_limitReached)
                {
                    return;
                }

                var bytes = Utf8NoBom.GetByteCount(line);
                if (_writerBytes + bytes > MaxFileBytes)
                {
                    // Stop writing until the date changes, so a flood of requests cannot fill the disk.
                    _limitReached = true;
                    writer.Write($"{Timestamp(now)} [WARN] log size limit reached; further lines are dropped until tomorrow.{Environment.NewLine}");
                    return;
                }

                writer.Write(line);
                _writerBytes += bytes;
            }
        }
        catch
        {
            // Logging must never crash the app. Drop the writer so the next line retries opening it.
            lock (Gate)
            {
                CloseLocked();
            }
        }
    }

    /// <summary>Deletes log files dated more than <see cref="RetentionDays"/> days ago. Never throws.</summary>
    public static void DeleteOldFiles()
    {
        try
        {
            if (!Directory.Exists(AppPaths.LogDirectory))
            {
                return;
            }

            var cutoff = DateTime.Today.AddDays(-RetentionDays);
            foreach (var file in Directory.EnumerateFiles(AppPaths.LogDirectory, FilePrefix + "*" + FileExtension))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (DateTime.TryParseExact(name[FilePrefix.Length..], DateFormat, CultureInfo.InvariantCulture,
                        DateTimeStyles.None, out var fileDate) && fileDate < cutoff)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // In use or no permission: try again next start.
                    }
                }
            }
        }
        catch
        {
            // Never let cleanup affect startup.
        }
    }

    /// <summary>Closes the open log file (on exit).</summary>
    public static void Close()
    {
        lock (Gate)
        {
            CloseLocked();
        }
    }

    private static StreamWriter GetWriterLocked(string date)
    {
        if (_writer is not null && _writerDate == date)
        {
            return _writer;
        }

        CloseLocked();
        Directory.CreateDirectory(AppPaths.LogDirectory);
        var path = Path.Combine(AppPaths.LogDirectory, FilePrefix + date + FileExtension);
        // ReadWrite/Delete sharing so the user can open (or delete) the log while the app runs.
        var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        _writer = new StreamWriter(stream, Utf8NoBom) { AutoFlush = true };
        _writerDate = date;
        _writerBytes = stream.Length;
        _limitReached = _writerBytes >= MaxFileBytes;
        return _writer;
    }

    private static void CloseLocked()
    {
        try
        {
            _writer?.Dispose();
        }
        catch
        {
            // Ignore errors while closing.
        }

        _writer = null;
        _writerDate = null;
    }

    private static string Timestamp(DateTime now) => now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
}
