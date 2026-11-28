using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ViMarkdown;

internal static class SelectionConversion
{
    // The destination determines the conversion family; the source is inferred
    // from its syntax so users never need to declare the current format.
    internal static string ConvertTo(string text, string target)
    {
        if (target == "d2フェンス")
        {
            var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            return "```d2" + newline + text + (text.EndsWith('\n') || text.EndsWith('\r') ? "" : newline) + "```";
        }
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var body = normalized.EndsWith('\n') ? normalized[..^1] : normalized;
        if (target is "TSV" or "CSV" or "Markdownの表")
        {
            try
            {
                ParseMarkdown(body);
                return Convert(text, "Markdownの表", target);
            }
            catch (FormatException) { }

            // A tab outside quoted CSV cells identifies TSV. Try both parsers:
            // quoted commas and tabs inside cells must not affect detection.
            List<List<string>>? csv = null;
            List<List<string>>? tsv = null;
            try { csv = ParseDelimited(body, ','); } catch (FormatException) { }
            try { tsv = ParseDelimited(body, '\t'); } catch (FormatException) { }
            var source = tsv is not null && tsv.Any(row => row.Count > 1) ? "TSV"
                : csv is not null ? "CSV"
                : tsv is not null ? "TSV"
                : throw new FormatException("表の形式を判定できませんでした。");
            return Convert(text, source, target);
        }

        var fenced = Regex.IsMatch(body, @"^ {0,3}(`{3,}|~{3,})");
        if (target == "コードブロック")
        {
            if (fenced)
            {
                // Validate an existing block rather than wrapping it again.
                Convert(text, "コードブロック", "標準テキスト");
                return text;
            }
            return Convert(text, "標準テキスト", target);
        }
        if (fenced)
        {
            var plainCode = Convert(text, "コードブロック", "標準テキスト");
            return target == "標準テキスト" ? plainCode : Convert(plainCode, "標準テキスト", target);
        }
        // Detect each line separately, including mixed lists. Checkboxes take
        // priority so their brackets are removed together with the bullet.
        var plain = Regex.Replace(text,
            @"^([ \t]*)(?:[-+*][ \t]+\[[ xX]\][ \t]*|[-+*][ \t]+|\d+[.)][ \t]+)",
            "$1", RegexOptions.Multiline);
        return Convert(plain, "標準テキスト", target);
    }

