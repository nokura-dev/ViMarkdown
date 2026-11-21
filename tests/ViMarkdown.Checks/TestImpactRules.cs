// These are conservative runtime dependencies, including routed events and reflection.
// A new/unmapped input is shared by every suite until its impact is reviewed here.
internal static class TestImpactRules
{
    private static readonly string[] GuiSuites =
    [
        nameof(MarkdownSyntaxChecks), nameof(NavigationChecks), nameof(PageInkAlignmentChecks),
        nameof(ModeAlignmentChecks), nameof(InsertLineEmphasisChecks), nameof(MarkdownHighlightChecks),
        nameof(EditorViewChecks), nameof(PaneNavigationChecks), nameof(FocusChecks), nameof(ImeChecks), nameof(TrashChecks),
        nameof(WorkspaceChecks), nameof(WorkspaceSearchChecks), nameof(WorkspaceShortcutChecks),
        nameof(WorkspaceMenuChecks), nameof(PageMoveChecks), nameof(WindowPlacementChecks),
        nameof(WorkspacePaneChecks), nameof(HistoryChecks), nameof(VimCommandChecks),
        nameof(MovementAuditChecks), nameof(LineEndEditingChecks), nameof(ParagraphNavigationChecks), nameof(ScrollChecks)
    ];
    private static readonly string[] Exports = [nameof(ExportChecks), nameof(DocxChecks), nameof(MermaidChecks)];
    private static readonly Dictionary<string, string[]> Sources = BuildSources();

    private static Dictionary<string, string[]> BuildSources()
    {
        var result = new Dictionary<string, string[]>(StringComparer.Ordinal);
        void Add(string[] suites, params string[] files)
        {
            foreach (var file in files) result.Add("src/ViMarkdown/" + file + ".cs", suites);
        }
        Add(GuiSuites, "VimKeyMap", "VimInput", "VimCommands", "VimNavigation", "VimEditing",
            "VimClipboard", "VimSearch", "VimRegex", "SourceNavigation", "DisplayNavigation", "MacInputMode",
            "ImeComposition", "WorkspaceLayout", "WorkspacePanes", "WorkspaceActions", "WorkspaceDialogs",
            "WorkspacePageMove", "WorkspaceShortcuts", "WorkspaceFocus", "WindowPlacement", "TimelineNavigation");
        Add(GuiSuites.Concat(Exports).ToArray(), "WorkspaceOrdering", "TimelinePeriods", "ExportTools");
        Add(GuiSuites.Concat([nameof(MediaChecks), nameof(ImageSizingChecks)]).ToArray(), "MediaEditing");
        Add(GuiSuites.Concat([nameof(TrashChecks)]).Distinct().ToArray(), "WorkspaceTrash");
        Add([nameof(WorkspaceSearchChecks)], "WorkspaceSearch");
        Add(GuiSuites, "WorkspaceSearchView", "WorkspaceSearchNavigation", "SearchHighlight");
        Add(Exports, "MarkdownExport", "ExportFileNames");
        Add([nameof(DocxChecks)], "DocxExport");
        // Shared layout, projection, persistence, initialization, and rendering files
        // deliberately use the all-suite fallback (not just their direct unit tests).
        return result;
    }

    public static IReadOnlyList<string> ForFile(string path, IReadOnlyList<string> suites)
    {
        if (Sources.TryGetValue(path, out var affected)) return affected;
        const string prefix = "tests/ViMarkdown.Checks/";
        if (path.StartsWith(prefix, StringComparison.Ordinal) && path.EndsWith(".cs", StringComparison.Ordinal))
        {
            var name = Path.GetFileNameWithoutExtension(path);
            if (suites.Contains(name, StringComparer.Ordinal))
                return name == nameof(PageInkAlignmentChecks) ? [name, nameof(ModeAlignmentChecks)] :
                    name == nameof(ScrollChecks) ? [name, nameof(PaneNavigationChecks)] : [name];
        }
        if (path == "docs/markdown-features-sample.md") return [nameof(ProjectionChecks), nameof(VimCommandChecks)];
        return suites;
    }
}
