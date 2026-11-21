using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using W = DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Markdig.Extensions.Tables;
using Avalonia.Media.Imaging;

namespace ViMarkdown;

public static class DocxExport
{
    private const int PageWidth = 11906, PageHeight = 16838;
    private const int VerticalMargin = 850, HorizontalMargin = 850; // 15mm, in twips.
    private const string BodySize = "20"; // Open XML font sizes are in half-points.
    private static readonly string[] HeadingSizes = ["36", "32", "28", "24", "22", "20"];
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().UseAdjacentReferenceLinks().Build();

    private static W.RunFonts Fonts() => new()
    {
        Ascii = "Meiryo UI", HighAnsi = "Meiryo UI", EastAsia = "Meiryo UI", ComplexScript = "Meiryo UI"
    };

    public static async Task<MarkdownExportResult> WriteAsync(string destination, IReadOnlyList<WorkspaceItem> pages,
        Func<string, byte[]?> imageResolver, Func<string, D2Result>? d2Resolver = null)
    {
        // Reuse the Markdown media collector, including diagram diagnostics and placeholders.
        var stage = Path.Combine(Path.GetTempPath(), "ViMarkdown-docx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        string? output = null;
        try
        {
            var collected = d2Resolver is null
                ? await MarkdownExport.WriteAsync(stage, pages, imageResolver)
                : MarkdownExport.Write(stage, pages, imageResolver, d2Resolver);
            Directory.CreateDirectory(destination);
            output = Path.Combine(destination, "DocxExport-" + DateTime.Now.ToString("yyyyMMddHHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(output);
            foreach (var page in pages)
            {
                var stem = ExportFileNames.PageStem(page);
                var markdown = File.ReadAllText(Path.Combine(collected.Directory, stem + ".md"));
                Write(Path.Combine(output, stem + ".docx"), page.Name, markdown, collected.Directory);
            }
            foreach (var report in Directory.GetFiles(collected.Directory, "*.txt")) File.Copy(report, Path.Combine(output, Path.GetFileName(report)));
            return collected with { Directory = output };
        }
        catch { if (output is not null && Directory.Exists(output)) Directory.Delete(output, true); throw; }
        finally { Directory.Delete(stage, true); }
    }

    private static void Write(string path, string name, string markdown, string mediaDirectory)
    {
        using var package = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = package.AddMainDocumentPart();
        main.AddNewPart<StyleDefinitionsPart>().Styles = new W.Styles(new W.DocDefaults(
            new W.RunPropertiesDefault(new W.RunPropertiesBaseStyle(Fonts(),
                new W.FontSize { Val = BodySize }, new W.FontSizeComplexScript { Val = BodySize }))));
        var body = new W.Body();
        main.Document = new W.Document(body);
        uint imageId = 0;
        W.Run Text(string value, bool bold = false, bool italic = false)
        {
            var props = new W.RunProperties(Fonts());
            if (bold) props.Append(new W.Bold());
            if (italic) props.Append(new W.Italic());
            props.Append(new W.FontSize { Val = BodySize }, new W.FontSizeComplexScript { Val = BodySize });
            return new W.Run(props, new W.Text(value) { Space = SpaceProcessingModeValues.Preserve });
        }
        W.Run Image(string url, string alt)
        {
            if (!url.StartsWith("assets/", StringComparison.Ordinal)) return Text("[画像: " + alt + " / " + url + "]");
            var file = Path.Combine(mediaDirectory, "assets", Path.GetFileName(Uri.UnescapeDataString(url[7..])));
            using var bitmap = new Bitmap(file);
            var dpi = ImageContent.ReadPngDpi(File.ReadAllBytes(file)) ?? new Avalonia.Vector(96, 96);
            var width = bitmap.PixelSize.Width * 914400d / dpi.X;
            var height = bitmap.PixelSize.Height * 914400d / dpi.Y;
            var scale = Math.Min(1, Math.Min((PageWidth - HorizontalMargin * 2) * 635d / width,
                (PageHeight - VerticalMargin * 2) * 635d / height));
            long cx = Math.Max(1, (long)(width * scale)), cy = Math.Max(1, (long)(height * scale));
            var part = main.AddImagePart(ImagePartType.Png);
            using (var stream = File.OpenRead(file)) part.FeedData(stream);
            var id = ++imageId;
            return new W.Run(new W.Drawing(new DW.Inline(
                new DW.Extent { Cx = cx, Cy = cy },
                new DW.DocProperties { Id = id, Name = "Image " + id, Description = alt },
                new DW.NonVisualGraphicFrameDrawingProperties(new A.GraphicFrameLocks { NoChangeAspect = true }),
                new A.Graphic(new A.GraphicData(new PIC.Picture(
                    new PIC.NonVisualPictureProperties(new PIC.NonVisualDrawingProperties { Id = id, Name = "Image " + id }, new PIC.NonVisualPictureDrawingProperties()),
                    new PIC.BlipFill(new A.Blip { Embed = main.GetIdOfPart(part) }, new A.Stretch(new A.FillRectangle())),
                    new PIC.ShapeProperties(new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = cx, Cy = cy }),
                        new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
                { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }))));
        }
        void Inline(ContainerInline? container, W.Paragraph paragraph, bool bold = false, bool italic = false)
        {
            if (container is null) return;
            foreach (var item in container)
                switch (item)
                {
                    case LiteralInline literal: paragraph.Append(Text(literal.Content.ToString(), bold, italic)); break;
                    case CodeInline code: paragraph.Append(Text(code.Content, bold, italic)); break;
                    case LineBreakInline: paragraph.Append(new W.Run(new W.Break())); break;
                    case EmphasisInline emphasis: Inline(emphasis, paragraph, bold || emphasis.DelimiterCount >= 2, italic || emphasis.DelimiterCount == 1); break;
                    case LinkInline link when link.IsImage: paragraph.Append(Image(link.Url ?? "", link.FirstChild?.ToString() ?? "画像")); break;
                    case LinkInline link:
                        Inline(link, paragraph, bold, italic);
                        if (!string.IsNullOrEmpty(link.Url)) paragraph.Append(Text(" (" + link.Url + ")"));
                        break;
                    case ContainerInline nested: Inline(nested, paragraph, bold, italic); break;
                    default: paragraph.Append(Text(item.ToString() ?? "")); break;
                }
        }
        void Blocks(ContainerBlock blocks, OpenXmlCompositeElement target)
        {
            foreach (var block in blocks)
            {
                if (block is Table table)
                {
                    var result = new W.Table(new W.TableProperties(new W.TableWidth { Width = "5000", Type = W.TableWidthUnitValues.Pct },
                        new W.TableBorders(new W.TopBorder { Val = W.BorderValues.Single }, new W.LeftBorder { Val = W.BorderValues.Single },
                            new W.BottomBorder { Val = W.BorderValues.Single }, new W.RightBorder { Val = W.BorderValues.Single },
                            new W.InsideHorizontalBorder { Val = W.BorderValues.Single }, new W.InsideVerticalBorder { Val = W.BorderValues.Single })));
                    var grid = new W.TableGrid();
                    foreach (var column in table.ColumnDefinitions) grid.Append(new W.GridColumn { Width = (9000 / Math.Max(1, table.ColumnDefinitions.Count)).ToString() });
                    result.Append(grid);
                    foreach (var row in table.OfType<TableRow>())
                    {
                        var r = new W.TableRow();
                        foreach (var cell in row.OfType<TableCell>())
                        {
                            var c = new W.TableCell(); Blocks(cell, c);
                            if (c.LastChild is not W.Paragraph) c.Append(new W.Paragraph());
                            r.Append(c);
                        }
                        result.Append(r);
                    }
                    target.Append(result); continue;
                }
                if (block is CodeBlock code)
                {
                    foreach (var line in code.Lines.ToString().Split('\n')) target.Append(new W.Paragraph(Text(line.TrimEnd('\r'))));
                }
                else if (block is LeafBlock leaf)
                {
                    var p = new W.Paragraph();
                    if (block is HeadingBlock heading)
                        p.Append(new W.ParagraphProperties(new W.KeepNext(), new W.OutlineLevel { Val = heading.Level - 1 },
                            new W.ParagraphMarkRunProperties(Fonts(), new W.Bold(),
                                new W.FontSize { Val = HeadingSizes[heading.Level - 1] },
                                new W.FontSizeComplexScript { Val = HeadingSizes[heading.Level - 1] })));
                    Inline(leaf.Inline, p, block is HeadingBlock);
                    if (block is HeadingBlock h)
                        foreach (var run in p.Elements<W.Run>())
                        {
                            run.RunProperties!.GetFirstChild<W.FontSize>()!.Val = HeadingSizes[h.Level - 1];
                            run.RunProperties.GetFirstChild<W.FontSizeComplexScript>()!.Val = HeadingSizes[h.Level - 1];
                        }
                    target.Append(p);
                }
                else if (block is ContainerBlock nested)
                {
                    if (block is ListItemBlock) target.Append(new W.Paragraph(Text("•")));
                    Blocks(nested, target);
                }
            }
        }
        body.Append(new W.Paragraph(Text(name, bold: true)));
        Blocks(Markdown.Parse(markdown, Pipeline), body);
        body.Append(new W.SectionProperties(new W.PageSize { Width = PageWidth, Height = PageHeight },
            new W.PageMargin { Top = VerticalMargin, Right = HorizontalMargin, Bottom = VerticalMargin,
                Left = HorizontalMargin, Header = 720, Footer = 720, Gutter = 0 }));
        main.Document.Save();
        var errors = new OpenXmlValidator().Validate(package).Take(5).ToArray();
        if (errors.Length > 0) throw new InvalidDataException(string.Join("; ", errors.Select(e => e.Description)));
    }
}
