using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private readonly ComboBox _searchScope = new()
    {
        Name = "SearchScope",
        IsTabStop = false,
        ItemsSource = new[] { "全てのノート", "選択中のノート", "表示中のページ" },
        SelectedIndex = 2,
        Width = 152,
        MinHeight = 28,
        Height = 28,
        FontSize = 12,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        Padding = new Thickness(8, 0),
        VerticalContentAlignment = VerticalAlignment.Center
    };
    private readonly TextBox _searchKeyword = new()
    {
        Name = "SearchKeyword",
        IsTabStop = false,
        Watermark = "キーワードを入力",
        MinWidth = 100,
        Width = 130,
        MinHeight = 28,
        Height = 28,
        FontSize = 12,
        BorderThickness = new Thickness(0),
        Background = Brushes.Transparent,
        Padding = new Thickness(8, 0),
        VerticalContentAlignment = VerticalAlignment.Center
    };
    private readonly Border _searchResultsPane = new() { Name = "SearchResultsPane", IsVisible = false, ClipToBounds = true };
    private readonly ListBox _searchResults = new() { Name = "SearchResults", Background = Brushes.Transparent, BorderThickness = new Thickness(0) };
    private readonly TextBlock _searchSummary = new() { FontSize = 12, Foreground = WorkspaceColors.Muted };
    private IReadOnlyList<(int Start, int End)> _workspaceSearchRanges = [];
    private string _highlightKeyword = "";
    private string _lastPageSearch = "";
    private long? _lastSearchPage;

    private Control BuildSearchBox()
    {
        AutomationProperties.SetName(_searchScope, "検索範囲");
        AutomationProperties.SetName(_searchKeyword, "検索キーワード");
        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(_searchScope);
        row.Children.Add(new Border { Width = 1, Height = 17, Background = WorkspaceColors.Divider });
        row.Children.Add(_searchKeyword);
        var button = new Button
        {
            Name = "SearchButton",
            Focusable = false, IsTabStop = false,
            Width = 30,
            Height = 28,
            MinHeight = 28,
            Padding = new Thickness(6),
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            Content = new Avalonia.Controls.Shapes.Path
            {
                Data = Geometry.Parse("M11,6 A5,5 0 1 1 1,6 A5,5 0 1 1 11,6 M10,10 L15,15"),
                Stroke = WorkspaceColors.Accent,
                StrokeThickness = 1.6,
                Stretch = Stretch.Uniform
            }
        };
        AutomationProperties.SetName(button, "検索");
        ToolTip.SetTip(button, "検索 (Enter)");
        button.Click += (_, _) => RunWorkspaceSearch();
        // Keep these explicit search-field moves separate from the main Tab
        // order, which deliberately excludes the toolbar.
        _searchKeyword.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key != Key.Tab || e.KeyModifiers != KeyModifiers.Shift) return;
            e.Handled = true;
            _searchScope.Focus();
        }, RoutingStrategies.Tunnel);
        _searchScope.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Down && e.KeyModifiers is KeyModifiers.Meta or KeyModifiers.Alt)
            {
                // Handle both combinations so the native Alt+Down binding does
                // not also expand the list on macOS.
                e.Handled = true;
                var modifier = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Alt;
                if (e.KeyModifiers == modifier) _searchScope.IsDropDownOpen = true;
                return;
            }
            if (e.Key != Key.Tab || e.KeyModifiers != KeyModifiers.None || _searchScope.IsDropDownOpen) return;
            e.Handled = true;
            _searchKeyword.Focus();
        }, RoutingStrategies.Tunnel);
        _searchKeyword.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            RunWorkspaceSearch();
        };
        row.Children.Add(button);
        return new Border
        {
            Name = "WorkspaceSearchBox",
            Child = row,
            Margin = new Thickness(8, 5, 10, 5),
            Background = WorkspaceColors.Editor,
            BorderBrush = WorkspaceColors.Divider,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4)
        };
    }

    private void BuildSearchResultsPane()
    {
        var panel = new DockPanel { Background = WorkspaceColors.Chrome };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(12, 5), MinHeight = 28 };
        header.Children.Add(new TextBlock { Text = "検索結果", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        var close = new Button { Focusable = false, IsTabStop = false, Name = "CloseSearchResults", Content = "×", FontSize = 20, Width = 28, Height = 28, MinHeight = 28, Padding = new Thickness(0), Background = Brushes.Transparent };
        AutomationProperties.SetName(close, "検索結果を閉じる");
        close.Click += (_, _) => { _searchResultsPane.IsVisible = false; FocusEditingSurface(); };
        Grid.SetColumn(close, 1); header.Children.Add(close);
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        // The summary belongs to the content area, so the pane ring encloses
        // both it and the cards, excluding only the title/close toolbar.
        var contentArea = new DockPanel();
        _searchSummary.Margin = new Thickness(12, 8, 12, 0);
        DockPanel.SetDock(_searchSummary, Dock.Top); contentArea.Children.Add(_searchSummary);
        _searchResults.Padding = new Thickness(0);
        _searchResults.Focusable = true;
        _searchResults.ItemTemplate = new FuncDataTemplate<WorkspaceSearchHit>((hit, _) =>
        {
            if (hit is null) return new TextBlock();
            var content = new StackPanel { Spacing = 5, Margin = new Thickness(10) };
            var heading = new TextBlock
            {
                Text = $"{hit.NoteName} → {hit.PageName}　{hit.LineNumber}行目",
                FontSize = 12,
                FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            ToolTip.SetTip(heading, heading.Text);
            content.Children.Add(heading);
            var line = new TextBlock { FontSize = 13, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis };
            AddHighlightedLine(line, hit.LineText, hit.Keyword);
            ToolTip.SetTip(line, hit.LineText);
            content.Children.Add(line);
            return content;
        });
        _searchResults.Styles.Add(new Style(s => s.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(ListBoxItem.BackgroundProperty, WorkspaceColors.Editor),
                new Setter(ListBoxItem.BorderBrushProperty, WorkspaceColors.Divider),
                new Setter(ListBoxItem.BorderThicknessProperty, new Thickness(1)),
                new Setter(ListBoxItem.CornerRadiusProperty, new CornerRadius(4)),
                new Setter(ListBoxItem.MarginProperty, new Thickness(0, 0, 0, 8)),
                new Setter(ListBoxItem.PaddingProperty, new Thickness(0)),
                new Setter(ListBoxItem.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch)
            }
        });
        _searchResults.Styles.Add(new Style(s => s.OfType<ListBoxItem>().Class(":selected"))
        {
            Setters = { new Setter(ListBoxItem.BackgroundProperty, WorkspaceColors.Selected), new Setter(ListBoxItem.BorderBrushProperty, WorkspaceColors.Accent) }
        });
        _searchResults.Styles.Add(new Style(s => s.OfType<ListBoxItem>().Class(":selected")
            .Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>())
        {
            Setters =
            {
                new Setter(Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, WorkspaceColors.Selected),
                new Setter(Avalonia.Controls.Presenters.ContentPresenter.ForegroundProperty, WorkspaceColors.Text)
            }
        });
        ScrollViewer.SetHorizontalScrollBarVisibility(_searchResults, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        _searchResults.AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.ClickCount != 2 || !e.GetCurrentPoint(_searchResults).Properties.IsLeftButtonPressed) return;
            var row = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<ListBoxItem>()
                .FirstOrDefault(c => _searchResults.IndexFromContainer(c) >= 0);
            if (row?.DataContext is WorkspaceSearchHit hit) NavigateToSearchHit(hit);
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        _searchResults.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && _searchResults.SelectedItem is WorkspaceSearchHit hit)
            { NavigateToSearchHit(hit); e.Handled = true; }
        };
        // Keep fixed spacing outside the virtualized list so bringing the
        // first card into view cannot consume the top inset.
        contentArea.Children.Add(new Border
        {
            Name = "SearchResultsCards",
            Padding = new Thickness(8),
            Child = _searchResults
        });
        panel.Children.Add(WithPaneFocus(contentArea, 4));
        _searchResultsPane.Background = WorkspaceColors.Chrome;
        _searchResultsPane.BorderBrush = WorkspaceColors.Divider;
        _searchResultsPane.BorderThickness = new Thickness(1, 0, 0, 0);
        _searchResultsPane.Child = panel;
    }

    private static void AddHighlightedLine(TextBlock block, string text, string keyword)
    {
        var offset = 0;
        foreach (var match in WorkspaceSearch.Matches(text, keyword))
        {
            block.Inlines!.Add(new Run(text[offset..match.Start]));
            block.Inlines.Add(new Run(text[match.Start..match.End]) { Background = WorkspaceColors.SearchMatch });
            offset = match.End;
        }
        block.Inlines!.Add(new Run(text[offset..]));
    }

}
