using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig.Extensions.Tables;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Utilities;
using S = DocumentFormat.OpenXml.Spreadsheet;
using A = DocumentFormat.OpenXml.Drawing;
using X = DocumentFormat.OpenXml.Drawing.Spreadsheet;

namespace ViMarkdown;

public static class XlsxExport
{
    private const double PixelsPerPoint = 96d / 72;
    private static readonly FontFamily ExportFont = new("Meiryo UI, Yu Gothic, Hiragino Sans, sans-serif");
    private record Piece(string Text, bool Bold = false, bool Italic = false);

    public static async Task<MarkdownExportResult> WriteAsync(string destination, IReadOnlyList<WorkspaceItem> pages,
        Func<string, byte[]?> imageResolver, Func<string, D2Result>? d2Resolver = null, string? noteName = null)
    {
        var stage = Path.Combine(Path.GetTempPath(), "ViMarkdown-xlsx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            var collected = d2Resolver is null
                ? await MarkdownExport.WriteAsync(stage, pages, imageResolver, pageMediaDirectories: false, noteName: noteName, renderDiagrams: true)
                : MarkdownExport.Write(stage, pages, imageResolver, d2Resolver, pageMediaDirectories: false, noteName: noteName, renderDiagrams: true);
            Directory.CreateDirectory(destination);
            using var output = new ExportOutput(destination);
            var pageFiles = new Dictionary<long, string>();
            var errorReports = new Dictionary<long, string>();
            foreach (var page in pages)
            {
                var fileName = ExportFileNames.Allocate(destination, page, ".xlsx", reserveErrorReport: collected.ErrorReports.ContainsKey(page.Id), noteName: noteName);
                pageFiles.Add(page.Id, fileName);
                using var stream = output.CreateFile(fileName);
                Write(stream, page.Name,
                    File.ReadAllText(Path.Combine(collected.Directory, collected.PageFiles[page.Id])), collected.Directory);
            }
            foreach (var (pageId, sourceName) in collected.ErrorReports)
            {
                var text = File.ReadAllText(Path.Combine(collected.Directory, sourceName));
                var body = text[(text.IndexOf("\n\n", StringComparison.Ordinal) + 2)..];
                var reportName = Path.GetFileNameWithoutExtension(pageFiles[pageId]) + "_error.txt";
                output.WriteText(reportName, "対象ファイル:\n" + pageFiles[pageId] + "\n\n" + body);
                errorReports.Add(pageId, reportName);
            }
            output.Commit();
            return collected with
            {
                Directory = destination, PageFiles = pageFiles,
                ErrorReports = errorReports
            };
        }
        finally { Directory.Delete(stage, true); }
    }

