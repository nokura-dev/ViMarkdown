using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ViMarkdown;

internal static class EditorViewChecks
{
    public static void Run(Action<bool, string> check, string? insertPath, string? workspacePath)
    {
        using var session = new EditorSession();
        var window = session.Window;
        var editor = session.Editor;
        // Keep this scenario independent of the bundled README's current title.
        editor.Text = "# Markdown 表示切り替え\n\n本文 **太字** と `code`\n\n---\n\n末尾";
        Dispatcher.UIThread.RunJobs();
        var original = editor.Document.Text;
        check(window.GetVisualDescendants().OfType<TextBlock>().Where(t => !string.IsNullOrEmpty(t.Text))
                .All(t => t.FontFamily.Equals(Typography.BodyFont)),
            "一覧・ラベル・ツールバーの文字は同梱フォントを使用する");
        check(window.GetVisualDescendants().OfType<TemplatedControl>().Where(c => c is Button or TextBox or ComboBox)
                .All(c => c.FontFamily.Equals(Typography.BodyFont)),
            "ボタン・検索欄・選択欄は両OS共通フォントを使用する");
        var licenseDirectory = Path.Combine(RuntimeAssets.DataDirectory, "Licenses", "MigMix1M");
        check(File.Exists(Path.Combine(licenseDirectory, "ipag00303", "IPA_Font_License_Agreement_v1.0.txt")) &&
            File.Exists(Path.Combine(licenseDirectory, "ViMarkdown-NOTICE.txt")) &&
            File.Exists(Path.Combine(licenseDirectory, "migmix-README.txt")),
            "フォントのライセンス・著作権表示・置き換え案内が出力に含まれる");
        session.Click("INSERT (i)");
        check(editor.IsVisible && editor.GetVisualDescendants().Contains(editor.TextArea.TextView)
            && editor.TextArea.TextView.Bounds.Width > 0 && editor.TextArea.TextView.Bounds.Height > 0,
            "挿入モードのテンプレート内に本文描画領域が配置される");
        check(editor.Document.Text == original && original.StartsWith("# Markdown"), "表示切り替えでMarkdown原文を維持");
        editor.CaretOffset = 0;
        window.KeyTextInput("追記");
        check(editor.Text == original.Insert(0, "追記"), "Undo対象の入力が本文に反映される");
        session.Key(PhysicalKey.Escape);
        session.Key(PhysicalKey.I);
        session.Click("Undo");
        check(editor.Document.Text == original, "モード往復後に入力をUndoできる");
        var renderPath = insertPath ?? workspacePath;
        if (renderPath is null) return;
        if (workspacePath is not null) session.Click("NORMAL (Esc)");
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new Vector(96, 96));
        bitmap.Render(window);
        bitmap.Save(renderPath);
        Console.WriteLine($"Rendered {renderPath}");
    }
}
