using System.Globalization;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using System.Text;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace ViMarkdown;

public sealed record MarkdownExportResult(string Directory, int Pages, int Images)
{
    public IReadOnlyList<string> D2Errors { get; init; } = [];
    public IReadOnlyList<string> MissingImages { get; init; } = [];
}

public static class MarkdownExport
{
    private static readonly MarkdownPipeline Pipeline = MarkdownPipelines.MarkdownExport;

    public static async Task<MarkdownExportResult> WriteAsync(string destination, IReadOnlyList<WorkspaceItem> pages,
        Func<string, byte[]?> imageResolver)
    {
        var diagrams = new Dictionary<string, D2Result>();
        foreach (var code in pages.SelectMany(d => Markdown.Parse(d.Body, Pipeline).Descendants<FencedCodeBlock>())
                     .Where(D2Renderer.IsD2).Select(D2Renderer.Code).Distinct())
            diagrams[code] = await D2Renderer.Shared.RenderAsync(code);
        return Write(destination, pages, imageResolver, code => diagrams[code]);
    }

    // Each export owns a fresh directory, so failures never alter an earlier export.
    public static MarkdownExportResult Write(string destination, IReadOnlyList<WorkspaceItem> pages,
        Func<string, byte[]?> imageResolver, Func<string, D2Result>? d2Resolver = null)
    {
        if (pages.Count == 0) throw new InvalidOperationException("出力するページがありません。");
        if (pages.Any(d => d.IsNote)) throw new ArgumentException("ページを指定してください。", nameof(pages));
        var images = new Dictionary<string, byte[]>();
        var diagramErrors = new List<string>();
        var missing = new HashSet<string>(StringComparer.Ordinal);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in pages)
        {
            var sequence = 0;
            var diagramSequence = 0;
            var stem = ExportFileNames.PageStem(item);
            string Resolve(string url)
            {
                if (!url.StartsWith("asset://", StringComparison.Ordinal)) return url;
                sequence++;
                var data = imageResolver(url);
                var name = $"{stem}-{sequence}.png";
                if (data is null)
                {
                    missing.Add(url);
                    data = MissingImage();
                    name = $"{stem}-missing-{sequence}.png";
                }
                images.Add(name, data);
                // Keep Japanese readable while escaping Markdown/URL delimiters.
                return "assets/" + EscapeFileName(name);
            }
            string? ResolveDiagram(string code)
            {
                var rendered = d2Resolver?.Invoke(code) ?? D2Renderer.Shared.RenderAsync(code).GetAwaiter().GetResult();
                if (rendered.Png is null)
                {
                    diagramErrors.Add($"{item.Name}({item.Id}): {rendered.Error}");
                    return null; // Keep invalid source editable in the exported Markdown.
                }
                var name = $"{stem}-d2-{++diagramSequence}.png";
                images.Add(name, rendered.Png);
                return "assets/" + EscapeFileName(name);
            }
            var body = Rewrite(item.Body, Resolve, ResolveDiagram);
            // IDs distinguish duplicate names, including names that sanitize to the same filename.
            files.Add(stem + ".md", body);
        }
        Directory.CreateDirectory(destination);
        var baseName = "MarkdownExport-" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var directory = Path.Combine(destination, baseName);
        for (var suffix = 2; Directory.Exists(directory) || File.Exists(directory); suffix++)
            directory = Path.Combine(destination, baseName + "-" + suffix);
        try
        {
            Directory.CreateDirectory(directory);
            if (images.Count > 0) Directory.CreateDirectory(Path.Combine(directory, "assets"));
            foreach (var (name, data) in images) File.WriteAllBytes(Path.Combine(directory, "assets", name), data);
            foreach (var (name, body) in files) File.WriteAllText(Path.Combine(directory, name), body, new UTF8Encoding(false));
            if (missing.Count > 0)
                File.WriteAllText(Path.Combine(directory, "missing-images.txt"), "次の画像はDBに存在しないため代替画像を出力しました。\n" + string.Join("\n", missing), new UTF8Encoding(false));
            if (diagramErrors.Count > 0)
                File.WriteAllText(Path.Combine(directory, "d2-errors.txt"), "描画できないD2は原文コードを保持しました。\n" + string.Join("\n", diagramErrors), new UTF8Encoding(false));
            return new(directory, pages.Count, images.Count) { MissingImages = missing.ToArray(), D2Errors = diagramErrors };
        }
        catch { if (Directory.Exists(directory)) Directory.Delete(directory, true); throw; }
    }

    private static string EscapeFileName(string name)
    {
        var result = new StringBuilder();
        foreach (var rune in name.EnumerateRunes())
        {
            var value = rune.Value;
            var safeAscii = value is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9'
                or '-' or '.' or '_' or '~';
            var safeUnicode = value > 127 && !Rune.IsWhiteSpace(rune) && !Rune.IsControl(rune)
                && Rune.GetUnicodeCategory(rune) != UnicodeCategory.Format;
            result.Append(safeAscii || safeUnicode ? rune.ToString() : Uri.EscapeDataString(rune.ToString()));
        }
        return result.ToString();
    }

    private static byte[] MissingImage()
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(640, 100));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.DrawRectangle(Brushes.LightGray, null, new Rect(0, 0, 640, 100));
            var text = new FormattedText("画像が見つかりません / Missing image", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Typography.BodyTypeface, 20, Brushes.DimGray);
            context.DrawText(text, new Point(20, 35));
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        return stream.ToArray();
    }

    private static string Rewrite(string source, Func<string, string> resolve, Func<string, string?> resolveDiagram)
    {
        var parsed = Markdown.Parse(source, Pipeline);
        var edits = new List<(int Start, int Length, string Value)>();
        foreach (var block in parsed.Descendants<FencedCodeBlock>().Where(D2Renderer.IsD2))
        {
            var url = resolveDiagram(D2Renderer.Code(block));
            if (url is not null)
            {
                var end = block.Span.End + 1;
                while (end > block.Span.Start && source[end - 1] is '\r' or '\n') end--;
                edits.Add((block.Span.Start, end - block.Span.Start, "![D2](" + url + ")"));
            }
        }
        foreach (var link in parsed.Descendants<LinkInline>())
        {
            var url = link.Url ?? "";
            var converted = resolve(url);
            if (link.Reference is not null)
            {
                // Inline reference uses so exported image URLs point to assets.
                var first = link.FirstChild?.Span.Start ?? (link.Span.Start + (link.IsImage ? 2 : 1));
                var last = link.LastChild?.Span.End ?? (first - 1);
                var label = last >= first ? source[first..(last + 1)] : "";
                var title = link.Title is null ? "" : " \"" + link.Title.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                edits.Add((link.Span.Start, link.Span.Length, (link.IsImage ? "!" : "") + "[" + label + "](<" + converted.Replace(">", "%3E").Replace("<", "%3C") + ">" + title + ")"));
            }
            else if (converted != url)
                edits.Add((link.UrlSpan.Start, link.UrlSpan.Length, converted));
        }
        // Remove definitions; all live uses have been converted above.
        foreach (var definition in parsed.Descendants<LinkReferenceDefinition>())
            edits.Add((definition.Span.Start, definition.Span.Length, ""));
        var result = new StringBuilder(source);
        foreach (var edit in edits.OrderByDescending(e => e.Start)) result.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Value);
        return result.ToString();
    }
}
