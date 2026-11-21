using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ViMarkdown;

internal static class D2Checks
{
    public static void Run(Action<bool, string> check, bool integration)
    {
        const string source = "前\r\n\r\n```d2\r\ndirection: right\r\n 開始 -> 完了\r\n```\r\n\r\n後";
        var units = Projection.Build(source);
        var diagram = units.Single(u => u.D2Code is not null);
        check(diagram.Start == source.IndexOf("```", StringComparison.Ordinal) && source[(diagram.End - 3)..diagram.End] == "```", "D2はフェンス全体の原文範囲を保持");
        check(diagram.D2Code!.Contains("開始 -> 完了") && !diagram.D2Code.Contains("```"), "D2の内容をCRLFから抽出");
        check(Projection.Build("```cs\nflowchart LR\n```\n\n`d2`").All(u => u.D2Code is null), "他言語とインラインコードをD2として扱わない");
        check(Projection.Build("~~~d2\ndirection: down\nA -> B\n~~~").Any(u => u.D2Code is not null), "チルダのフェンスにも対応");
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
        var calls = 0;
        var renderer = new D2Renderer(async code =>
        {
            Interlocked.Increment(ref calls);
            await Task.Delay(5);
            return code == "bad" ? new(null, "D2構文エラー: test") : new(png, null);
        });
        var a = renderer.RenderAsync("same");
        var b = renderer.RenderAsync("same");
        Task.WaitAll(a, b);
        check(ReferenceEquals(a, b) && calls == 1 && a.Result.Success, "同じコードの同時要求は1回の描画とキャッシュを共有");
        renderer.RenderAsync("changed").GetAwaiter().GetResult();
        check(calls == 2, "コード変更時は再描画");
        var bad = renderer.RenderAsync("bad").GetAwaiter().GetResult();
        check(!bad.Success && renderer.RenderAsync("bad").Result == bad && calls == 3, "構文エラーもキャッシュ");
        check(!renderer.RenderAsync(new string('x', 100001)).Result.Success && calls == 3, "巨大なコードを描画前に制限");
        using (var layout = new RichTextLayout(units, 400, d2Resolver: _ => new(png, null)))
        {
            var top = layout.CaretBounds(diagram.Start).Y;
            check(layout.HitTest(new Point(10, top + 10)) == diagram.Start && layout.HitTest(new Point(390, top + 10)) == diagram.End,
                "図のクリックは原文ブロックの両端に対応");
            check(layout.SelectionBounds(diagram.Start, diagram.End).Count > 0, "図をビジュアル選択できる");
        }
        using (var layout = new RichTextLayout(units, 240, d2Resolver: _ => bad))
            check(layout.Layout.Height > 0, "構文エラーをレイアウトできる");
        var root = Path.Combine(Path.GetTempPath(), "d2-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            var pages = new[] { new WorkspaceItem(10, null, "フロー", false, source), new WorkspaceItem(11, null, "エラー", false, "```d2\nbad\n```\n") };
            var output = MarkdownExport.Write(root, pages, _ => null, code => code.Contains("bad") ? bad : new(png, null));
            var body = File.ReadAllText(Path.Combine(output.Directory, "フロー(10).md"));
            check(output.Images == 1 && body.Contains("![D2](assets/フロー%2810%29-d2-1.png)"), "個別出力でD2をPNG参照へ変換");
            check(File.ReadAllBytes(Path.Combine(output.Directory, "assets", "フロー(10)-d2-1.png")).SequenceEqual(png), "出力画像のバイト列を保持");
            check(output.D2Errors.Count == 1 && File.ReadAllText(Path.Combine(output.Directory, "エラー(11).md")).Contains("```d2\nbad\n```") && File.Exists(Path.Combine(output.Directory, "d2-errors.txt")), "失敗した図はコードを保持してエラー一覧を出力");
            check(pages[0].Body == source, "画像出力で元のD2コードを変更しない");
            if (integration)
            {
                var real = D2Renderer.Shared.RenderAsync(diagram.D2Code).GetAwaiter().GetResult();
                check(real.Success, "公式D2で日本語フローチャートをPNG化: " + real.Error);
                using var bitmap = new Bitmap(new MemoryStream(real.Png!));
                check(bitmap.PixelSize.Width > 10 && bitmap.PixelSize.Height > 10, "実際の生成画像をAvaloniaで読み込める");
                var surface = new RichSurface();
                surface.SetSource(source);
                Dispatcher.UIThread.RunJobs();
                surface.Measure(new Size(800, double.PositiveInfinity));
                surface.Arrange(new Rect(new Size(800, surface.DesiredSize.Height)));
                using (var preview = new RenderTargetBitmap(new PixelSize(800, (int)Math.Ceiling(surface.DesiredSize.Height))))
                {
                    preview.Render(surface);
                    preview.Save(Path.Combine(Path.GetTempPath(), "ViMarkdown-d2.png"));
                }
                check(surface.DesiredSize.Height > 100, "非同期完了後のRichSurfaceを画像として描画");
                var invalid = D2Renderer.Shared.RenderAsync("A: {").GetAwaiter().GetResult();
                check(!invalid.Success && invalid.Error!.Contains("エラー"), "公式D2の構文エラーを識別: " + invalid.Error);
                var imagePath = Path.Combine(root, "参照 画像.png");
                using (var image = new RenderTargetBitmap(new PixelSize(16, 12))) image.Save(imagePath);
                var imageCode = "a: { icon: \"" + imagePath.Replace("\\", "\\\\") + "\" }";
                var withImage = D2Renderer.Shared.RenderAsync(imageCode).GetAwaiter().GetResult();
                check(withImage.Success, "同梱CLIで日本語・空白入り絶対パスのローカルPNGを描画: " + withImage.Error);
                var blocked = D2Renderer.Shared.RenderAsync("layers: { detail: { a -> b } }").GetAwaiter().GetResult();
                check(!blocked.Success && blocked.Error!.Contains("Layers"), "実描画経路でも複数ボードを拒否");
                var exported = MarkdownExport.Write(root, [pages[0]], _ => null);
                check(exported.Images == 1 && exported.D2Errors.Count == 0, "公式D2描画結果で実際にMarkdown出力");
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
