using Avalonia.Controls;
using AvaloniaEdit;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private readonly TextBlock _conversionStatusDisplay = new() { IsVisible = false };
    private int _conversionStatusGeneration;

    private void ShowConversionStatus(string target, bool success)
    {
        _conversionStatusDisplay.Text = target + (success ? "に変換しました" : "に変換できませんでした");
        _saveStatus.IsVisible = false;
        _conversionStatusDisplay.IsVisible = true;
        var generation = ++_conversionStatusGeneration;
        Avalonia.Threading.DispatcherTimer.RunOnce(() =>
        {
            if (generation != _conversionStatusGeneration) return;
            _conversionStatusDisplay.IsVisible = false;
            _saveStatus.IsVisible = true;
        }, TimeSpan.FromSeconds(5));
    }

    private void ConfigureEditorContextMenu(TextEditor editor)
    {
        MenuItem Conversion(string label, string target)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) =>
            {
                if (_mode != EditorMode.Insert || editor.SelectionLength == 0 || editor.IsReadOnly)
                {
                    ShowConversionStatus(target, false);
                    return;
                }
                try
                {
                    var start = editor.SelectionStart;
                    var length = editor.SelectionLength;
                    var result = SelectionConversion.ConvertTo(editor.SelectedText, target);
                    // Block syntax needs its own lines even for a partial-line selection.
                    if (target is "コードブロック" or "d2フェンス" or "Markdownの表")
                    {
                        if (start > 0 && editor.Document.GetCharAt(start - 1) != '\n') result = "\n" + result;
                        if (start + length < editor.Document.TextLength && editor.Document.GetCharAt(start + length) is not ('\r' or '\n') && !result.EndsWith('\n')) result += "\n";
                    }
                    using (editor.Document.RunUpdate()) editor.Document.Replace(start, length, result);
                    editor.Select(start, result.Length);
                    editor.TextArea.Focus();
                    ShowConversionStatus(target, true);
                }
                catch (FormatException) { ShowConversionStatus(target, false); }
            };
            return item;
        }
        var items = new Control[]
        {
            new MenuItem
            {
                Header = "表",
                ItemsSource = new[]
                {
                    Conversion("表へ変換", "Markdownの表"),
                    Conversion("csvへ変換", "CSV"),
                    Conversion("tsvへ変換", "TSV")
                }
            },
            new MenuItem
            {
                Header = "リスト",
                ItemsSource = new[]
                {
                    Conversion("チェックボックスへ変換", "Markdownチェックボックス"),
                    Conversion("箇条書きへ変換", "Markdownリスト(箇条書き)"),
                    Conversion("番号リストへ変換", "Markdownリスト(番号付き)")
                }
            },
            new MenuItem
            {
                Header = "コード",
                ItemsSource = new[]
                {
                    Conversion("コードブロックへ変換", "コードブロック"),
                    Conversion("d2フェンスへ変換", "d2フェンス")
                }
            },
            new Separator(),
            Conversion("標準テキストに変換", "標準テキスト")
        };
        var menu = new ContextMenu { ItemsSource = items };
        menu.Opening += (_, e) =>
        {
            e.Cancel = _mode != EditorMode.Insert || editor.SelectionLength == 0;
            foreach (var item in items.OfType<MenuItem>()) item.IsEnabled = !editor.IsReadOnly;
        };
        editor.ContextMenu = menu;
    }
}