    internal static string Convert(string text, string source, string target)
    {
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var normalized = text.Replace("\r\n", "\n").Replace('\r', '\n');
        var trailing = normalized.EndsWith('\n');
        var body = trailing ? normalized[..^1] : normalized;
        string result;
        if (source is "TSV" or "CSV" or "Markdownの表")
        {
            var rows = source == "Markdownの表" ? ParseMarkdown(body) : ParseDelimited(body, source == "CSV" ? ',' : '\t');
            if (target == "Markdownの表")
            {
                var columns = rows.Max(row => row.Count);
                string Row(List<string> row) => "| " + string.Join(" | ", Enumerable.Range(0, columns).Select(i =>
                    i < row.Count ? WebUtility.HtmlEncode(row[i]).Replace("\\", "&#92;").Replace("|", "&#124;").Replace("\n", "<br>") : "")) + " |";
                result = string.Join("\n", new[] { Row(rows[0]), "| " + string.Join(" | ", Enumerable.Repeat("---", columns)) + " |" }.Concat(rows.Skip(1).Select(Row)));
            }
            else
            {
                var delimiter = target == "CSV" ? ',' : '\t';
                string Cell(string value) => value.IndexOfAny([delimiter, '"', '\n', '\r']) >= 0
                    ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
                result = string.Join("\n", rows.Select(row => string.Join(delimiter.ToString(), row.Select(Cell))));
            }
        }
        else if (source == "コードブロック" || target == "コードブロック")
        {
            if (source == "コードブロック")
            {
                var lines = body.Split('\n');
                var opening = Regex.Match(lines[0], @"^ {0,3}(`{3,}|~{3,})[^\n]*$");
                if (!opening.Success || lines.Length < 2) throw new FormatException("コードブロック全体を選択してください。");
                var fence = opening.Groups[1].Value;
                if (!Regex.IsMatch(lines[^1], @"^ {0,3}" + Regex.Escape(fence[0].ToString()) + "{" + fence.Length + @",}\s*$"))
                    throw new FormatException("閉じフェンスを含むコードブロック全体を選択してください。");
                result = string.Join("\n", lines.Skip(1).Take(lines.Length - 2));
            }
            else
            {
                var longest = Regex.Matches(body, "`+").Select(match => match.Length).DefaultIfEmpty(0).Max();
                var fence = new string('`', Math.Max(3, longest + 1));
                result = fence + "\n" + body + "\n" + fence;
            }
        }
        else
        {
            var number = 0;
            result = string.Join("\n", body.Split('\n').Select(line =>
            {
                var pattern = source switch
                {
                    "Markdownリスト(箇条書き)" => @"^(\s*)[-+*][ \t]+(.*)$",
                    "Markdownリスト(番号付き)" => @"^(\s*)\d+[.)][ \t]+(.*)$",
                    "Markdownチェックボックス" => @"^(\s*)[-+*][ \t]+\[[ xX]\][ \t]*(.*)$",
                    _ => @"^(\s*)(.*)$"
                };
                if (string.IsNullOrWhiteSpace(line)) return line;
                var match = Regex.Match(line, pattern);
                if (!match.Success) throw new FormatException("選択範囲の形式が変換元と一致しません。");
                var prefix = target switch
                {
                    "Markdownリスト(箇条書き)" => "- ",
                    "Markdownリスト(番号付き)" => $"{++number}. ",
                    "Markdownチェックボックス" => "- [ ] ",
                    _ => ""
                };
                return match.Groups[1].Value + prefix + match.Groups[2].Value;
            }));
        }
        return (result + (trailing ? "\n" : "")).Replace("\n", newline);
    }

    private static List<List<string>> ParseDelimited(string text, char delimiter)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var closed = false;
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else { quoted = false; closed = true; }
                }
                else cell.Append(ch);
            }
            else if (ch == delimiter || ch == '\n')
            {
                row.Add(cell.ToString()); cell.Clear(); closed = false;
                if (ch == '\n') { rows.Add(row); row = new List<string>(); }
            }
            else if (ch == '"' && cell.Length == 0 && !closed) quoted = true;
            else if (closed || ch == '"') throw new FormatException("引用符の形式が正しくありません。");
            else cell.Append(ch);
        }
        if (quoted) throw new FormatException("閉じていない引用符があります。");
        row.Add(cell.ToString()); rows.Add(row);
        return rows;
    }

    private static List<List<string>> ParseMarkdown(string text)
    {
        var lines = text.Split('\n');
        var rows = lines.Select(line =>
        {
            var cells = new List<string>();
            var cell = new StringBuilder();
            var value = line.Trim();
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] == '\\' && i + 1 < value.Length && value[i + 1] is '|' or '\\') cell.Append(value[++i]);
                else if (value[i] == '|') { cells.Add(cell.ToString().Trim()); cell.Clear(); }
                else cell.Append(value[i]);
            }
            cells.Add(cell.ToString().Trim());
            if (value.StartsWith('|')) cells.RemoveAt(0);
            if (value.EndsWith('|') && cells.Count > 1 && cells[^1].Length == 0) cells.RemoveAt(cells.Count - 1);
            return cells;
        }).ToList();
        if (rows.Count < 2 || rows[1].Count != rows[0].Count || !rows[1].All(cell => Regex.IsMatch(cell, @"^:?-+:?$")))
            throw new FormatException("見出しと区切り行を含むMarkdownの表を選択してください。");
        rows.RemoveAt(1);
        return rows.Select(row => row.Select(cell => WebUtility.HtmlDecode(Regex.Replace(cell, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase))).ToList()).ToList();
    }
}
