using Avalonia;
using Avalonia.Media;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using ViMarkdown;

internal static class MarkdownSyntaxChecks
{
    public static void Run(Action<bool, string> check)
    {
        const string source = "3. third\n4. fourth\n   - nested\n\n> quote\n> second\n\n---\n\n[link](https://example.com) and ~~gone~~\n\n- [ ] todo\n- [x] done\n\nTerm\n: definition\n\n| left | center | right |\n| :--- | :---: | ---: |\n| a | b | c |\n\n```cs\nvar x = 1;\n```\n";
        var units = Projection.Build(source);
        var text = string.Concat(units.Select(u => u.Text));
        check(text.Contains("3. ") && text.Contains("4. ") && text.Contains("• "), "番号の開始値と入れ子のリスト記号");
        check(text.Contains("│  quote") && units.Any(u => u.Rule), "引用表示と水平線");
        check(units.Where(u => u.LinkUrl is not null).All(u => u.LinkUrl == "https://example.com") && units.Any(u => u.LinkUrl is not null), "リンクのURLを保持");
        check(string.Concat(units.Where(u => u.Strike).Select(u => u.Text)) == "gone", "取り消し線の対象範囲");
        check(units.Count(u => u.TaskOffset.HasValue) == 2 && units.Where(u => u.TaskOffset.HasValue).All(u => source.Substring(u.TaskOffset!.Value, 3) is "[ ]" or "[x]"), "タスクのクリック位置は原文のチェック欄");
        check(text.Contains("definition") && units.Any(u => u.Text == "T" && u.Bold), "定義リストの用語と説明");
        var table = units.Single(u => u.Table is not null);
        check(table.Alignments!.SequenceEqual(new[] { TextAlignment.Left, TextAlignment.Center, TextAlignment.Right }), "表の列配置");
        check(units.All(u => u.Start >= 0 && u.End >= u.Start && u.End <= source.Length), "追加記法の原文範囲");
        using var layout = new RichTextLayout(units, 900);
        foreach (var task in units.Where(u => u.TaskOffset.HasValue))
        {
            var rect = layout.CaretBounds(task.Start);
            check(layout.InteractiveAt(new Point(rect.X + 2, rect.Center.Y))?.TaskOffset == task.TaskOffset, "チェックボックスの当たり判定");
        }
        var link = units.First(u => u.LinkUrl is not null);
        var linkRect = layout.CaretBounds(link.Start);
        check(layout.InteractiveAt(new Point(linkRect.X + 2, linkRect.Center.Y))?.LinkUrl == link.LinkUrl, "リンクの当たり判定");
        const string aligned = "| a | b | c |\n| :--- | :---: | ---: |\n| x | y | z |";
        using var alignedLayout = new RichTextLayout(Projection.Build(aligned), 900);
        var left = alignedLayout.CaretBounds(aligned.LastIndexOf('x'));
        var center = alignedLayout.CaretBounds(aligned.LastIndexOf('y'));
        var right = alignedLayout.CaretBounds(aligned.LastIndexOf('z'));
        check(left.X < 20 && center.X > 420 && right.X > 860, $"列配置が実際のセル描画位置に反映 {left.X}, {center.X}, {right.X}");
        check(alignedLayout.HitTest(new Point(right.X + .1, right.Center.Y)) == aligned.LastIndexOf('z'), "右寄せセルのクリックと原文位置");
        check(alignedLayout.TryMoveInTable(aligned.IndexOf('a'), 1, true, out var below) && below == aligned.LastIndexOf('x'),
        "表のj移動は区切り行を飛ばして同じ列へ移動");
        check(alignedLayout.TryMoveInTable(below, -1, true, out var above) && above == aligned.IndexOf('a'),
        "表のk移動は見出しセルへ戻る");
        check(alignedLayout.TryMoveInTable(aligned.IndexOf('a'), 1, false, out var cellEnd) && cellEnd == aligned.IndexOf('a') + 1,
            "表のl移動は現在のセルの文字末尾に止まる");
        check(alignedLayout.TryMoveInTable(cellEnd, 1, false, out var nextCell) && nextCell == aligned.IndexOf('b'),
            "セル末尾の次に次のセルの文字先頭へ進む");
        check(alignedLayout.TryMoveInTable(nextCell, -1, false, out var previousEnd) && previousEnd == cellEnd,
            "表のh移動は前のセルの文字末尾に止まる");
        check(alignedLayout.TryMoveInTable(previousEnd, -1, false, out var previousCell) && previousCell == aligned.IndexOf('a'),
            "セル末尾から前の表示文字へ戻る");
        const string wrappedTable = "| short | long |\n| --- | --- |\n| x | abcdefghijklmnopqrstuvwxyz |";
        using var centered = new RichTextLayout(Projection.Build(wrappedTable), 160);
        var shortCell = centered.CaretBounds(wrappedTable.IndexOf("| x", StringComparison.Ordinal) + 2);
        var firstLong = centered.CaretBounds(wrappedTable.IndexOf("abcdefghijklmnopqrstuvwxyz", StringComparison.Ordinal));
        var lastLong = centered.CaretBounds(wrappedTable.LastIndexOf('z'));
        check(lastLong.Y > firstLong.Y && Math.Abs(shortCell.Center.Y - (firstLong.Center.Y + lastLong.Center.Y) / 2) < 1,
        "折り返す行でも短いセルのカーソルを上下中央へ配置");
        check(centered.HitTest(new Point(shortCell.X + .1, shortCell.Center.Y)) == wrappedTable.IndexOf("| x", StringComparison.Ordinal) + 2,
        "中央配置したセルのクリック位置が一致");
        using var session = new EditorSession();
        var window = session.Window;
        var editor = session.Editor;
        var surface = session.Rich;
        editor.Document.Text = aligned;
        Dispatcher.UIThread.RunJobs();
        using (var tableLayout = new RichTextLayout(Projection.Build(aligned), surface.Bounds.Width - surface.ContentPadding.Left - surface.ContentPadding.Right))
        {
            var cellRect = tableLayout.CaretBounds(aligned.IndexOf('a'));
            var cellPoint = surface.TranslatePoint(new Point(cellRect.X + surface.ContentOrigin.X + .1,
                cellRect.Center.Y + surface.ContentOrigin.Y), window)!.Value;
            window.MouseDown(cellPoint, MouseButton.Left);
            window.MouseUp(cellPoint, MouseButton.Left);
            foreach (var (key, expected) in new[] { (PhysicalKey.J, aligned.LastIndexOf('x')),
                (PhysicalKey.K, aligned.IndexOf('a')), (PhysicalKey.L, aligned.IndexOf('a') + 1),
                (PhysicalKey.L, aligned.IndexOf('b')), (PhysicalKey.H, aligned.IndexOf('a') + 1),
                (PhysicalKey.H, aligned.IndexOf('a')) })
            {
                window.KeyPressQwerty(key, RawInputModifiers.None);
                window.KeyReleaseQwerty(key, RawInputModifiers.None);
                Dispatcher.UIThread.RunJobs();
                check(surface.Caret == expected, $"ノーマルモードの表内キー移動 {key}");
            }
        }
        editor.Document.Text = "- [ ] task";
        editor.Document.UndoStack.ClearAll();
        Dispatcher.UIThread.RunJobs();
        using var taskLayout = new RichTextLayout(Projection.Build(editor.Document.Text), surface.Bounds.Width - surface.ContentPadding.Left - surface.ContentPadding.Right);
        var taskRect = taskLayout.CaretBounds(2);
        var point = surface.TranslatePoint(new Point(taskRect.X + surface.ContentOrigin.X + 2, taskRect.Center.Y + surface.ContentOrigin.Y), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        check(editor.Document.Text == "- [x] task", "チェックボックスのクリックで実際の本文を更新");
        editor.Document.UndoStack.Undo();
        check(editor.Document.Text == "- [ ] task", "チェック状態の変更をUndo");
        editor.Document.UndoStack.Redo();
        check(editor.Document.Text == "- [x] task", "チェック状態の変更をRedo");
        check(units.Any(u => u.Code && u.Text == "v"), "言語指定付きコードは等幅表示");
    }
}
