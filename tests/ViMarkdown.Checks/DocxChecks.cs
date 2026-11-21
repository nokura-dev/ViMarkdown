using ViMarkdown;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using W = DocumentFormat.OpenXml.Wordprocessing;

static class DocxChecks
{
    public static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "docx-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var store = new WorkspaceStore(Path.Combine(root, "source.db"));
            using var bitmap = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(16, 8));
            using var imageStream = new MemoryStream();
            bitmap.Save(imageStream);
            var png = imageStream.ToArray();
            var asset = store.AddImage(png);
            var id = store.Create("同名(資料)", false, null, "# 見出し\n\n## 見出し2\n\n### 見出し3\n\n#### 見出し4\n\n##### 見出し5\n\n###### 見出し6\n\n**太字** *斜体* `code`\n次行\n\n```text\nコード本文\n```\n\n| 列A | 列B |\n|---|---|\n| 日本語 | 内容 |\n\n![画像](" + asset + ")\n\n![欠損](asset://missing)\n\n```mermaid\nflowchart TD\n A --> B\n```");
            store.Create("同名(資料)", false, null, "第二ページ");
            var backup = store.Backup(root);
            using (var copy = new WorkspaceStore(backup))
                check(copy.Items().Count == 2 && copy.GetImage(asset)!.SequenceEqual(png), "バックアップに本文と画像を保持（WAL使用中）");
            {
                var result = DocxExport.WriteAsync(root, store.Items(), store.GetImage, _ => new MermaidResult(png, null)).GetAwaiter().GetResult();
                var files = Directory.GetFiles(result.Directory, "*.docx");
                check(files.Length == 2, "DOCX個別出力のファイル数");
                foreach (var file in files)
                {
                    using var doc = WordprocessingDocument.Open(file, false);
                    check(!new OpenXmlValidator().Validate(doc).Any(), "DOCX再オープン・スキーマ検証");
                    var body = doc.MainDocumentPart!.Document!.Body!;
                    var margins = body.GetFirstChild<W.SectionProperties>()!.GetFirstChild<W.PageMargin>()!;
                    check(margins.Top!.Value == 850 && margins.Bottom!.Value == 850 &&
                        margins.Left!.Value == 850 && margins.Right!.Value == 850,
                        "DOCX上下左右の余白15mm");
                    var textRuns = body.Descendants<W.Run>().Where(r => r.Elements<W.Text>().Any()).ToArray();
                    check(textRuns.All(r => r.RunProperties?.GetFirstChild<W.RunFonts>() is { } fonts &&
                        fonts.Ascii?.Value == "Meiryo UI" && fonts.HighAnsi?.Value == "Meiryo UI" &&
                        fonts.EastAsia?.Value == "Meiryo UI" && fonts.ComplexScript?.Value == "Meiryo UI"),
                        "DOCX日本語・英字・見出し・表・コードのフォントをMeiryo UIに統一");
                    var defaults = doc.MainDocumentPart.StyleDefinitionsPart!.Styles!
                        .GetFirstChild<W.DocDefaults>()!.GetFirstChild<W.RunPropertiesDefault>()!
                        .GetFirstChild<W.RunPropertiesBaseStyle>()!;
                    check(defaults.GetFirstChild<W.RunFonts>()!.EastAsia!.Value == "Meiryo UI" &&
                        defaults.GetFirstChild<W.FontSize>()!.Val!.Value == "20",
                        "DOCX文書既定書式もMeiryo UI・10pt");
                    check(textRuns.Where(r => (r.Parent as W.Paragraph)?.ParagraphProperties?.GetFirstChild<W.OutlineLevel>() is null)
                        .All(r => r.RunProperties!.GetFirstChild<W.FontSize>()?.Val?.Value == "20" &&
                            r.RunProperties.GetFirstChild<W.FontSizeComplexScript>()?.Val?.Value == "20"),
                        "DOCX本文・ページ名・表・コードは10pt");
                    if (!doc.MainDocumentPart!.Document!.InnerText.Contains("見出し")) continue;
                    var headings = body.Elements<W.Paragraph>()
                        .Where(p => p.ParagraphProperties?.GetFirstChild<W.OutlineLevel>() is not null).ToArray();
                    var sizes = new[] { "36", "32", "28", "24", "22", "20" };
                    check(headings.Length == 6 && headings.Select((p, i) =>
                        p.ParagraphProperties!.GetFirstChild<W.OutlineLevel>()!.Val!.Value == i &&
                        p.Elements<W.Run>().Where(r => r.Elements<W.Text>().Any()).All(r =>
                            r.RunProperties!.GetFirstChild<W.FontSize>()?.Val?.Value == sizes[i] &&
                            r.RunProperties.GetFirstChild<W.FontSizeComplexScript>()?.Val?.Value == sizes[i] &&
                            r.RunProperties.GetFirstChild<W.Bold>() is not null)).All(value => value),
                        "DOCX見出しH1〜H6は18・16・14・12・11・10ptの太字とアウトライン");
                    check(doc.MainDocumentPart!.ImageParts.Count() == 3, "DOCX画像・図・欠損代替画像を内部に埋め込み");
                    check(doc.MainDocumentPart!.Document!.Descendants<W.Table>().Count() == 1, "DOCX表を変換");
                    check(doc.MainDocumentPart!.Document!.Descendants<W.Bold>().Any() && doc.MainDocumentPart!.Document!.Descendants<W.Italic>().Any(), "DOCX強調書式");
                }
                check(result.MissingImages.Count == 1 && File.Exists(Path.Combine(result.Directory, "missing-images.txt")), "DOCX欠損画像を報告");
            }
            check(store.Items().First(d => d.Id == id).Body.Contains(asset), "DOCX出力後も元ページ保持");
        }
        finally { Directory.Delete(root, true); }
    }
}
