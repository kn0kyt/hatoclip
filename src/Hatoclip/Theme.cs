using Microsoft.Win32;

namespace Hatoclip;

/// <summary>
/// Colours of the PC-side UI. The values mirror the CSS custom properties of the phone page
/// (src/Hatoclip/Web/style.css) so that the window, the tray menu and the page look like one product.
/// The warning and "ok soft" colours have no counterpart on the page and are defined here only.
/// </summary>
public sealed record ThemePalette(
    bool IsDark,
    Color Background,
    Color Card,
    Color Field,
    Color Text,
    Color Muted,
    Color Border,
    Color Accent,
    Color AccentText,
    Color AccentSoft,
    Color Ok,
    Color OkSoft,
    Color Warn,
    Color WarnSoft,
    Color WarnBorder,
    Color Focus,
    Color PigeonBody,
    Color PigeonWing,
    Color PigeonBeak);

/// <summary>
/// The single place for the look of the PC-side UI: palette (light / dark), fonts and metrics.
/// Metrics are logical pixels at 96 DPI; callers scale them with <see cref="Scale"/>.
/// </summary>
/// <remarks>
/// The palette follows the Windows app theme (Settings → Personalization → Colors → "Choose your app
/// mode"). Developer switch: the environment variable <c>HATOCLIP_THEME=light|dark</c> forces a theme.
/// </remarks>
public static class Theme
{
    public const string OverrideVariable = "HATOCLIP_THEME";

    // ---- metrics (logical pixels at 96 DPI) ---------------------------------------------------

    public const int OuterPadding = 20;
    public const int CardPadding = 20;
    public const int CardRadius = 12;
    public const int FieldRadius = 8;
    public const int ButtonRadius = 8;
    public const int CalloutRadius = 8;
    public const int BlockGap = 16;
    public const int SmallGap = 8;

    /// <summary>Space a <see cref="RoundedButton"/> keeps around its body for the keyboard focus ring.</summary>
    public const int FocusRingInset = 3;

    // ---- fonts (points) -------------------------------------------------------------------------

    public const float BaseFontSize = 9f;
    public const float SmallFontSize = 8.5f;
    public const float TitleFontSize = 15f;
    public const float MenuFontSize = 9f;

    private static readonly string[] RegularFamilies = ["Yu Gothic UI", "Segoe UI"];
    private static readonly string[] SemiboldFamilies = ["Yu Gothic UI Semibold", "Segoe UI Semibold"];

    public static readonly ThemePalette Light = new(
        IsDark: false,
        Background: Hex(0xf4f6fa),
        Card: Hex(0xffffff),
        Field: Hex(0xf7f9fc),
        Text: Hex(0x1d2433),
        Muted: Hex(0x5d6779),
        Border: Hex(0xd6dce6),
        Accent: Hex(0x4a628c),
        AccentText: Hex(0xffffff),
        AccentSoft: Hex(0xe3e9f3),
        Ok: Hex(0x1f7a3d),
        OkSoft: Hex(0xe5f3e9),
        Warn: Hex(0x9a4a00),
        WarnSoft: Hex(0xfdf3e3),
        WarnBorder: Hex(0xf0d3a4),
        Focus: Hex(0x2f6fd6),
        PigeonBody: Hex(0x4a628c),
        PigeonWing: Hex(0x96accd),
        PigeonBeak: Hex(0xf0a032));

    public static readonly ThemePalette Dark = new(
        IsDark: true,
        Background: Hex(0x12161d),
        Card: Hex(0x1c222c),
        Field: Hex(0x151a22),
        Text: Hex(0xe6e9ef),
        Muted: Hex(0x9aa4b5),
        Border: Hex(0x2f3846),
        Accent: Hex(0x8ea6d0),
        AccentText: Hex(0x10141a),
        AccentSoft: Hex(0x263042),
        Ok: Hex(0x6fcf8f),
        OkSoft: Hex(0x1d3326),
        Warn: Hex(0xf2b45c),
        WarnSoft: Hex(0x33291b),
        WarnBorder: Hex(0x5a4526),
        Focus: Hex(0x9cc1ff),
        PigeonBody: Hex(0x8ea6d0),
        PigeonWing: Hex(0xc9d6ea),
        PigeonBeak: Hex(0xf0a032));

