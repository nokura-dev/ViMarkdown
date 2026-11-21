using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ViMarkdown;

internal static class MarkdownHighlightChecks
{
    public static void Run(Action<bool, string> check, string? renderPath)
    {
        using var session = new EditorSession();
        var window = session.Window;
        session.Click("INSERT (i)");
        var editor = session.Editor;
        const string sample = "# Markdown サンプル\n\n## 今日のメモ\n\nこれは **太字** と *斜体*、~~取り消し線~~ です。\n- [ ] 表示を確認する\n- [x] 配色を選ぶ\n\n> 引用文と補足メモ\n\n[ドキュメント](https://example.com)\n\nインラインコードは `total = 120` です。\n\n| 項目 | 状態 | 金額 |\n| :--- | :---: | ---: |\n| りんご | 完了 | 120 |\n\n```csharp\nvar total = price * count;\nConsole.WriteLine(total);\n```\n---\n\n\\*通常の文字*\n";
        editor.Text = sample;
        editor.Document.UndoStack.ClearAll();
        Dispatcher.UIThread.RunJobs();
        AvaloniaEdit.Rendering.VisualLineElement ElementAt(string token)
        {
            var offset = editor.Text.IndexOf(token, StringComparison.Ordinal);
            var line = editor.Document.GetLineByOffset(offset);
            var visual = editor.TextArea.TextView.GetVisualLine(line.LineNumber)!;
            var column = visual.GetVisualColumn(offset - visual.FirstDocumentLine.Offset);
            return visual.Elements.First(e => e.VisualColumn <= column && column < e.VisualColumn + e.VisualLength);
        }
        Color ForegroundAt(string token) => ((ISolidColorBrush)ElementAt(token).TextRunProperties.ForegroundBrush!).Color;
        var bodyFont = Typography.BodyFont;
        var codeFont = Typography.EditorFont;
        check(editor.FontFamily.Equals(bodyFont) && ElementAt("これは").TextRunProperties.Typeface.FontFamily.Equals(bodyFont),
            "挿入モードの本文は同梱MigMix 1Mを使用");
        check(ElementAt("`total").TextRunProperties.Typeface.FontFamily.Equals(codeFont)
            && ElementAt("var total").TextRunProperties.Typeface.FontFamily.Equals(codeFont),
            "挿入モードのインライン・ブロックコードも同梱MigMix 1Mを使用");
        void HasColor(string token, string color, string name) => check(ForegroundAt(token) == Color.Parse(color), name + " actual=" + ForegroundAt(token));
        HasColor("# Markdown", "#005CC5", "Light Plusの見出し記号を青で表示");
        HasColor("今日のメモ", "#005CC5", "見出し本文も青で表示");
        HasColor("**太字**", "#7A3E9D", "太字の記号と本文を紫で表示");
        HasColor("斜体", "#7A3E9D", "斜体を紫で表示");
        HasColor("取り消し線", "#7A3E9D", "取り消し線を紫で表示");
        HasColor("- [ ]", "#16754A", "リスト記号を緑で表示");
        HasColor("[x]", "#16754A", "タスク記号を緑で表示");
        HasColor("引用文", "#667085", "引用を青灰色で表示");
        HasColor("ドキュメント", "#007ACC", "リンクラベルを青で表示");
        HasColor("https://example.com", "#5B6573", "リンク先を灰色で区別");
        HasColor("`total", "#A43B00", "インラインコードを茶色で表示");
        HasColor("| 項目", "#16754A", "表の区切りを緑で表示");
        HasColor(":---:", "#16754A", "表の配置記号を緑で表示");
        HasColor("var total", "#A43B00", "コード内容は言語にかかわらず単色で表示");
        HasColor("Console", "#A43B00", "複数行コードの色を保持");
        HasColor("通常の文字", "#242424", "エスケープされた強調記号は本文色を維持");
        check(editor.Text == sample && !editor.Document.UndoStack.CanUndo, "色分けは原文とUndo履歴を変更しない");
        if (renderPath is not null)
        {
            editor.CaretOffset = sample.IndexOf("- [ ]", StringComparison.Ordinal);
            Dispatcher.UIThread.RunJobs();
            using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height));
            bitmap.Render(window);
            bitmap.Save(renderPath);
        }
        editor.Text = "```\n**code** https://example.test\n```\n\nplain";
        Dispatcher.UIThread.RunJobs();
        HasColor("**code**", "#A43B00", "コード内の強調記号を強調として色分けしない");
        HasColor("https://example.test", "#A43B00", "コード内のURLにもコード色を適用");
        HasColor("plain", "#242424", "コードフェンス後は本文色に戻る");
        editor.Document.Remove(editor.Text.IndexOf("```\n\nplain", StringComparison.Ordinal), 3);
        Dispatcher.UIThread.RunJobs();
        HasColor("plain", "#A43B00", "閉じフェンスの削除で後続行を再色分け");
        editor.Document.UndoStack.Undo();
        Dispatcher.UIThread.RunJobs();
        HasColor("plain", "#242424", "Undoでフェンス後の色も復元");
    }
}
