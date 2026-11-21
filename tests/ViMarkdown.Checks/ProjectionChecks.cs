using ViMarkdown;

internal static class ProjectionChecks
{
    public static void Run(Action<bool, string> check)
    {
        const string markdown = "# 日本語\n\nこれは **太字** と *斜体* です。\n\n`code`\n";
        var units = Projection.Build(markdown);
        var fullSample = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "markdown-features-sample.md"));
        var codeUnits = Projection.Build(fullSample).Where(u => u.Code && !string.IsNullOrWhiteSpace(u.Text)).ToArray();
        var mismatch = codeUnits.FirstOrDefault(unit => fullSample[unit.Start..unit.End] != unit.Text);
        check(codeUnits.Length > 0 && mismatch is null,
            $"サンプル内の全コード文字で表示文字と原文位置を一致させる [offset={mismatch?.Start}]");
        check(string.Concat(units.Select(u => u.Text)).Contains("これは 太字 と 斜体 です。"), "隠した記号を除く本文");
        check(units.Any(u => u.Text == "太" && u.Bold && u.Start == markdown.IndexOf("太字", StringComparison.Ordinal)), "太字の原文位置");
        check(units.Any(u => u.Text == "斜" && u.Italic), "斜体");
        check(units.Any(u => u.Text == "日" && u.Size > 17), "見出し");
        check(units.Any(u => u.Text == "c" && u.Code && u.Start == markdown.IndexOf("code", StringComparison.Ordinal)), "コードの原文位置");
        check(units.All(u => u.Start >= 0 && u.End >= u.Start && u.End <= markdown.Length), "原文範囲の整合性");
        const string unicode = "A👩‍💻é日";
        var next = TextBoundaries.Next(unicode, 1);
        check(unicode[1..next] == "👩‍💻", "複合絵文字を一度で移動");
        check(TextBoundaries.Previous(unicode, next) == 1, "複合絵文字の逆移動");
        check(unicode[next..TextBoundaries.Next(unicode, next)] == "é", "結合文字を一度で移動");
        check(TextBoundaries.Next(unicode, unicode.Length) == unicode.Length && TextBoundaries.Previous(unicode, 0) == 0, "ページ端");
        foreach (var sample in new[] { "", "\n", "# 見出し\r\n\r\n本文", "```cs\nvar x = 1;\n```", "![画像](asset://x)", "**未完", "- リスト\n  - 入れ子" })
        {
            var projected = Projection.Build(sample);
            check(projected.All(u => u.Start >= 0 && u.End <= sample.Length), $"境界ケース {sample.Replace('\n', ' ')}");
        }
    }
}
