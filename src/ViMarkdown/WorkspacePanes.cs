using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using Avalonia.Styling;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private Control BuildWorkspacePane(ListBox list, bool notes)
    {
        var background = notes ? WorkspaceColors.Notes : WorkspaceColors.Pages;
        var panel = new DockPanel { Background = background, ClipToBounds = true };
        list.Background = background;
        list.BorderThickness = new Thickness(0);
        list.Foreground = WorkspaceColors.Text;
        // Internal padding can accumulate during bring-into-view; keep it zero.
        list.Padding = new Thickness(0);
        list.Focusable = true;
        ScrollViewer.SetHorizontalScrollBarVisibility(list, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        // Selection may rebuild every row. Restore focus to the stable list
        // after its pointer handlers, including clicks on the current selection.
        list.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (e.InitialPressMouseButton == MouseButton.Left) list.Focus();
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        list.FontSize = 14;
        list.Styles.Add(new Style(s => s.OfType<TextBlock>())
        {
            Setters =
            {
                new Setter(TextBlock.FontSizeProperty, 14d),
                new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center)
            }
        });
        list.Styles.Add(new Avalonia.Styling.Style(s => s.OfType<ListBoxItem>())
        {
            Setters =
            {
                new Setter(ListBoxItem.MinHeightProperty, 30d),
                new Setter(ListBoxItem.VerticalContentAlignmentProperty, VerticalAlignment.Center),
                new Setter(ListBoxItem.PaddingProperty, notes ? new Thickness(12, 4) : new Thickness(12, 6)),
                new Setter(ListBoxItem.MarginProperty, new Thickness(0)),
                new Setter(ListBoxItem.BorderBrushProperty, WorkspaceColors.Divider),
                new Setter(ListBoxItem.BorderThicknessProperty, new Thickness(0, 0, 0, 1)),
                new Setter(ListBoxItem.CornerRadiusProperty, new CornerRadius(0))
            }
        });
        foreach (var state in new[] { ":pointerover", ":selected", ":selected:pointerover" })
        {
            var style = new Avalonia.Styling.Style(s => state == ":pointerover"
                ? s.OfType<ListBoxItem>().Class(":pointerover").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>()
                : state == ":selected"
                    ? s.OfType<ListBoxItem>().Class(":selected").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>()
                    : s.OfType<ListBoxItem>().Class(":selected").Class(":pointerover").Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>());
            style.Setters.Add(new Setter(Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty,
                state == ":pointerover" ? WorkspaceColors.Hover : WorkspaceColors.Selected));
            style.Setters.Add(new Setter(Avalonia.Controls.Presenters.ContentPresenter.ForegroundProperty, WorkspaceColors.Text));
            list.Styles.Add(style);
        }
        if (notes)
        {
            var dropHover = new Style(s => s.OfType<ListBoxItem>().Class("page-drop-hover")
                .Template().OfType<Avalonia.Controls.Presenters.ContentPresenter>());
            dropHover.Setters.Add(new Setter(Avalonia.Controls.Presenters.ContentPresenter.BackgroundProperty, WorkspaceColors.Hover));
            list.Styles.Add(dropHover);
        }
        if (!notes)
        {
            // Include Latin ascenders/descenders and Japanese fallback glyphs
            // so titles keep the same line box regardless of their script.
            using var titleMetrics = new TextLayout("Agjpqy 日本語", new Typeface(Typography.JapaneseUiFont), 14, WorkspaceColors.Text);
            using var dateMetrics = new TextLayout("0123456789/ : 更新日時不明", Typography.BodyTypeface, 10, WorkspaceColors.Muted);
            var titleLineHeight = titleMetrics.Height;
            var dateLineHeight = dateMetrics.Height;
            static (double Top, double Bottom) InkEdges(TextLayout text)
            {
                var line = text.TextLines[0];
                var top = double.PositiveInfinity;
                var bottom = double.NegativeInfinity;
                foreach (var run in line.TextRuns.OfType<ShapedTextRun>())
                {
                    var ink = run.GlyphRun.BuildGeometry().Bounds;
                    if (ink.Width <= 0 || ink.Height <= 0) continue;
                    var baseline = line.Baseline - run.Baseline;
                    top = Math.Min(top, ink.Top + baseline);
                    bottom = Math.Max(bottom, ink.Bottom + baseline);
                }
                return double.IsFinite(top) ? (top, bottom) : (0, text.Height);
            }
            const double lineSpacing = 1;
            var titleInk = InkEdges(titleMetrics);
            var dateInk = InkEdges(dateMetrics);
            // The visible gap includes the title's descent space and the
            // date's ascent space. Halve that gap, not just StackPanel.Spacing.
            // Overlap only the empty line-box space, preserving full line
            // heights so descenders and Japanese glyphs are not clipped.
            var gapReduction = Math.Max(0, titleLineHeight - titleInk.Bottom + lineSpacing + dateInk.Top) / 2;
            list.ItemTemplate = new FuncDataTemplate<WorkspaceItem>((page, _) =>
            {
                if (page is null) return null;
                var row = new StackPanel { Spacing = lineSpacing };
                // Preserve the bundled font's full ascent and descent; row
                // padding controls the extra space around these line boxes.
                row.Children.Add(new TextBlock
                {
                    Text = page.Name,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    FontFamily = Typography.JapaneseUiFont,
                    FontSize = 14,
                    LineHeight = titleLineHeight
                });
                row.Children.Add(new TextBlock
                {
                    Name = "PageUpdatedAt",
                    Tag = page.Id,
                    Text = page.UpdatedAt == DateTimeOffset.MinValue ? "更新日時不明" : page.UpdatedAt.ToLocalTime().ToString("yyyy/MM/dd HH:mm"),
                    FontSize = 10,
                    LineHeight = dateLineHeight,
                    Margin = new Thickness(0, -gapReduction, 0, 0),
                    Foreground = WorkspaceColors.Muted
                });
                return row;
            });
        }
        var actionRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(10, 5), MinHeight = 28 };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
        actionRow.Children.Add(actions);
        if (!notes)
        {
            var sort = new ComboBox
            {
                Name = "PageSort",
                IsTabStop = false,
                ItemsSource = new[] { "名称順", "更新日(新しい順)", "更新日(古い順)" },
                SelectedIndex = (int)_pageSortOrder,
                HorizontalAlignment = HorizontalAlignment.Right,
                Width = 138,
                Height = 28,
                MinHeight = 28,
                Margin = new Thickness(1),
                Background = WorkspaceColors.Editor,
                BorderBrush = WorkspaceColors.Divider,
                CornerRadius = new CornerRadius(3),
                Foreground = WorkspaceColors.Text,
                Padding = new Thickness(6, 0),
                FontSize = 11,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            Avalonia.Automation.AutomationProperties.SetName(sort, "ページの表示順");
            ToolTip.SetTip(sort, "ページの表示順");
            sort.SelectionChanged += (_, _) =>
            {
                if (sort.SelectedIndex < 0 || sort.SelectedIndex == (int)_pageSortOrder) return;
                if (!SaveCurrent()) { sort.SelectedIndex = (int)_pageSortOrder; return; }
                _pageSortOrder = (PageSortOrder)sort.SelectedIndex;
                _store.SetSetting("page-sort", _pageSortOrder.ToString());
                ReloadLists();
            };
            Grid.SetColumn(sort, 1); actionRow.Children.Add(sort);
        }
        var menuActions = BuildWorkspaceActions(notes, actions);
        AddWorkspaceContextMenu(list, notes, menuActions);
        var header = new Border
        {
            Background = WorkspaceColors.Chrome,
            BorderBrush = WorkspaceColors.Divider,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = actionRow
        };
        DockPanel.SetDock(header, Dock.Top); panel.Children.Add(header);
        panel.Children.Add(WithPaneFocus(list, notes ? 1 : 2)); return panel;
    }
}
