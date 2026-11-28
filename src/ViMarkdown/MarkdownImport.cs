using System.Text;
using Avalonia.Media.Imaging;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace ViMarkdown;

public sealed record MarkdownImportResult(string Body, int Images, IReadOnlyList<string> MissingImages);

public static class MarkdownImport
{
    public static MarkdownImportResult Read(string path, Func<byte[], string> saveImage)
    {
        var source = File.ReadAllText(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var parsed = Markdown.Parse(source, MarkdownPipelines.MarkdownExport);
        var edits = new List<(int Start, int Length, string Value)>();
        var images = new Dictionary<string, string>(StringComparer.Ordinal);
        var missing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var link in parsed.Descendants<LinkInline>().Where(link => link.IsImage))
        {
            var url = link.Url ?? "";
            if (string.IsNullOrWhiteSpace(url)) continue;
            // Only local images are imported. Remote URLs are never downloaded.
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && !uri.IsFile) continue;
            string asset;
            try
            {
                var imagePath = uri is { IsFile: true } ? uri.LocalPath
                    : Path.GetFullPath(Path.Combine(directory, Uri.UnescapeDataString(url)));
                if (!images.TryGetValue(imagePath, out asset!))
                {
                    var data = File.ReadAllBytes(imagePath);
                    using var bitmap = new Bitmap(new MemoryStream(data));
                    var png = data.AsSpan().StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                        ? data : ImageContent.EncodePng(bitmap);
                    asset = saveImage(png);
                    images.Add(imagePath, asset);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                missing.Add(url);
                continue;
            }
            if (link.Reference is not null)
            {
                var first = link.FirstChild?.Span.Start ?? link.Span.Start + 2;
                var last = link.LastChild?.Span.End ?? first - 1;
                var label = last >= first ? source[first..(last + 1)] : "";
                var title = link.Title is null ? "" : " \"" + link.Title.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
                edits.Add((link.Span.Start, link.Span.Length, $"![{label}]({asset}{title})"));
            }
            else edits.Add((link.UrlSpan.Start, link.UrlSpan.Length, asset));
        }
        var body = new StringBuilder(source);
        foreach (var edit in edits.OrderByDescending(edit => edit.Start))
            body.Remove(edit.Start, edit.Length).Insert(edit.Start, edit.Value);
        return new(body.ToString(), images.Count, missing.ToArray());
    }
}