    private static bool _listening;

    /// <summary>The palette in use. Read and changed on the UI thread.</summary>
    public static ThemePalette Current { get; private set; } = Resolve();

    /// <summary>Raised on the UI thread when the Windows app theme changes (after <see cref="Initialize"/>).</summary>
    public static event EventHandler? Changed;

    /// <summary>
    /// Starts following Windows theme changes. Call once on the UI thread after the WinForms
    /// synchronization context exists (SystemEvents raises the event on the subscribing thread).
    /// </summary>
    public static void Initialize()
    {
        if (_listening)
        {
            return;
        }

        _listening = true;
        SystemEvents.UserPreferenceChanged += (_, e) =>
        {
            // The app theme switch arrives as WM_SETTINGCHANGE("ImmersiveColorSet") = category General.
            if (e.Category != UserPreferenceCategory.General)
            {
                return;
            }

            var palette = Resolve();
            if (palette != Current)
            {
                Current = palette;
                Changed?.Invoke(null, EventArgs.Empty);
            }
        };
    }

    /// <summary>The palette for the forced theme (environment variable) or else the Windows app theme.</summary>
    public static ThemePalette Resolve() =>
        ParseOverride(Environment.GetEnvironmentVariable(OverrideVariable)) switch
        {
            true => Dark,
            false => Light,
            null => NativeTheme.AppsUseDarkTheme() ? Dark : Light,
        };

    /// <summary><c>"dark"</c> → true, <c>"light"</c> → false (any case, trimmed), anything else → null (no override).</summary>
    internal static bool? ParseOverride(string? value) =>
        value?.Trim().ToLowerInvariant() switch
        {
            "dark" => true,
            "light" => false,
            _ => null,
        };

    /// <summary>Logical (96 DPI) pixels to device pixels, rounded; never rounds a positive value down to 0.</summary>
    public static int Scale(int logical, float factor)
    {
        if (logical == 0)
        {
            return 0;
        }

        var scaled = (int)Math.Round(logical * factor, MidpointRounding.AwayFromZero);
        return scaled == 0 ? Math.Sign(logical) : scaled;
    }

    /// <summary>Creates a UI font sized in device pixels for <paramref name="dpi"/> (Yu Gothic UI, else Segoe UI, else the default).</summary>
    public static Font CreateFont(float points, int dpi, bool semibold = false)
    {
        var pixels = points * dpi / 72f;
        var family = FindFamily(semibold ? SemiboldFamilies : RegularFamilies);
        if (family is not null)
        {
            return new Font(family, pixels, FontStyle.Regular, GraphicsUnit.Pixel);
        }

        // Neither semibold face exists: fall back to bold of the regular family (or the system default).
        var regular = FindFamily(RegularFamilies) ?? SystemFonts.MessageBoxFont?.FontFamily.Name ?? FontFamily.GenericSansSerif.Name;
        return new Font(regular, pixels, semibold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
    }

    /// <summary>A point-sized font for controls that do their own DPI handling (the tray menu).</summary>
    public static Font CreatePointFont(float points)
    {
        var family = FindFamily(RegularFamilies) ?? SystemFonts.MessageBoxFont?.FontFamily.Name ?? FontFamily.GenericSansSerif.Name;
        return new Font(family, points, FontStyle.Regular, GraphicsUnit.Point);
    }

    private static readonly Dictionary<string, bool> FamilyExists = [];

    private static string? FindFamily(string[] names)
    {
        foreach (var name in names)
        {
            if (!FamilyExists.TryGetValue(name, out var exists))
            {
                try
                {
                    using var family = new FontFamily(name);
                    exists = true;
                }
                catch (ArgumentException)
                {
                    exists = false;
                }

                FamilyExists[name] = exists;
            }

            if (exists)
            {
                return name;
            }
        }

        return null;
    }

    private static Color Hex(int rgb) => Color.FromArgb(255, (rgb >> 16) & 0xff, (rgb >> 8) & 0xff, rgb & 0xff);
}
