using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ViMarkdown;

public sealed partial class WorkspaceStore
{
    private string EditHistoryPath => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(DatabasePath))!, "ViMarkdown-edit-history.md");
    private const string HistoryMarker = "<!-- ViMarkdown-edit-history: ";

    public void PrepareEditHistory()
    {
        if (DatabasePath == ":memory:") return;
        var path = EditHistoryPath;
        if (File.Exists(path))
        {
            var text = File.ReadAllText(path);
            var markers = Regex.Matches(text, @"(?m)^<!-- ViMarkdown-edit-history: (?<time>[^\r\n]+) -->\r?$");
            var kept = new StringBuilder();
            var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
            var removed = false;
            if (markers.Count > 0) kept.Append(text.AsSpan(0, markers[0].Index));
            for (var i = 0; i < markers.Count; i++)
            {
                var marker = markers[i];
                var end = i + 1 < markers.Count ? markers[i + 1].Index : text.Length;
                if (DateTimeOffset.TryParseExact(marker.Groups["time"].Value, "O", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var timestamp) && timestamp <= cutoff) removed = true;
                else kept.Append(text.AsSpan(marker.Index, end - marker.Index));
            }
            if (removed)
            {
                var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllText(temporary, kept.ToString(), new UTF8Encoding(false));
                    File.Move(temporary, path, overwrite: true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
        }
        // Recover saved changes whose history was not written before a forced exit.
        using var command = CreateCommand("SELECT page_id FROM edit_history_pending ORDER BY page_id");
        var ids = new List<long>();
        using (var reader = command.ExecuteReader())
            while (reader.Read()) ids.Add(reader.GetInt64(0));
        foreach (var id in ids) FlushEditHistory(id);
    }

    public void FlushEditHistory(long pageId)
    {
        if (DatabasePath == ":memory:") return;
        string before, after, name, note;
        using (var command = CreateCommand("SELECT h.baseline,p.body,p.name,COALESCE(n.name,'未分類') FROM edit_history_pending h JOIN items p ON p.id=h.page_id LEFT JOIN items n ON n.id=p.note_id WHERE h.page_id=$id", ("$id", pageId)))
        using (var reader = command.ExecuteReader())
        {
            if (!reader.Read()) return;
            before = reader.GetString(0); after = reader.GetString(1);
            name = reader.GetString(2); note = reader.GetString(3);
        }
        if (before != after)
        {
            var now = DateTimeOffset.Now;
            var diff = BuildHistoryDiff(before, after);
            // A longer fence keeps Markdown code fences in the edited body literal.
            var fence = new string('`', Math.Max(3, Regex.Matches(diff, "`+").Cast<Match>().Select(m => m.Length + 1).DefaultIfEmpty(3).Max()));
            var entry = new StringBuilder();
            entry.Append(HistoryMarker).Append(now.ToString("O", CultureInfo.InvariantCulture)).AppendLine(" -->");
            entry.Append("# ").AppendLine(now.ToString("yyyy/MM/dd", CultureInfo.InvariantCulture));
            entry.Append("## ").Append(HistoryHeading(note)).Append(' ').AppendLine(HistoryHeading(name));
            entry.Append(fence).AppendLine("diff").Append(diff).AppendLine(fence).AppendLine();
            // Keep the pending baseline if writing fails; ordinary DB saves still work.
            File.AppendAllText(EditHistoryPath, entry.ToString(), new UTF8Encoding(false));
        }
        Execute("DELETE FROM edit_history_pending WHERE page_id=$id", ("$id", pageId));
    }

    private static string HistoryHeading(string text) => text.Replace("\r", " ").Replace("\n", " ")
        .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
        .Replace("\\", "\\\\").Replace("`", "\\`").Replace("*", "\\*").Replace("_", "\\_").Replace("[", "\\[").Replace("]", "\\]");

    private static string BuildHistoryDiff(string before, string after)
    {
        // Retain line endings while comparing, including the final newline.
        var oldLines = Regex.Matches(before, @"[^\n]*\n|[^\n]+$").Cast<Match>().Select(m => m.Value).ToArray();
        var newLines = Regex.Matches(after, @"[^\n]*\n|[^\n]+$").Cast<Match>().Select(m => m.Value).ToArray();
        var start = 0;
        while (start < oldLines.Length && start < newLines.Length && oldLines[start] == newLines[start]) start++;
        var oldEnd = oldLines.Length;
        var newEnd = newLines.Length;
        while (oldEnd > start && newEnd > start && oldLines[oldEnd - 1] == newLines[newEnd - 1]) { oldEnd--; newEnd--; }
        var result = new StringBuilder();
        void AppendLine(char prefix, string line)
        {
            result.Append(prefix).Append(line.EndsWith('\n') ? line[..^1] : line).Append('\n');
            if (!line.EndsWith('\n')) result.AppendLine(@"\ No newline at end of file");
        }
        result.Append("@@ -").Append(oldEnd == start ? start : start + 1).Append(',').Append(oldEnd - start)
            .Append(" +").Append(newEnd == start ? start : start + 1).Append(',').Append(newEnd - start).AppendLine(" @@");
        // Bound quadratic work for large replacements. The fallback is a valid
        // replacement of the changed range, preserving every removed/added line.
        var oldCount = oldEnd - start;
        var newCount = newEnd - start;
        if ((long)(oldCount + 1) * (newCount + 1) > 1_000_000)
        {
            for (var i = start; i < oldEnd; i++) AppendLine('-', oldLines[i]);
            for (var j = start; j < newEnd; j++) AppendLine('+', newLines[j]);
            return result.ToString();
        }
        var lengths = new int[oldCount + 1, newCount + 1];
        for (var i = oldCount - 1; i >= 0; i--)
            for (var j = newCount - 1; j >= 0; j--)
                lengths[i, j] = oldLines[start + i] == newLines[start + j]
                    ? lengths[i + 1, j + 1] + 1 : Math.Max(lengths[i + 1, j], lengths[i, j + 1]);
        var x = 0; var y = 0;
        while (x < oldCount || y < newCount)
        {
            if (x < oldCount && y < newCount && oldLines[start + x] == newLines[start + y])
            { AppendLine(' ', oldLines[start + x++]); y++; }
            else if (x < oldCount && (y == newCount || lengths[x + 1, y] >= lengths[x, y + 1]))
                AppendLine('-', oldLines[start + x++]);
            else AppendLine('+', newLines[start + y++]);
        }
        return result.ToString();
    }
}
