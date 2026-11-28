using System.Reflection;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ViMarkdown;

internal static class HelpDialogs
{
    private static TextBlock Label(string text, double size = 14) => new()
    {
        Text = text, FontSize = size, Foreground = WorkspaceColors.Text,
        TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center
    };

    private static Window Create(string title, Window owner, double width, double height)
    {
        var area = owner.Screens.ScreenFromWindow(owner)?.WorkingArea;
        var scale = owner.RenderScaling;
        var dialog = new Window
        {
            Title = title, Width = Math.Min(width, area?.Width / scale ?? width),
            Height = Math.Min(height, area?.Height / scale ?? height),
            CanResize = true, ShowInTaskbar = false, SystemDecorations = SystemDecorations.None,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = WorkspaceColors.Editor, Foreground = WorkspaceColors.Text
        };
        dialog.KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; dialog.Close(); } };
        return dialog;
    }

    private static DockPanel Frame(Window dialog, string? title, bool compact = false)
    {
        var body = new DockPanel { Margin = compact ? new Thickness(24, 12) : new Thickness(24) };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, compact ? 8 : 20), LastChildFill = true };
        var close = new Button { Content = "×", FontSize = 22, Padding = new Thickness(8, 0), Background = Brushes.Transparent };
        AutomationProperties.SetName(close, "閉じる");
        close.Click += (_, _) => dialog.Close();
        DockPanel.SetDock(close, Dock.Right); header.Children.Add(close);
        if (title is not null)
        {
            var heading = Label(title, 24); heading.FontWeight = FontWeight.SemiBold;
            header.Children.Add(heading);
        }
        else
        {
            header.LastChildFill = false;
            header.Margin = new Thickness(0, 0, 0, 4);
        }
        DockPanel.SetDock(header, Dock.Top); body.Children.Add(header);
        dialog.Content = new Border { BorderBrush = WorkspaceColors.Divider, BorderThickness = new Thickness(1), Child = body };
        return body;
    }

    public static async Task ShowShortcuts(Window owner)
    {
        var dialog = Create("ショートカット一覧", owner, 860, 700);
        var body = Frame(dialog, "ショートカット一覧", compact: true);
        var os = new TabControl { Name = "ShortcutOs", SelectedIndex = OperatingSystem.IsMacOS() ? 1 : 0,
            Items = { new TabItem { Header = "Windows" }, new TabItem { Header = "macOS" } } };
        AutomationProperties.SetName(os, "ショートカットのOS");
        DockPanel.SetDock(os, Dock.Top); body.Children.Add(os);
        var tabs = new TabControl { Name = "ShortcutCategory" };
        var basic = new StackPanel { Spacing = 0 };
        var vim = new StackPanel { Spacing = 0 };
        tabs.Items.Add(new TabItem { Header = "基本操作", Content = Scroll(basic) });
        tabs.Items.Add(new TabItem { Header = "Vim操作", Content = Scroll(vim) });
        body.Children.Add(tabs);
        void PopulateBasic()
        {
            basic.Children.Clear();
            var mod = os.SelectedIndex == 1 ? "Ctrl / ⌘" : "Ctrl";
            foreach (var (action, key) in new (string, string)[]
            {
                ("ページ作成", mod + "+N"), ("ノート作成", mod + "+Shift+N"),
                ("ページ名変更", "F2"), ("ノート名変更", mod + "+F2"),
                ("ページ内検索", mod + "+F"), ("ノート内検索", mod + "+Shift+F"),
                ("前／次のペインへ移動", mod + "+Shift+← / →"),
                ("ノート一覧を閉じる／開く", mod + "+1"),
                ("ページ一覧を閉じる／開く", mod + "+2"),
                ("別ウィンドウを閉じる", mod + "+W"),
                ("一覧の項目を選択", "↑ / ↓"), ("ページの選択範囲を拡張", "Shift+↑ / ↓"),
                ("一覧のページ／ノートを削除", "Delete"),
                ("挿入中に画像・テキストを貼り付け", mod + "+V"),
                ("検索を実行／検索結果を開く", "Enter"), ("検索結果を閉じて本文へ戻る", "Esc"),
                ("検索欄から検索範囲へ移動", "Shift+Tab"), ("検索範囲から検索欄へ移動", "Tab"),
                ("検索範囲の候補を展開", os.SelectedIndex == 1 ? "⌘+↓" : "Alt+↓"),
                ("スライドショー開始／終了", "F5"), ("スライドショーを終了", "Esc"),
                ("スライドショー中にスクロール", "↑ / ↓")
            }) AddRow(basic, action, key);

        }
        os.SelectionChanged += (_, _) => PopulateBasic();
        PopulateBasic();
        // Reuse the bundled reference so the complete Vim table stays aligned with the README.
        using var stream = typeof(App).Assembly.GetManifestResourceStream("ViMarkdown.InitialPages.README.md")!;
        using var reader = new StreamReader(stream);
        var reference = reader.ReadToEnd();
        var start = reference.IndexOf("### カーソル移動", StringComparison.Ordinal);
        var end = reference.IndexOf("### マウス・ワークスペース", start, StringComparison.Ordinal);
        var lines = reference[start..end].Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.StartsWith("### "))
            {
                var heading = Label(line[4..], 16); heading.FontWeight = FontWeight.SemiBold;
                heading.Margin = new Thickness(0, 10, 0, 4); vim.Children.Add(heading);
            }
            else if (line.StartsWith('|'))
            {
                // Markdown headers and delimiters belong to the table syntax,
                // not to the explanatory text or shortcut rows.
                if (IsTableSeparator(line) ||
                    (index + 1 < lines.Length && IsTableSeparator(lines[index + 1]))) continue;
                var cells = line.Trim('|').Split('|');
                if (cells.Length == 2) AddRow(vim, cells[1].Trim(), cells[0].Trim().Replace("`` ` ``", "バッククォート").Replace("`", ""));
            }
            else if (!string.IsNullOrWhiteSpace(line))
                vim.Children.Add(new TextBlock { Text = line.Replace("`", ""), TextWrapping = TextWrapping.Wrap,
                    Foreground = WorkspaceColors.Muted, Margin = new Thickness(0, 4, 0, 4) });
        }
        await dialog.ShowDialog(owner);
    }

    private static bool IsTableSeparator(string line)
    {
        var cells = line.Trim().Trim('|').Split('|');
        return cells.Length >= 2 && cells.All(cell =>
        {
            var delimiter = cell.Trim().Trim(':');
            return delimiter.Length >= 3 && delimiter.All(c => c == '-');
        });
    }

    private static ScrollViewer Scroll(Control content) => new()
    {
        Content = content, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
    };

    private static void AddRow(StackPanel parent, string action, string keys)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(0, 3) };
        row.Children.Add(Label(action));
        var key = new Border { Background = WorkspaceColors.Pages, BorderBrush = WorkspaceColors.Divider,
            BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(8, 2),
            Margin = new Thickness(12, 0, 0, 0), HorizontalAlignment = HorizontalAlignment.Right,
            Child = Label(keys) };
        Grid.SetColumn(key, 1); row.Children.Add(key);
        parent.Children.Add(new Border { BorderBrush = WorkspaceColors.Divider,
            BorderThickness = new Thickness(0, 0, 0, 1), Child = row });
    }

    public static async Task ShowAbout(Window owner)
    {
        var dialog = Create("このアプリについて", owner, 420, 300);
        dialog.MaxHeight = owner.Screens.ScreenFromWindow(owner)?.WorkingArea.Height / owner.RenderScaling ?? 600;
        dialog.SizeToContent = SizeToContent.Height;
        dialog.Height = double.NaN;
        var content = new StackPanel { Spacing = 8, HorizontalAlignment = HorizontalAlignment.Stretch };
        using var iconStream = AssetLoader.Open(new Uri("avares://ViMarkdown/Assets/ViMarkdown.png"));
        using var icon = new Bitmap(iconStream);
        var identity = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        identity.Children.Add(new Image { Source = icon, Width = 56, Height = 56 });
        var details = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        details.Children.Add(Label("ViMarkdown", 24));
        var version = typeof(App).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0] ?? "不明";
        details.Children.Add(Label("バージョン: " + version));
        identity.Children.Add(details);
        var heading = new DockPanel { LastChildFill = true };
        var close = new Button
        {
            Content = "×", FontSize = 22, Padding = new Thickness(4, 0),
            Background = Brushes.Transparent, VerticalAlignment = VerticalAlignment.Top
        };
        AutomationProperties.SetName(close, "閉じる");
        close.Click += (_, _) => dialog.Close();
        DockPanel.SetDock(close, Dock.Right);
        heading.Children.Add(close);
        heading.Children.Add(identity);
        content.Children.Add(heading);
        content.Children.Add(Label("Vim操作に対応したMarkdownエディター"));
        content.Children.Add(Label("作者: nokura-dev　／　ライセンス: MIT"));
        var publication = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        publication.Children.Add(Label("掲載先:"));
        var link = new HyperlinkButton
        {
            Content = "https://github.com/nokura-dev/ViMarkdown/",
            NavigateUri = new Uri("https://github.com/nokura-dev/ViMarkdown/"),
            FontSize = 14, Padding = new Thickness(0), MinHeight = 0,
            VerticalAlignment = VerticalAlignment.Center
        };
        publication.Children.Add(link);
        content.Children.Add(publication);
        dialog.Content = new Border
        {
            BorderBrush = WorkspaceColors.Divider, BorderThickness = new Thickness(1),
            Padding = new Thickness(12), Child = Scroll(content)
        };
        await dialog.ShowDialog(owner);
    }
}
