using Microsoft.Win32;

namespace Hatoclip;

/// <summary>
/// "Start with Windows" via HKCU\Software\Microsoft\Windows\CurrentVersion\Run (no admin rights).
/// </summary>
public static class AutoStart
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Hatoclip";

    public static string CurrentExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    /// <summary>True if any Hatoclip entry exists, even one pointing at an old exe location.</summary>
    public static bool IsEnabled() => !string.IsNullOrWhiteSpace(GetRegisteredCommand());

    /// <summary>True if an entry exists but points somewhere other than <paramref name="exePath"/> (the exe was moved).</summary>
    public static bool IsStale(string? exePath = null) => IsStale(GetRegisteredCommand(), exePath ?? CurrentExePath);

    /// <summary>
    /// True if the entry points at an exe that no longer exists (the exe was moved or deleted). An entry
    /// whose exe is still there is left alone, so running another copy (a dev build, a trial download)
    /// does not take over autostart.
    /// </summary>
    public static bool IsOrphaned(string? exePath = null) =>
        IsOrphaned(GetRegisteredCommand(), exePath ?? CurrentExePath, File.Exists);

    internal static bool IsStale(string? command, string exePath) =>
        !string.IsNullOrWhiteSpace(command) &&
        !string.Equals(command.Trim().Trim('"'), exePath, StringComparison.OrdinalIgnoreCase);

    internal static bool IsOrphaned(string? command, string exePath, Func<string, bool> fileExists) =>
        IsStale(command, exePath) && !fileExists(command!.Trim().Trim('"'));

    /// <summary>Registers (or re-points) the Run entry to the quoted exe path.</summary>
    public static void Enable(string? exePath = null)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
        key.SetValue(ValueName, $"\"{exePath ?? CurrentExePath}\"", RegistryValueKind.String);
        Log.Info("Autostart enabled.");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
        Log.Info("Autostart disabled.");
    }

    private static string? GetRegisteredCommand()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
        return key?.GetValue(ValueName) as string;
    }
}
