using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Hatoclip;

/// <summary>
/// Windows theme interop: reads the app theme setting and applies DWM window attributes (dark title
/// bar, rounded corners). Every call is best effort: on Windows 10 builds without an attribute the
/// call simply fails and the window keeps the default look.
/// </summary>
internal static class NativeTheme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;

    public enum CornerPreference
    {
        Default = 0,
        DoNotRound = 1,
        Round = 2,
        RoundSmall = 3,
    }

    /// <summary>True when "Choose your default app mode" is Dark (<c>AppsUseLightTheme</c> = 0).</summary>
    public static bool AppsUseDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
            return IsDarkFromRegistryValue(key?.GetValue("AppsUseLightTheme"));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    /// <summary>Interprets the <c>AppsUseLightTheme</c> value: a DWORD 0 means dark; missing or anything else means light.</summary>
    internal static bool IsDarkFromRegistryValue(object? value) => value is int i && i == 0;

    /// <summary>Dark or light title bar (Windows 10 1809+ / Windows 11).</summary>
    public static void SetDarkTitleBar(IntPtr hwnd, bool dark)
    {
        var value = dark ? 1 : 0;
        if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref value, sizeof(int)) != 0)
        {
            _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref value, sizeof(int));
        }
    }

    /// <summary>Window corner rounding (Windows 11). Returns false where unsupported.</summary>
    public static bool SetCornerPreference(IntPtr hwnd, CornerPreference preference)
    {
        var value = (int)preference;
        return DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref value, sizeof(int)) == 0;
    }

    /// <summary>Colour of the 1px window border DWM draws (Windows 11). Returns false where unsupported.</summary>
    public static bool SetBorderColor(IntPtr hwnd, Color color)
    {
        var colorref = color.R | (color.G << 8) | (color.B << 16);
        return DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref colorref, sizeof(int)) == 0;
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
