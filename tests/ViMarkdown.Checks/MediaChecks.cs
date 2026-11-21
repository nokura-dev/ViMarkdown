using Avalonia;
using Avalonia.Media.Imaging;
using ViMarkdown;

internal static class MediaChecks
{
    public static void Run(Action<bool, string> check)
    {
        const string source = "前文\n\n| 見出し | 説明 |\n| --- | --- |\n| **太字** | 日本語 👩‍💻 |\n\n後文";
        var units = Projection.Build(source);
        var table = units.Single(u => u.Table is not null);
        check(table.Table!.Count == 2 && table.Table[0].Count == 2, "表の見出しと本文を解析");
        check(table.Table[1][0].Any(u => u.Bold), "表内のインライン装飾を保持");
        using var layout = new RichTextLayout(units, 400);
        var offset = source.IndexOf("日本語", StringComparison.Ordinal);
        var caret = layout.CaretBounds(offset);
        check(caret.X > 200 && layout.HitTest(new Point(caret.X + .1, caret.Center.Y)) == offset,
            "表内のセルのクリックと原文位置が一致");
        var headerOffset = source.IndexOf("説明", StringComparison.Ordinal);
        check(layout.CaretBounds(layout.MoveVertical(headerOffset, 1)).Y > layout.CaretBounds(headerOffset).Y,
            "表内の上下移動はセルの描画位置に対応");
        check(layout.SelectionBounds(offset, offset + 3).Count > 0, "表内の選択範囲");
        using var narrow = new RichTextLayout(units, 180);
        check(narrow.Layout.Height >= layout.Layout.Height, "表を狭めるとセルを折り返して高さを確保");
        foreach (var mermaid in new[] { false, true })
        {
            using var tallBitmap = new RenderTargetBitmap(new PixelSize(160, 280));
            using var stream = new MemoryStream();
            tallBitmap.Save(stream);
            var tallPng = stream.ToArray();
            var media = mermaid ? "```mermaid\nflowchart TD\n A --> B\n```" : "![画像](asset://test)";
            var page = "前文\n\n" + media + "\n\n後文";
            using var mediaLayout = new RichTextLayout(Projection.Build(page), 400, _ => tallPng,
                mermaidResolver: _ => new MermaidResult(tallPng, null));
            var imageStart = page.IndexOf(media, StringComparison.Ordinal);
            var after = page.IndexOf("後文", StringComparison.Ordinal);
            var position = 0;
            var visitedImage = false;
            for (var i = 0; i < 30 && position < after; i++)
            {
                visitedImage |= position >= imageStart && position < imageStart + media.Length;
                var next = mediaLayout.MoveVertical(position, 1);
                if (next == position) break;
                position = next;
            }
            check(visitedImage && position >= after, $"{(mermaid ? "図" : "画像")}の行頭で停止せずjで後文へ移動");
            position = after;
            for (var i = 0; i < 30 && position > 0; i++)
            {
                var next = mediaLayout.MoveVertical(position, -1);
                if (next == position) break;
                position = next;
            }
            check(position == 0, $"{(mermaid ? "図" : "画像")}を越えてkで前文へ移動");
        }
        var path = Path.Combine(Path.GetTempPath(), "markdown-media-" + Guid.NewGuid() + ".db");
        string url;
        byte[] png;
        using (var bitmap = new RenderTargetBitmap(new PixelSize(32, 24)))
        using (var stream = new MemoryStream()) { bitmap.Save(stream); png = stream.ToArray(); }
        try
        {
            using (var store = new WorkspaceStore(path))
            {
                url = store.AddImage(png);
                store.Create("表と画像", false, null, source + $"\n\n![画像]({url})");
            }
            using (var store = new WorkspaceStore(path))
            {
                check(store.GetImage(url)!.SequenceEqual(png), "DB再接続後も画像のバイト列を復元");
                var body = store.Items().Single().Body;
                var projected = Projection.Build(body);
                check(projected.Any(u => u.Table is not null) && projected.Any(u => u.ImageUrl == url), "保存したページから表と画像参照を再構築");
                var surface = new RichSurface { ImageResolver = store.GetImage };
                surface.SetSource(body);
                surface.Measure(new Size(400, double.PositiveInfinity));
                surface.Arrange(new Rect(new Size(400, surface.DesiredSize.Height)));
                using var rendered = new RenderTargetBitmap(new PixelSize(400, (int)surface.DesiredSize.Height));
                rendered.Render(surface);
                check(surface.DesiredSize.Height > layout.Layout.Height, "SQLite画像と表を同一ページへ描画");
                using var missing = new RichTextLayout(Projection.Build("![欠損](asset://missing)"), 200, store.GetImage);
                check(missing.Layout.Height >= 84, "欠損画像にも表示領域を確保");
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix);
        }
    }
}