    private static void Write(Stream outputStream, string name, string markdown, string mediaDirectory)
    {
        var document = Markdown.Parse(markdown, MarkdownPipelines.DocxExport);
        var columns = Math.Max(1, Descendants(document).OfType<Table>().Select(t => t.ColumnDefinitions.Count).DefaultIfEmpty(1).Max());
        if (columns > 16384) throw new InvalidDataException("表の列数がExcelの上限を超えています。");
        using var package = SpreadsheetDocument.Create(outputStream, SpreadsheetDocumentType.Workbook);
        var book = package.AddWorkbookPart();
        book.Workbook = new S.Workbook();
        var styles = book.AddNewPart<WorkbookStylesPart>();
        styles.Stylesheet = new S.Stylesheet(
            new S.Fonts(new S.Font(new S.FontSize { Val = 10 }, new S.FontName { Val = "Meiryo UI" })) { Count = 1 },
            new S.Fills(new S.Fill(new S.PatternFill { PatternType = S.PatternValues.None }),
                new S.Fill(new S.PatternFill { PatternType = S.PatternValues.Gray125 })) { Count = 2 },
            new S.Borders(new S.Border()) { Count = 1 },
            new S.CellStyleFormats(new S.CellFormat()) { Count = 1 },
            new S.CellFormats(new S.CellFormat { FontId = 0, FillId = 0, BorderId = 0, FormatId = 0,
                ApplyAlignment = true, Alignment = new S.Alignment { WrapText = true, Vertical = S.VerticalAlignmentValues.Top } },
                new S.CellFormat { FontId = 0, FillId = 0, BorderId = 0, FormatId = 0,
                    ApplyAlignment = true, Alignment = new S.Alignment { WrapText = false, Vertical = S.VerticalAlignmentValues.Top } }) { Count = 2 });
        var part = book.AddNewPart<WorksheetPart>();
        var data = new S.SheetData();
        part.Worksheet = new S.Worksheet(
            new S.SheetProperties(new S.PageSetupProperties { FitToPage = true }),
            new S.Columns(new S.Column { Min = 1, Max = (uint)columns, Width = 90d / columns, CustomWidth = true }), data);
        book.Workbook.Append(new S.Sheets(new S.Sheet { Name = "ページ", SheetId = 1, Id = book.GetIdOfPart(part) }));
        var drawing = part.AddNewPart<DrawingsPart>();
        drawing.WorksheetDrawing = new X.WorksheetDrawing();
        uint rowIndex = 0, imageId = 0;
        string Reference(int column, uint row)
        {
            var result = "";
            for (var n = column + 1; n > 0; n = (n - 1) / 26) result = (char)('A' + (n - 1) % 26) + result;
            return result + row;
        }
        // Column widths use the maximum digit width of the workbook's default font.
        var digitWidth = Enumerable.Range(0, 10).Max(digit =>
        {
            using var layout = new TextLayout(digit.ToString(CultureInfo.InvariantCulture), new Typeface(ExportFont),
                10 * PixelsPerPoint, Brushes.Black);
            return layout.WidthIncludingTrailingWhitespace;
        });
        var columnPixels = Math.Floor(90d / columns * digitWidth + .5) + 5;
        S.Row NewRow(double? height = null)
        {
            if (++rowIndex > 1048576) throw new InvalidDataException("出力行数がExcelの上限を超えています。");
            var row = new S.Row { RowIndex = rowIndex };
            if (height is double fixedHeight)
            {
                row.Height = Math.Clamp(fixedHeight, 1, 400);
                row.CustomHeight = true;
            }
            data.Append(row);
            return row;
        }
        void WriteRows(IReadOnlyList<List<Piece>> cells, bool table = false, double size = 10)
        {
            // Body text may overflow into adjacent empty cells. Only table cells wrap.
            var width = table ? Math.Max(1, columnPixels - 5) : double.PositiveInfinity;
            var chunks = cells.Select(cell => Split(cell, width, size)).ToArray();
            var count = chunks.Max(c => c.Count);
            for (var i = 0; i < count; i++)
            {
                // All text cells are unmerged and leave row height to Excel.
                var row = NewRow();
                for (var column = 0; column < chunks.Length; column++)
                {
                    if (i >= chunks[column].Count) continue;
                    var content = new S.InlineString();
                    foreach (var piece in chunks[column][i])
                    {
                        var properties = new S.RunProperties(new S.RunFont { Val = "Meiryo UI" });
                        if (piece.Bold) properties.Append(new S.Bold());
                        if (piece.Italic) properties.Append(new S.Italic());
                        properties.Append(new S.FontSize { Val = size });
                        content.Append(new S.Run(properties, new S.Text(piece.Text) { Space = SpaceProcessingModeValues.Preserve }));
                    }
                    row.Append(new S.Cell { CellReference = Reference(column, rowIndex), DataType = S.CellValues.InlineString,
                        StyleIndex = table ? 0u : 1u, InlineString = content });
                }
            }
        }
        void Image(LinkInline link)
        {
            var url = link.Url ?? "";
            if (!url.StartsWith("assets/", StringComparison.Ordinal))
            {
                WriteRows([new List<Piece> { new("[画像: " + url + "]") }]);
                return;
            }
            var file = Path.Combine(mediaDirectory, "assets", Path.GetFileName(Uri.UnescapeDataString(url[7..])));
            using var bitmap = new Bitmap(file);
            var dpi = ImageContent.ReadPngDpi(File.ReadAllBytes(file)) ?? new Avalonia.Vector(96, 96);
            var width = bitmap.PixelSize.Width * 72d / dpi.X;
            var height = bitmap.PixelSize.Height * 72d / dpi.Y;
            var scale = Math.Min(1, Math.Min(510.3 / width, 756.9 / height));
            width *= scale; height *= scale;
            var startRow = rowIndex;
            for (double remaining = height + 8; remaining > 0; remaining -= 400) NewRow(Math.Min(400, remaining));
            var image = drawing.AddImagePart(ImagePartType.Png);
            using (var stream = File.OpenRead(file)) image.FeedData(stream);
            var id = ++imageId;
            long cx = Math.Max(1, (long)(width * 12700)), cy = Math.Max(1, (long)(height * 12700));
            drawing.WorksheetDrawing.Append(new X.OneCellAnchor(
                new X.FromMarker(new X.ColumnId("0"), new X.ColumnOffset("0"), new X.RowId(startRow.ToString(CultureInfo.InvariantCulture)), new X.RowOffset("0")),
                new X.Extent { Cx = cx, Cy = cy },
                new X.Picture(
                    new X.NonVisualPictureProperties(new X.NonVisualDrawingProperties { Id = id, Name = "Image " + id },
                        new X.NonVisualPictureDrawingProperties(new A.PictureLocks { NoChangeAspect = true })),
                    new X.BlipFill(new A.Blip { Embed = drawing.GetIdOfPart(image) }, new A.Stretch(new A.FillRectangle())),
                    new X.ShapeProperties(new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = cx, Cy = cy }),
                        new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })), new X.ClientData()));
        }
        void Blocks(ContainerBlock blocks, int depth = 0, string? marker = null, bool inList = false)
        {
            foreach (var block in blocks)
            {
                if (block is ListBlock list)
                {
                    var number = int.TryParse(list.OrderedStart, out var start) ? start : 1;
                    foreach (var item in list.OfType<ListItemBlock>())
                    {
                        var label = list.IsOrdered ? (number++).ToString(CultureInfo.InvariantCulture) + ". " : "• ";
                        Blocks(item, depth + 1, label, true);
                    }
                }
                else if (block is Table table)
                {
                    foreach (var row in table.OfType<TableRow>())
                    {
                        var cells = row.OfType<TableCell>().Select(cell => CellText(cell, row.IsHeader)).ToList();
                        if (cells.Count > 0) WriteRows(cells, true);
                        foreach (var cell in row.OfType<TableCell>())
                            foreach (var image in Descendants(cell).OfType<LeafBlock>().SelectMany(l => Images(l.Inline))) Image(image);
                    }
                }
                else if (block is CodeBlock code)
                {
                    foreach (var line in code.Lines.ToString().Split('\n')) WriteRows([new List<Piece> { new(line.TrimEnd('\r')) }]);
                }
                else if (block is LeafBlock leaf)
                {
                    if (block is HeadingBlock { Level: 1 or 2 })
                    {
                        // Normalize blank text rows, preserving rows reserved for images.
                        while (data.LastChild is S.Row previous && previous.Elements<S.Cell>().Any() &&
                            previous.Elements<S.Cell>().All(cell => string.IsNullOrWhiteSpace(cell.InnerText)))
                        {
                            previous.Remove();
                            rowIndex--;
                        }
                        NewRow();
                    }
                    var pieces = new List<Piece>();
                    if (depth > 0) pieces.Add(new(new string(' ', Math.Min(depth - (inList ? 1 : 0), 20) * 2)));
                    if (marker is not null) { pieces.Add(new(marker)); marker = null; }
                    Inline(leaf.Inline, pieces, block is HeadingBlock);
                    WriteRows([pieces], size: block is HeadingBlock h ? new double[] { 18, 16, 14, 12, 11, 10 }[h.Level - 1] : 10);
                    foreach (var image in Images(leaf.Inline)) Image(image);
                }
                else if (block is ContainerBlock nested)
                {
                    Blocks(nested, depth + 1, marker, inList);
                    marker = null;
                }
            }
        }
        WriteRows([new List<Piece> { new(name, true) }], size: 10);
        Blocks(document);
        // docx uses 850 twips on every side. SpreadsheetML margins are inches.
        const double margin = 850d / 1440;
        part.Worksheet.Append(new S.PageMargins { Left = margin, Right = margin, Top = margin, Bottom = margin, Header = .5, Footer = .5 },
            new S.PageSetup { PaperSize = 9, Orientation = S.OrientationValues.Portrait, FitToWidth = 1, FitToHeight = 0 });
        if (drawing.WorksheetDrawing.HasChildren)
        {
            part.Worksheet.Append(new S.Drawing { Id = part.GetIdOfPart(drawing) });
            drawing.WorksheetDrawing.Save();
        }
        else part.DeletePart(drawing);
        styles.Stylesheet.Save();
        part.Worksheet.Save();
        book.Workbook.Save();
        var errors = new OpenXmlValidator().Validate(package).Take(5).ToArray();
        if (errors.Length > 0) throw new InvalidDataException(string.Join("; ", errors.Select(e => e.Description)));
    }

    private static IEnumerable<Block> Descendants(ContainerBlock block)
    {
        foreach (var child in block)
        {
            yield return child;
            if (child is ContainerBlock nested)
                foreach (var descendant in Descendants(nested)) yield return descendant;
        }
    }

    private static IEnumerable<LinkInline> Images(ContainerInline? container)
    {
        if (container is null) yield break;
        foreach (var item in container)
        {
            if (item is LinkInline { IsImage: true } image) yield return image;
            else if (item is ContainerInline nested)
                foreach (var child in Images(nested)) yield return child;
        }
    }

    private static List<Piece> CellText(ContainerBlock block, bool bold)
    {
        var result = new List<Piece>();
        foreach (var child in Descendants(block))
        {
            if (child is not LeafBlock leaf) continue;
            if (result.Count > 0) result.Add(new("\n"));
            if (leaf is CodeBlock code) result.Add(new(code.Lines.ToString(), bold));
            else Inline(leaf.Inline, result, bold);
        }
        return result;
    }

    private static void Inline(ContainerInline? container, List<Piece> result, bool bold = false, bool italic = false)
    {
        if (container is null) return;
        foreach (var item in container)
            switch (item)
            {
                case LiteralInline literal: result.Add(new(literal.Content.ToString(), bold, italic)); break;
                case CodeInline code: result.Add(new(code.Content, bold, italic)); break;
                case LineBreakInline: result.Add(new("\n", bold, italic)); break;
                case LinkInline { IsImage: true }: break;
                case LinkInline link:
                    Inline(link, result, bold, italic);
                    if (!string.IsNullOrEmpty(link.Url)) result.Add(new(" (" + link.Url + ")", bold, italic));
                    break;
                case EmphasisInline emphasis:
                    Inline(emphasis, result, bold || emphasis.DelimiterCount >= 2, italic || emphasis.DelimiterCount == 1); break;
                case ContainerInline nested: Inline(nested, result, bold, italic); break;
                default: result.Add(new(item.ToString() ?? "", bold, italic)); break;
            }
    }

    private static double MeasureHeight(List<Piece> pieces, double width, double size)
    {
        var text = string.Concat(pieces.Select(p => p.Text));
        var spans = new List<ValueSpan<TextRunProperties>>();
        var offset = 0;
        foreach (var piece in pieces)
        {
            if (piece.Text.Length > 0)
                spans.Add(new ValueSpan<TextRunProperties>(offset, piece.Text.Length,
                    new GenericTextRunProperties(new Typeface(ExportFont,
                        piece.Italic ? FontStyle.Italic : FontStyle.Normal,
                        piece.Bold ? FontWeight.Bold : FontWeight.Normal), size * PixelsPerPoint,
                        foregroundBrush: Brushes.Black)));
            offset += piece.Text.Length;
        }
        using var layout = new TextLayout(text.Length == 0 ? " " : text, new Typeface(ExportFont),
            size * PixelsPerPoint, Brushes.Black,
            textWrapping: double.IsPositiveInfinity(width) ? TextWrapping.NoWrap : TextWrapping.Wrap,
            maxWidth: width, textStyleOverrides: spans);
        // Convert Avalonia's 96-DPI units to points; allow only 2pt for cell padding.
        return Math.Ceiling((layout.Height / PixelsPerPoint + 2) * 4) / 4;
    }

    private static List<Piece> Slice(List<Piece> pieces, int start, int length)
    {
        var result = new List<Piece>();
        var offset = 0;
        foreach (var piece in pieces)
        {
            var from = Math.Max(start, offset);
            var to = Math.Min(start + length, offset + piece.Text.Length);
            if (to > from) result.Add(piece with { Text = piece.Text.Substring(from - offset, to - from) });
            offset += piece.Text.Length;
            if (offset >= start + length) break;
        }
        return result;
    }

    private static List<List<Piece>> Split(List<Piece> pieces, double width, double size)
    {
        var text = string.Concat(pieces.Select(p => p.Text));
        if (text.Length == 0) return [pieces];
        // Search only Unicode text-element boundaries, preserving emoji and run styles.
        var boundaries = StringInfo.ParseCombiningCharacters(text).Append(text.Length).ToArray();
        var result = new List<List<Piece>>();
        for (var first = 0; first < boundaries.Length - 1;)
        {
            var start = boundaries[first];
            int low = first + 1, high = boundaries.Length - 1, best = first;
            while (low <= high)
            {
                var middle = low + (high - low) / 2;
                var length = boundaries[middle] - start;
                if (length <= 32767 && text.AsSpan(start, length).Count('\n') <= 253 &&
                    MeasureHeight(Slice(pieces, start, length), width, size) <= 380)
                { best = middle; low = middle + 1; }
                else high = middle - 1;
            }
            if (best == first)
                throw new InvalidDataException("1文字の結合文字列がExcelのセル上限を超えています。");
            // Prefer a nearby sentence or line boundary without creating tiny chunks.
            if (best < boundaries.Length - 1)
            {
                var minimum = start + (boundaries[best] - start) * 4 / 5;
                for (var candidate = best; candidate > first && boundaries[candidate] >= minimum; candidate--)
                {
                    if (text[boundaries[candidate] - 1] is '。' or '.' or '！' or '？' or '\n')
                    { best = candidate; break; }
                }
            }
            result.Add(Slice(pieces, start, boundaries[best] - start));
            first = best;
        }
        return result;
    }
}
