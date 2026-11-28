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
            var initial = Path.Combine(root, "initial");
            var single = MarkdownExport.Write(initial, pages, store.GetImage);
            check(Directory.GetFiles(single.Directory, "*.md").Length == 2, "同名・パス記号を含むページを安全に個別出力");
            check(single.Images == 3 && Directory.GetFiles(single.Directory, "*.png", SearchOption.AllDirectories).Select(Path.GetFileName).Order().SequenceEqual(new[] { "_同名(1)-1.png", "_同名(1)-2.png", "_同名(2)-1.png" }), "画像はページごとに本文の登場順で採番");
            check(Directory.GetFiles(single.Directory, "*.md").Select(Path.GetFileName).Order().SequenceEqual(new[] { "_同名.md", "_同名_1.md" }.Order()), "ページファイル名はページ名と重複時のアンダスコア連番");
            check(single.Directory == initial, "選択フォルダ直下へ出力");
            var original = File.ReadAllText(Path.Combine(initial, "_同名.md"));
            var collision = MarkdownExport.Write(initial, [pages[1]], store.GetImage);
            check(collision.PageFiles[2] == "_同名_2.md" && File.ReadAllText(Path.Combine(initial, "_同名.md")) == original,
                "既存の本文と画像フォルダを保持してアンダスコア連番で出力");
            var text = File.ReadAllText(Directory.GetFiles(single.Directory, "*.md").Single(p => Path.GetFileName(p) == "_同名.md"));
            check(text.Contains("本文 **太字**\r\n") && text.Contains($"`![コード]({url})`") && text.Contains($"![例]({url})"), "書式・改行・コード中の画像表記を保持");
            var moved = Path.Combine(root, "移動先");
            Directory.Move(single.Directory, moved);
            foreach (var path in Directory.GetFiles(moved, "*.md"))
            {
                if (Path.GetFileName(path) == "_同名_2.md") continue;
                var images = Markdown.Parse(File.ReadAllText(path)).Descendants<LinkInline>().Where(l => l.IsImage).ToArray();
                check(images.Length > 0 && images.All(l => l.Url!.StartsWith(Path.GetFileNameWithoutExtension(path) + "/") && File.ReadAllBytes(Path.Combine(moved, Uri.UnescapeDataString(l.Url!))).SequenceEqual(png)), "mdとページ別画像フォルダを移動後も画像参照が成立");
            }
            var links = Directory.GetFiles(moved, "*.md").Where(path => Path.GetFileName(path) != "_同名_2.md").SelectMany(path => Markdown.Parse(File.ReadAllText(path)).Descendants<LinkInline>()).ToArray();
            check(links.Count(l => l.IsImage && l.Url!.StartsWith("_同名")) == 3 && links.Any(l => !l.IsImage && l.Url == "https://example.com"), "個別ページの同じ参照ラベルの画像と外部リンクを正しく出力");
            {
                var incomplete = MarkdownExport.Write(Path.Combine(root, "missing"), [pages[0] with { Body = "本文\n\n![欠損](asset://sample)\n\n![正常](" + url + ")" }], store.GetImage);
                var exported = File.ReadAllText(Directory.GetFiles(incomplete.Directory, "*.md").Single());
                var exportedImages = Markdown.Parse(exported).Descendants<LinkInline>().Where(l => l.IsImage).ToArray();
                check(incomplete.MissingImages.SequenceEqual(new[] { "asset://sample" }) && exported.Contains("本文") && exportedImages.Length == 2 &&
                    exportedImages.All(l => File.Exists(Path.Combine(incomplete.Directory, Uri.UnescapeDataString(l.Url!)))), "欠損画像があっても個別出力は本文と全画像参照を保存");
                check(File.ReadAllText(Path.Combine(incomplete.Directory, incomplete.ErrorReports[1])).Contains("asset://sample") &&
                    File.ReadAllBytes(Path.Combine(incomplete.Directory, "_同名", "_同名(1)-missing-1.png")).Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }) &&
                    File.Exists(Path.Combine(incomplete.Directory, "_同名", "_同名(1)-2.png")), "欠損参照の一覧と代替画像を出力");
            }
            {
                var readable = MarkdownExport.Write(Path.Combine(root, "readable"),
                    [pages[0] with { Name = "議事録 空白　日本語(案)#100%[図]&", Body = $"![画像]({url})\n\n![参照][pic]\n\n[pic]: {url}\n\n![欠落](asset://sample)" }],
                    store.GetImage);
                var exported = File.ReadAllText(Directory.GetFiles(readable.Directory, "*.md").Single());
                check(exported.Contains("議事録%20空白%E3%80%80日本語%28案%29%23100%25%5B図%5D%26/議事録%20空白%E3%80%80日本語%28案%29%23100%25%5B図%5D%26%281%29-1.png") &&
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
