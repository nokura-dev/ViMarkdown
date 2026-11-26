using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private const double DefaultNotesWidth = 160, DefaultPagesWidth = 310;
    // Include the action buttons, spacing, margins and page sort selector.
    private const double MinimumNotesWidth = 112, MinimumPagesWidth = 290, MinimumEditorWidth = 510;
    private const double MinimumWorkspaceWidth = 64 + 8 + MinimumNotesWidth + MinimumPagesWidth + MinimumEditorWidth;
    // Timeline header, seven period buttons, and the trash button.
    private const double MinimumWorkspaceHeight = 38 + 9 * 54;
    private const double MinimumSearchResultsWidth = 250;

    private Grid BuildWorkspaceLayout(Control editor)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions
            {
                new(64, GridUnitType.Pixel),
                new(DefaultNotesWidth, GridUnitType.Pixel) { MinWidth = MinimumNotesWidth },
                new(4, GridUnitType.Pixel),
                new(DefaultPagesWidth, GridUnitType.Pixel) { MinWidth = MinimumPagesWidth },
                new(4, GridUnitType.Pixel),
                new(1, GridUnitType.Star) { MinWidth = MinimumEditorWidth },
                new(0, GridUnitType.Pixel)
            }
        };
        // The two list columns have pixel widths. Their combined width must
        // leave space for the editor, including after resizing the window.
        void UpdatePaneLimits()
        {
            MinWidth = MinimumWorkspaceWidth + (_searchResultsPane.IsVisible ? MinimumSearchResultsWidth : 0);
            if (Width < MinWidth) Width = MinWidth;
            if (grid.Bounds.Width <= 0) return;
            var layoutWidth = Math.Max(grid.Bounds.Width, MinWidth);
            var resultsWidth = _searchResultsPane.IsVisible
                ? Math.Min(320, layoutWidth - MinimumWorkspaceWidth) : 0;
            grid.ColumnDefinitions[6].Width = new GridLength(resultsWidth);
            var available = layoutWidth - 64 - 8 - MinimumEditorWidth - resultsWidth;
            if (available <= 0) return;
            var notes = grid.ColumnDefinitions[1];
            var pages = grid.ColumnDefinitions[3];
            var notesWidth = Math.Clamp(notes.Width.Value, MinimumNotesWidth, available - MinimumPagesWidth);
            var pagesWidth = Math.Clamp(pages.Width.Value, MinimumPagesWidth, available - notesWidth);
            notes.Width = new GridLength(notesWidth);
            pages.Width = new GridLength(pagesWidth);
            notes.MaxWidth = available - pagesWidth;
            pages.MaxWidth = available - notesWidth;
        }
        grid.SizeChanged += (_, _) => UpdatePaneLimits();
        grid.LayoutUpdated += (_, _) => UpdatePaneLimits();
        var notesPane = BuildWorkspacePane(_notes, true); Grid.SetColumn(notesPane, 1); grid.Children.Add(notesPane);
        grid.Children.Add(BuildTimeline());
        var pages = BuildWorkspacePane(_pages, false); Grid.SetColumn(pages, 3); grid.Children.Add(pages);
        foreach (var column in new[] { 2, 4 })
        {
            var splitter = new GridSplitter
            {
                Width = 4,
                Background = WorkspaceColors.Pages,
                ResizeDirection = GridResizeDirection.Columns,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                BorderBrush = WorkspaceColors.Divider,
                BorderThickness = new Thickness(1, 0, 0, 0)
            };
            // Handle the second press before Thumb starts another drag, so the
            // reset width also becomes the starting width of the next drag.
            splitter.AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (e.ClickCount != 2 || !e.GetCurrentPoint(splitter).Properties.IsLeftButtonPressed) return;
                var width = column == 2 ? DefaultNotesWidth : DefaultPagesWidth;
                var other = grid.ColumnDefinitions[column == 2 ? 3 : 1];
                var available = grid.Bounds.Width - 64 - 8 - MinimumEditorWidth - grid.ColumnDefinitions[6].Width.Value;
                other.Width = new GridLength(Math.Min(other.Width.Value, available - width));
                grid.ColumnDefinitions[column - 1].Width = new GridLength(width);
                UpdatePaneLimits();
                e.Handled = true;
            }, RoutingStrategies.Tunnel);
            Grid.SetColumn(splitter, column); grid.Children.Add(splitter);
        }
        Grid.SetColumn(editor, 5); grid.Children.Add(editor);
        BuildSearchResultsPane();
        Grid.SetColumn(_searchResultsPane, 6); grid.Children.Add(_searchResultsPane);
        return grid;
    }
}
