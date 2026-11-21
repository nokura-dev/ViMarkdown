using ViMarkdown;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

internal static class ExportChecks
{
    public static void Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "markdown-export-check-" + Guid.NewGuid().ToString("N"));
        using var store = new WorkspaceStore(":memory:");
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
        var url = store.AddImage(png);
        var body = $"本文 **太字**\r\n\r\n![図]({url} \"説明\")\r\n\r\n![参照][pic]\r\n\r\n[pic]: <{url}> \"参照の説明\"\r\n\r\n`![コード]({url})`\r\n\r\n```md\r\n![例]({url})\r\n```\r\n";
        var pages = new[] { new WorkspaceItem(1, null, "../同名", false, body), new WorkspaceItem(2, null, "../同名", false, "[外部][pic]\n\n[pic]: https://example.com\n\n![同じ](" + url + ")") };
        try
        {
            var single = MarkdownExport.Write(root, pages, store.GetImage);
            check(Directory.GetFiles(single.Directory, "*.md").Length == 2, "同名・パス記号を含むページを安全に個別出力");
            check(single.Images == 3 && Directory.GetFiles(Path.Combine(single.Directory, "assets")).Select(Path.GetFileName).Order().SequenceEqual(new[] { "_同名(1)-1.png", "_同名(1)-2.png", "_同名(2)-1.png" }), "画像はページごとに本文の登場順で採番");
            check(Directory.GetFiles(single.Directory, "*.md").Select(Path.GetFileName).Order().SequenceEqual(new[] { "_同名(1).md", "_同名(2).md" }), "ページファイル名はページ名と括弧付きID");
            check(System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(single.Directory), @"^MarkdownExport-\d{14}$"), "出力フォルダ名は年月日時分秒");
            // Occupy every timestamp in the test window to exercise collision suffixes deterministically.
            for (var second = -2; second <= 20; second++)
            {
                var occupied = Path.Combine(root, "MarkdownExport-" + DateTime.Now.AddSeconds(second).ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture));
                Directory.CreateDirectory(occupied);
                File.WriteAllText(Path.Combine(occupied, "keep.txt"), "keep");
            }
            var collision = MarkdownExport.Write(root, [pages[1]], store.GetImage);
            check(System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(collision.Directory), @"^MarkdownExport-\d{14}-2$") && File.ReadAllText(Path.Combine(collision.Directory[..^2], "keep.txt")) == "keep", "同じ秒の出力は連番を付け既存フォルダを保持");
            var text = File.ReadAllText(Directory.GetFiles(single.Directory, "*.md").Single(p => p.EndsWith("(1).md")));
            check(text.Contains("本文 **太字**\r\n") && text.Contains($"`![コード]({url})`") && text.Contains($"![例]({url})"), "書式・改行・コード中の画像表記を保持");
            var moved = Path.Combine(root, "移動先");
            Directory.Move(single.Directory, moved);
            foreach (var path in Directory.GetFiles(moved, "*.md"))
            {
                var images = Markdown.Parse(File.ReadAllText(path)).Descendants<LinkInline>().Where(l => l.IsImage).ToArray();
                check(images.Length > 0 && images.All(l => l.Url!.StartsWith("assets/") && File.ReadAllBytes(Path.Combine(moved, Uri.UnescapeDataString(l.Url!))).SequenceEqual(png)), "mdとassetsを移動後も画像参照が成立");
            }
            var links = Directory.GetFiles(moved, "*.md").SelectMany(path => Markdown.Parse(File.ReadAllText(path)).Descendants<LinkInline>()).ToArray();
            check(links.Count(l => l.IsImage && l.Url!.StartsWith("assets/")) == 3 && links.Any(l => !l.IsImage && l.Url == "https://example.com"), "個別ページの同じ参照ラベルの画像と外部リンクを正しく出力");
            {
                var incomplete = MarkdownExport.Write(root, [pages[0] with { Body = "本文\n\n![欠損](asset://sample)\n\n![正常](" + url + ")" }], store.GetImage);
                var exported = File.ReadAllText(Directory.GetFiles(incomplete.Directory, "*.md").Single());
                var exportedImages = Markdown.Parse(exported).Descendants<LinkInline>().Where(l => l.IsImage).ToArray();
                check(incomplete.MissingImages.SequenceEqual(new[] { "asset://sample" }) && exported.Contains("本文") && exportedImages.Length == 2 &&
                    exportedImages.All(l => File.Exists(Path.Combine(incomplete.Directory, Uri.UnescapeDataString(l.Url!)))), "欠損画像があっても個別出力は本文と全画像参照を保存");
                check(File.ReadAllText(Path.Combine(incomplete.Directory, "missing-images.txt")).Contains("asset://sample") &&
                    File.ReadAllBytes(Path.Combine(incomplete.Directory, "assets", "_同名(1)-missing-1.png")).Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
                    File.Exists(Path.Combine(incomplete.Directory, "assets", "_同名(1)-2.png")), "欠損参照の一覧と代替画像を出力");
            }
            {
                var readable = MarkdownExport.Write(root,
                    [pages[0] with { Name = "議事録 空白　日本語(案)#100%[図]&", Body = $"![画像]({url})\n\n![参照][pic]\n\n[pic]: {url}\n\n![欠落](asset://sample)" }],
                    store.GetImage);
                var exported = File.ReadAllText(Directory.GetFiles(readable.Directory, "*.md").Single());
                check(exported.Contains("assets/議事録%20空白%E3%80%80日本語%28案%29%23100%25%5B図%5D%26%281%29-1.png") &&
                    exported.Contains("-missing-3.png"), "個別出力の画像リンクは日本語を保持し空白・括弧・予約文字だけエンコード");
                var imageLinks = Markdown.Parse(exported).Descendants<LinkInline>().Where(l => l.IsImage).ToArray();
                var relocated = Path.Combine(root, "moved-" + Guid.NewGuid().ToString("N"));
                Directory.Move(readable.Directory, relocated);
                check(imageLinks.Length == 3 && imageLinks.All(l => File.Exists(Path.Combine(relocated, Uri.UnescapeDataString(l.Url!)))),
                    "特殊文字と日本語を含む通常・参照形式・欠落画像リンクが移動後も解決");
            }
            check(store.GetImage(url)!.SequenceEqual(png) && pages[0].Body == body, "出力は元ページとDB画像を変更しない");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
