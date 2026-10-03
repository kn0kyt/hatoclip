namespace Hatoclip;

/// <summary>Well-known file locations under %LOCALAPPDATA%\Hatoclip.</summary>
public static class AppPaths
{
    public static string DataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hatoclip");

    public static string ConfigFile => Path.Combine(DataDirectory, "config.json");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");
}
