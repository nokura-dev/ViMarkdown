namespace ViMarkdown;

// Paths are relative to the executable, never the caller's working directory.
public static class RuntimeAssets
{
    public static string DataDirectory => OperatingSystem.IsMacOS() &&
        Path.GetFileName(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory)) == "MacOS"
        ? Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Resources")) : AppContext.BaseDirectory;
    public static string FontDirectory => Path.Combine(DataDirectory, "Fonts", "MigMix1M");
    public static string D2Executable => Path.Combine(AppContext.BaseDirectory, "Tools", "D2",
        OperatingSystem.IsWindows() ? "d2.exe" : "d2");
}
