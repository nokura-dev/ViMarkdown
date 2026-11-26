namespace ViMarkdown;

internal static class ExportFileNames
{
    public static string PageStem(WorkspaceItem page, string? noteName = null) =>
        (noteName is null ? "" : SafeName(noteName) + "_") + SafeName(page.Name);
    public static string MediaStem(WorkspaceItem page, string? noteName = null) => $"{PageStem(page, noteName)}({page.Id})";

    public static string Allocate(string directory, WorkspaceItem page, string extension, ISet<string>? reserved = null, bool reserveMediaDirectory = false, bool reserveErrorReport = false, string? noteName = null)
        => Allocate(directory, PageStem(page, noteName), extension, reserved, reserveMediaDirectory, reserveErrorReport);

    public static string Allocate(string directory, string stem, string extension, ISet<string>? reserved = null, bool reserveMediaDirectory = false, bool reserveErrorReport = false)
    {
        var existing = Directory.Exists(directory)
            ? new HashSet<string>(Directory.EnumerateFileSystemEntries(directory).Select(Path.GetFileName)!, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var suffix = 0; ; suffix++)
        {
            var name = stem + (suffix == 0 ? "" : "_" + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture)) + extension;
            var mediaDirectory = Path.GetFileNameWithoutExtension(name);
            var errorName = mediaDirectory + "_error.txt";
            if (existing.Contains(name) || reserved?.Contains(name) == true ||
                (reserveMediaDirectory && (existing.Contains(mediaDirectory) || reserved?.Contains(mediaDirectory) == true)) ||
                (reserveErrorReport && (existing.Contains(errorName) || reserved?.Contains(errorName) == true))) continue;
            reserved?.Add(name);
            if (reserveErrorReport) reserved?.Add(errorName);
            if (reserveMediaDirectory) reserved?.Add(mediaDirectory);
            return name;
        }
    }

    private static string SafeName(string name)
    {
        var invalid = "<>:\"/\\|?*";
        var safe = new string(name.Select(c => char.IsControl(c) || invalid.Contains(c) ? '_' : c).Take(80).ToArray()).Trim(' ', '.');
        return string.IsNullOrEmpty(safe) ? "document" : safe;
    }
}
