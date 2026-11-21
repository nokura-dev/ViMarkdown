namespace ViMarkdown;

internal static class ExportFileNames
{
    public static string PageStem(WorkspaceItem page) => $"{SafeName(page.Name)}({page.Id})";

    private static string SafeName(string name)
    {
        var invalid = "<>:\"/\\|?*";
        var safe = new string(name.Select(c => char.IsControl(c) || invalid.Contains(c) ? '_' : c).Take(80).ToArray()).Trim(' ', '.');
        return string.IsNullOrEmpty(safe) ? "document" : safe;
    }

}
