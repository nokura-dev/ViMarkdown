using Avalonia;
using Avalonia.Media.Imaging;
using ViMarkdown;

internal static class D2PolicyChecks
{
    public static void Run(Action<bool, string> check)
    {
        void Reject(string source, string message)
        {
            try { D2Policy.Prepare(source); check(false, message); }
            catch (Exception e) when (e is FormatException or IOException or UnauthorizedAccessException)
            { check(true, message); }
        }
        D2Policy.Prepare("# import @x\nnode: \"layers scenarios steps https://example.com @x\"\n");
        check(true, "コメント・通常ラベル内の禁止語とURLは許可");
        foreach (var board in new[] { "layers", "scenarios", "steps" })
        {
            Reject(board + ": { detail: { x -> y } }", board + "ボードを拒否");
            Reject("group: { \"" + board + "\": {} }", "入れ子・引用キーのボードを拒否");
        }
        foreach (var source in new[] { "...@other", "node: @other", "a: {icon: \"https://example.com/a.png\"}",
            "|icon|: \"https://example.com/a.png\"",
            "a.icon: \"http://example.com/a.png\"", "a: {icon: \"relative.png\"}",
            "a: {icon: \"/tmp/icon.svg\"}", "a: {icon: \"//server/icon.png\"}",
            "a: {icon: \"data:image/png;base64,abc\"}", "a: {icon: ${image}}",
            "classes: { c: {icon: \"https://example.com/a.png\"} }", "*.icon: \"https://example.com/a.png\"",
            "a: |md\n![image](https://example.com/a.png)\n|", "a: |md\n<img src='https://example.com/a.png'>\n|" })
            Reject(source, "外部参照・隠れた参照を拒否: " + source);
        check(Projection.Build("```mermaid\nA-->B\n```").All(x => x.D2Code is null), "旧MermaidはD2として実行しない");
        check(Projection.Build("```D2\na -> b\n```\n\n```d2\nc -> d\n```").Count(x => x.D2Code is not null) == 2,
            "専用フェンス・大文字と複数フェンスに対応");
        var root = Path.Combine(Path.GetTempPath(), "d2 policy 日本語 " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "画像.PNG");
            using (var bitmap = new RenderTargetBitmap(new PixelSize(12, 8))) bitmap.Save(path);
            string Quote(string p) => "\"" + p.Replace("\\", "\\\\") + "\"";
            var code = "a.icon: " + Quote(path);
            var prepared = D2Policy.Prepare(code);
            check(prepared.Images.Count == 1 && !prepared.Code.Contains(path) && prepared.Code.Contains("image-0.png"),
                "絶対パスPNGを検査し所有するスナップショット参照へ変換");
            using (var bitmap = new RenderTargetBitmap(new PixelSize(14, 10))) bitmap.Save(path);
            var changed = D2Policy.Prepare(code);
            check(changed.Key != prepared.Key, "参照画像の内容変更で描画キャッシュを更新");
            var jpg = Path.Combine(root, "写真.jpg");
            using (var pixels = new SkiaSharp.SKBitmap(16, 12))
            using (var image = SkiaSharp.SKImage.FromBitmap(pixels))
            using (var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90))
            using (var file = File.Create(jpg)) data.SaveTo(file);
            check(D2Policy.Prepare("a.icon: " + Quote(jpg)).Images.Count == 1, "JPEG画像の絶対パスを許可");
            File.WriteAllText(path, "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");
            Reject(code, "PNG拡張子に偽装したSVGを拒否");
            File.Delete(path);
            Reject(code, "存在しない画像をCLI起動前に検出");
            var png = new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 };
            File.WriteAllBytes(path, png);
            var renderer = new D2Renderer();
            var result = renderer.RenderAsync(code).GetAwaiter().GetResult();
            check(!result.Success, "ヘッダーだけの破損画像は描画成功にならない");
        }
        finally { Directory.Delete(root, true); }
        check(File.Exists(Path.Combine(RuntimeAssets.FontDirectory, "migmix-1m-regular.ttf")) &&
            File.Exists(Path.Combine(RuntimeAssets.FontDirectory, "migmix-1m-bold.ttf")), "外部フォントを配置");
        check(Typography.BodyTypeface.GlyphTypeface.TryGetGlyph('漢', out var glyph) && glyph != 0,
            "共有外部フォントで日本語グリフを読み込める");
    }
}
