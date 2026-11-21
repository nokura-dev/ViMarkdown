using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using System.Security.Cryptography;
using ViMarkdown;

internal static class LayoutChecks
{
    private const string mixed = "# Markdownエディタ 技術検証\n\nこれは **太字** と *斜体*、`inline code` の表示テストです。\n\n日本語 English 👩‍💻 é 日本語\n\n操作 h j k l と Esc と D2 を確認します。";

    public static void Run(Action<bool, string> check, string? renderPath)
    {
        foreach (var (file, weight) in new[]
        {
            ("regular", FontWeight.Normal),
            ("bold", FontWeight.Bold)
        })
        {
            using var asset = File.OpenRead(Path.Combine(RuntimeAssets.FontDirectory, $"migmix-1m-{file}.ttf"));
            var actualHash = Convert.ToHexString(SHA256.HashData(asset));
            // Compare against the upstream font, not the host's installed fonts.
            var source = Path.Combine(CheckInputs.FindRoot(), "src", "ViMarkdown", "Assets", "Fonts", "MigMix1M", $"migmix-1m-{file}.ttf");
            using var original = File.OpenRead(source);
            check(actualHash == Convert.ToHexString(SHA256.HashData(original)), $"{file}の外部共有フォントは元のTTFと一致する");
            var face = new Typeface(Typography.BodyFont, weight: weight).GlyphTypeface;
            check(face.FamilyName == "MigMix 1M" && face.Weight == weight && face.FontSimulations == FontSimulations.None,
                $"{file}は同梱MigMix 1Mの実際のウェイトを使用する（代替・疑似太字なし）: {face.FamilyName}, {face.Weight}, {face.FontSimulations}");
            check("Agjpqy日本語更新日時".All(c => face.TryGetGlyph(c, out var glyph) && glyph != 0),
                $"{file}は日本語・英数字を代替フォントなしで表示できる");
        }
        using (var latin = new TextLayout("Agjpqy", Typography.BodyTypeface, 14, Brushes.Black))
        using (var japanese = new TextLayout("日本語", Typography.BodyTypeface, 14, Brushes.Black))
            check(Math.Abs(latin.Height - japanese.Height) < .01 &&
                Math.Abs(latin.TextLines[0].Baseline - japanese.TextLines[0].Baseline) < .01,
                "共通フォントの英数字と日本語は行高・基準線が揃う");
        using (var bodySpacing = new RichTextLayout(Projection.Build("本文 English\n次の行\n最後の行"), 1000))
        using (var headingSpacing = new RichTextLayout(Projection.Build("# 見出し\n本文"), 1000))
        {
            var bodyHeight = bodySpacing.Layout.TextLines[0].Height;
            check(bodyHeight >= Typography.Body && bodyHeight < Typography.Body * 2,
                "リッチ表示の本文行高は文字サイズに応じた範囲に収まる");
            check(bodySpacing.Layout.TextLines.Take(3).All(line => Math.Abs(line.Height - bodyHeight) < 0.1),
                "ノーマル・ビジュアル共通の本文行高は一定");
            check(headingSpacing.Layout.TextLines[0].Height > bodyHeight &&
                headingSpacing.Layout.TextLines[0].Height >= 30 * Typography.RichScale,
                "見出し行は大きい文字を収める高さを確保");
        }

        // Fixed block spacing must not create a line or a cursor stop.
        foreach (var prefix in new[] { "# ", "## ", "### ", "- ", "1. ", "- [ ] " })
        foreach (var width in new[] { 110d, 1000d })
        foreach (var lineHeight in new[] { double.NaN, 32d })
        {
            var source = prefix + "長い見出しまたはリストの末尾 English 日本語\n\n後続";
            var units = Projection.Build(source);
            using var spaced = new RichTextLayout(units, width, lineHeight: lineHeight);
            using var plain = new RichTextLayout(units.Select(u => u with { BottomSpacing = 0 }).ToArray(), width, lineHeight: lineHeight);
            var next = source.IndexOf("後続", StringComparison.Ordinal);
            var rect = spaced.CaretBounds(next);
            check(spaced.Text == plain.Text && spaced.Layout.TextLines.Count == plain.Layout.TextLines.Count,
                "固定余白は表示文字・空行を増やさない");
            check(Math.Abs(spaced.Height - plain.Height - 6) < .001 &&
                Math.Abs(rect.Y - plain.CaretBounds(next).Y - 6) < .001,
                "見出し・リストの末尾余白は折り返し・行高によらず6px");
            check(spaced.HitTest(new Point(rect.X + .1, rect.Center.Y)) == next,
                "余白後のクリック位置とカーソル位置が一致");
            check(spaced.SelectionBounds(next, source.Length).Any(r => Math.Abs(r.Center.Y - rect.Center.Y) < .001),
                "余白後の選択範囲とカーソル位置が一致");
            var previous = spaced.MoveVertical(next, -1);
            check(previous < next && spaced.MoveVertical(previous, 1) == next,
                "上下移動は固定余白を空行として数えない");
        }
        foreach (var source in new[] { "- 一つ\n- 二つ", "1. 一つ\n2. 二つ", "- 親\n  - 子\n- 末尾" })
        {
            var units = Projection.Build(source);
            check(units.Count(u => u.BottomSpacing > 0) == 1,
                "リスト項目ごとではなくリスト全体の最下行にだけ余白を付ける");
        }

        using (var layout = new RichTextLayout(Projection.Build(mixed), 1000))
        {
            var headingLatin = layout.CaretBounds(mixed.IndexOf("Markdown", StringComparison.Ordinal));
            var headingJapanese = layout.CaretBounds(mixed.IndexOf("エディタ", StringComparison.Ordinal));
            check(headingLatin.Y == headingJapanese.Y && headingLatin.Height == headingJapanese.Height,
                "英字と日本語の見出しが同一行のレイアウトを共有");
            var japanese = layout.CaretBounds(mixed.IndexOf("これは", StringComparison.Ordinal));
            var codeStart = mixed.IndexOf("inline code", StringComparison.Ordinal);
            var code = layout.CaretBounds(codeStart);
            check(Math.Abs(japanese.Center.Y - code.Center.Y) < 0.01 && japanese.Height == Typography.Body && code.Height == Typography.Body, "本文とインラインコードが同じ行に配置される");
            check(layout.HitTest(new Point(code.X + 0.1, code.Center.Y)) == codeStart,
                "インラインコードの描画位置から原文へクリック対応");
            var selection = layout.SelectionBounds(codeStart, codeStart + "inline code".Length);
            check(selection.Count == 1 && Math.Abs(selection[0].Center.Y - code.Center.Y) < 0.01 && selection[0].Width > 0,
                "選択範囲とカーソルは同じ行の中央に配置");
            var emoji = mixed.IndexOf("👩‍💻", StringComparison.Ordinal);
            var emojiRect = layout.CaretBounds(emoji);
            var nextRect = layout.CaretBounds(emoji + "👩‍💻".Length);
            var picked = layout.HitTest(new Point((emojiRect.X + nextRect.X) / 2, emojiRect.Center.Y));
            check(picked == emoji || picked == emoji + "👩‍💻".Length, "絵文字のクリックは書記素の両端に限定");
            var last = layout.CaretBounds(mixed.Length);
            check(last.Height > 0 && double.IsFinite(last.Y), "末尾のカーソル領域");
        }

        for (var level = 1; level <= 6; level++)
        {
            var source = new string('#', level) + " 見出し\n\n本文";
            using var layout = new RichTextLayout(Projection.Build(source), 1000);
            var heading = layout.CaretBounds(source.IndexOf("見出し", StringComparison.Ordinal));
            var body = layout.CaretBounds(source.IndexOf("本文", StringComparison.Ordinal));
            check(Math.Abs(heading.Height - (32 - level * 2) * Typography.RichScale) < 0.01 && body.Height == Typography.Body,
                $"見出し{level}と本文のカーソルは各フォントサイズの高さ");
            var line = layout.Layout.HitTestTextPosition(layout.ToDisplayOffset(source.IndexOf("見出し", StringComparison.Ordinal)));
            check(Math.Abs(heading.Center.Y - line.Center.Y) < 0.01, $"見出し{level}のカーソルは行の中央に配置");
        }

        // All heading sizes at EOF, plus each line-ending form on the largest heading.
        foreach (var (level, ending) in Enumerable.Range(1, 6).Select(level => (level, ""))
            .Concat(new[] { (1, "\n"), (1, "\r\n"), (1, "\n\n本文"), (1, "\r\n\r\n本文") }))
        {
            var headingText = new string('#', level) + " 日本語 👩‍💻 é **見出し**";
            var source = headingText + ending;
            using var layout = new RichTextLayout(Projection.Build(source), 1000);
            var start = layout.CaretBounds(source.IndexOf("日本語", StringComparison.Ordinal));
            var end = layout.CaretBounds(headingText.Length);
            check(Math.Abs(end.Height - start.Height) < 0.01 && Math.Abs(end.Y - start.Y) < 0.01,
                $"見出し{level}の行末も文字と同じカーソル高さ・位置（末尾{ending.Length}文字）");
            if (ending.StartsWith("\n\n") || ending.StartsWith("\r\n\r\n"))
            {
                var blank = layout.CaretBounds(headingText.Length + (ending.StartsWith("\r") ? 2 : 1));
                check(blank.Height == Typography.Body, "見出し直後の空行は本文のカーソル高さ");
            }
        }

        using (var wrapped = new RichTextLayout(Projection.Build(mixed), 220))
        {
            var unwrapped = new RichTextLayout(Projection.Build(mixed), 1000);
            using (unwrapped)
                check(wrapped.Layout.TextLines.Count > unwrapped.Layout.TextLines.Count, "幅の変更で折り返しを再計算");
            var first = mixed.IndexOf("Markdown", StringComparison.Ordinal);
            var down = wrapped.MoveVertical(first, 1);
            check(wrapped.CaretBounds(down).Y > wrapped.CaretBounds(first).Y, "上下移動は実際の折り返し行を使用");
            var selected = wrapped.SelectionBounds(0, mixed.Length);
            check(selected.Count > 4 && selected.All(rect => rect.Width >= 0 && rect.Height > 0),
                "複数行にまたがる選択領域");
        }
        using (var empty = new RichTextLayout(Projection.Build(""), 220))
        {
            check(empty.CaretBounds(0).Height > 0 && empty.HitTest(new Point(0, 0)) == 0, "空ページの描画とクリック");
        }

        // Optional artifact: actual drawing, not a mockup, for visual regression review.
        if (renderPath is not null)
        {
            var surface = new RichSurface { Caret = mixed.IndexOf("inline code", StringComparison.Ordinal) };
            surface.SetSource(mixed);
            surface.Measure(new Size(1000, double.PositiveInfinity));
            surface.Arrange(new Rect(new Size(1000, surface.DesiredSize.Height)));
            using var bitmap = new RenderTargetBitmap(new PixelSize(1000, (int)Math.Ceiling(surface.DesiredSize.Height)), new Vector(96, 96));
            bitmap.Render(surface);
            bitmap.Save(renderPath);
            Console.WriteLine($"Rendered {renderPath}");
        }
    }
}
