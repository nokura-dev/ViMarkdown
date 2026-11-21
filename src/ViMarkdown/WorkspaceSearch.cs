namespace ViMarkdown;

public enum WorkspaceSearchScope { AllNotes, SelectedNote, CurrentPage }

public sealed record WorkspaceSearchHit(long PageId, long? NoteId, string NoteName, string PageName,
    int LineNumber, int Offset, string LineText, string Keyword);

public static class WorkspaceSearch
{
    public static List<(int Start, int End)> Matches(string text, string keyword)
    {
        var result = new List<(int, int)>();
        if (string.IsNullOrWhiteSpace(keyword)) return result;
        for (var offset = 0; offset <= text.Length - keyword.Length;)
        {
            var index = text.IndexOf(keyword, offset, StringComparison.OrdinalIgnoreCase);
            if (index < 0) break;
            result.Add((index, index + keyword.Length));
            offset = index + keyword.Length;
        }
        return result;
    }

    public static List<WorkspaceSearchHit> Find(IEnumerable<WorkspaceItem> items, WorkspaceSearchScope scope,
        long? noteId, long? pageId, string keyword, string? currentBody = null)
    {
        var all = items.ToList();
        var notes = all.Where(i => i.IsNote).ToDictionary(i => i.Id, i => i.Name);
        var pages = all.Where(i => !i.IsNote && (scope == WorkspaceSearchScope.AllNotes ||
            (scope == WorkspaceSearchScope.SelectedNote ? i.NoteId == noteId : i.Id == pageId)));
        var hits = new List<WorkspaceSearchHit>();
        foreach (var page in pages.OrderBy(p => p.NoteId.HasValue ? notes.GetValueOrDefault(p.NoteId.Value) : "未分類")
                     .ThenBy(p => p.Name).ThenBy(p => p.Id))
        {
            var body = page.Id == pageId && currentBody is not null ? currentBody : page.Body;
            var start = 0;
            var line = 1;
            while (start < body.Length)
            {
                var end = start;
                while (end < body.Length && body[end] is not '\r' and not '\n') end++;
                var text = body[start..end];
                var matches = Matches(text, keyword);
                if (matches.Count > 0)
                    hits.Add(new(page.Id, page.NoteId, page.NoteId.HasValue ? notes.GetValueOrDefault(page.NoteId.Value, "未分類") : "未分類",
                        page.Name, line, start + matches[0].Start, text, keyword));
                start = end < body.Length && body[end] == '\r' && end + 1 < body.Length && body[end + 1] == '\n' ? end + 2 : end + 1;
                line++;
            }
        }
        return hits;
    }
}
