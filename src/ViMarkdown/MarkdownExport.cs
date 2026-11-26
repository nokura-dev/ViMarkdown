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
    public IReadOnlyDictionary<long, string> PageFiles { get; init; } = new Dictionary<long, string>();
    public IReadOnlyDictionary<long, string> ErrorReports { get; init; } = new Dictionary<long, string>();
    public IReadOnlyList<string> D2Errors { get; init; } = [];
    public IReadOnlyList<string> MissingImages { get; init; } = [];
}

public static class MarkdownExport
{
    private static readonly MarkdownPipeline Pipeline = MarkdownPipelines.MarkdownExport;

    public static async Task<MarkdownExportResult> WriteAsync(string destination, IReadOnlyList<WorkspaceItem> pages,
        Func<string, byte[]?> imageResolver, bool pageMediaDirectories = true, string? noteName = null, bool renderDiagrams = false)
    {
        var diagrams = new Dictionary<string, D2Result>();
        if (renderDiagrams) foreach (var code in pages.SelectMany(d => Markdown.Parse(d.Body, Pipeline).Descendants<FencedCodeBlock>())
                     .Where(D2Renderer.IsD2).Select(D2Renderer.Code).Distinct())
            diagrams[code] = await D2Renderer.Shared.RenderAsync(code);
        return Write(destination, pages, imageResolver, code => diagrams[code], pageMediaDirectories, noteName, renderDiagrams);
    }

    // Existing files are preserved; failures roll back only this export.
    public static MarkdownExportResult Write(string destination, IReadOnlyList<WorkspaceItem> pages,
        Func<string, byte[]?> imageResolver, Func<string, D2Result>? d2Resolver = null, bool pageMediaDirectories = true, string? noteName = null, bool renderDiagrams = false)
    {
        if (pages.Count == 0) throw new InvalidOperationException("出力するページがありません。");
        if (pages.Any(d => d.IsNote)) throw new ArgumentException("ページを指定してください。", nameof(pages));
        Directory.CreateDirectory(destination);
        var directory = destination;
        using var output = new ExportOutput(directory);
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pageFiles = new Dictionary<long, string>();
        var errorReports = new Dictionary<long, string>();
        var reportBodies = new Dictionary<string, string>();
        var images = new Dictionary<string, byte[]>();
        var diagramErrors = new List<string>();
        var missing = new HashSet<string>(StringComparer.Ordinal);
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in pages)
        {
            var pageMissing = new HashSet<string>(StringComparer.Ordinal);
            var pageD2Errors = new List<string>();
            var sequence = 0;
            var diagramSequence = 0;
            var stem = ExportFileNames.MediaStem(item, noteName);
            var fileName = ExportFileNames.Allocate(directory, item, ".md", reserved, reserveMediaDirectory: pageMediaDirectories, reserveErrorReport: true, noteName: noteName);
            // Office exporters collect media internally under assets; md uses a folder per page.
            var mediaDirectory = pageMediaDirectories ? Path.GetFileNameWithoutExtension(fileName) : "assets";
            string Resolve(string url)
            {
                if (!url.StartsWith("asset://", StringComparison.Ordinal)) return url;
                sequence++;
                var data = imageResolver(url);
                var name = $"{stem}-{sequence}.png";
                if (data is null)
                {
                    missing.Add(url);
                    pageMissing.Add(url);
                    data = MissingImage();
                    name = $"{stem}-missing-{sequence}.png";
                }
                images.Add(Path.Combine(mediaDirectory, name), data);
                // Keep Japanese readable while escaping Markdown/URL delimiters.
                return EscapeFileName(mediaDirectory) + "/" + EscapeFileName(name);
            }
            string? ResolveDiagram(string code)
            {
                var rendered = d2Resolver?.Invoke(code) ?? D2Renderer.Shared.RenderAsync(code).GetAwaiter().GetResult();
                if (rendered.Png is null)
                {
                    var error = $"{item.Name}({item.Id}): {rendered.Error}";
                    diagramErrors.Add(error);
                    pageD2Errors.Add(error);
                    return null; // Keep invalid source editable in the exported Markdown.
                }
                var name = $"{stem}-d2-{++diagramSequence}.png";
                images.Add(Path.Combine(mediaDirectory, name), rendered.Png);
                return EscapeFileName(mediaDirectory) + "/" + EscapeFileName(name);
            }
            var body = Rewrite(item.Body, Resolve, renderDiagrams ? ResolveDiagram : null);
            files.Add(fileName, body);
            pageFiles.Add(item.Id, fileName);
            if (pageMissing.Count > 0 || pageD2Errors.Count > 0)
            {
                var reportName = Path.GetFileNameWithoutExtension(fileName) + "_error.txt";
                var report = "対象ファイル:\n" + fileName + "\n\n";
                if (pageMissing.Count > 0)
                    report += "次の画像はDBに存在しないため代替画像を出力しました。\n" + string.Join("\n", pageMissing) + "\n\n";
                if (pageD2Errors.Count > 0)
                    report += "描画できないD2は原文コードを保持しました。\n" + string.Join("\n", pageD2Errors) + "\n";
                errorReports.Add(item.Id, reportName);
                reportBodies.Add(reportName, report);
            }
        }
        foreach (var (name, data) in images) output.WriteImage(name, data);
        foreach (var (name, body) in files) output.WriteText(name, body);
        foreach (var (name, body) in reportBodies) output.WriteText(name, body);
        output.Commit();
        return new(directory, pages.Count, images.Count)
        {
            MissingImages = missing.ToArray(), D2Errors = diagramErrors, PageFiles = pageFiles,
            ErrorReports = errorReports
        };
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

    private static string Rewrite(string source, Func<string, string> resolve, Func<string, string?>? resolveDiagram)
    {
        var parsed = Markdown.Parse(source, Pipeline);
        var edits = new List<(int Start, int Length, string Value)>();
        if (resolveDiagram is not null) foreach (var block in parsed.Descendants<FencedCodeBlock>().Where(D2Renderer.IsD2))
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
                // Inline reference uses so exported image URLs point to their media folder.
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
