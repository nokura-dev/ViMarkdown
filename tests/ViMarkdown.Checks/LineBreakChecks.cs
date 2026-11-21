using Avalonia;
using ViMarkdown;

internal static class LineBreakChecks
{
    public static void Run(Action<bool, string> check)
    {
        foreach (var newline in new[] { "\n", "\r\n", "\r" })
        {
            var single = "前の段落" + newline + "次の段落";
            var paragraph = "前の段落" + newline + newline + "次の段落";
            var repeated = "前の段落" + newline + "  " + newline + newline + newline + "次の段落";
            using var lines = new RichTextLayout(Projection.Build(single), 1000);
            using var spaced = new RichTextLayout(Projection.Build(paragraph), 1000);
            using var collapsed = new RichTextLayout(Projection.Build(repeated), 1000);
            var next = paragraph.IndexOf("次の段落", StringComparison.Ordinal);
            var nextBounds = spaced.CaretBounds(next);
            check(nextBounds.Y > lines.CaretBounds(single.IndexOf("次の段落", StringComparison.Ordinal)).Y,
                "空白行は通常の行間に加えて段落間の余白を作る");
            check(Math.Abs(collapsed.CaretBounds(repeated.IndexOf("次の段落", StringComparison.Ordinal)).Y - nextBounds.Y) < 0.01,
                "連続する空白行と空白文字だけの行は一定の段落余白にまとめる");
            check(spaced.HitTest(new Point(nextBounds.X + 0.1, nextBounds.Center.Y)) == next &&
                spaced.SelectionBounds(0, paragraph.Length).Count >= 2,
                "段落余白を挟んでもクリックとビジュアル選択が原文に対応");
            var gap = Projection.Build(repeated).Single(u => u.Start == "前の段落".Length + newline.Length);
            check(gap.End == repeated.IndexOf("次の段落", StringComparison.Ordinal),
                "段落余白は省略した空白行全体の原文範囲を保持");
        }
        using (var code = new RichTextLayout(Projection.Build("```\n最初\n\n\n最後\n```"), 1000))
            check(code.Text.Contains("最初\n\n\n最後"), "コードブロック内の連続空白行はそのまま保持");

        foreach (var newline in new[] { "\n", "\r\n", "\r" })
            foreach (var suffix in new[] { "", "  ", "\\" })
            {
                var source = "日本語 **English**" + suffix + newline + "次の行です";
                var units = Projection.Build(source);
                using var layout = new RichTextLayout(units, 2000);
                check(layout.Text.Contains("English\n次の行です"), "通常改行と明示改行をリッチ表示でも保持");
                var second = source.IndexOf("次の行", StringComparison.Ordinal);
                var firstBounds = layout.CaretBounds(0);
                var secondBounds = layout.CaretBounds(second);
                check(secondBounds.Y > firstBounds.Y && Math.Abs(firstBounds.X - secondBounds.X) < 0.01,
                    "幅に余裕があっても原文の次行を行頭へ配置");
                check(units.Any(unit => unit.Text == "\n" && unit.Start == second - newline.Length && unit.End == second),
                    "改行の原文範囲はLF/CRLF/CR全体と対応");
                check(layout.HitTest(new Point(secondBounds.X + 0.1, secondBounds.Center.Y)) == second,
                    "改行後のクリック位置が原文の次行先頭と対応");
                check(layout.SelectionBounds(0, source.Length).Count >= 2, "改行をまたぐ選択は複数行を表示");
                check(layout.MoveVertical(0, 1) == second && layout.MoveVertical(second, -1) == 0,
                    "原文改行をまたぐ上下移動");
            }

    }
}
