using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private const double DefaultNotesWidth = 160, DefaultPagesWidth = 320;
    // Include the action buttons, spacing, margins and page sort selector.
    private const double MinimumNotesWidth = 144, MinimumPagesWidth = 320, MinimumEditorWidth = 510;
    private const double CollapsedPaneWidth = 32;
    private const double MinimumWorkspaceWidth = 64 + 8 + MinimumNotesWidth + MinimumPagesWidth + MinimumEditorWidth;
    // Timeline header including its bottom border, eight periods, and trash.
    private const double MinimumWorkspaceHeight = 39 + 9 * 54;
    private const double MinimumSearchResultsWidth = 250;
    private bool _notesCollapsed, _pagesCollapsed;
    private double _expandedNotesWidth = DefaultNotesWidth, _expandedPagesWidth = DefaultPagesWidth;
    private readonly Dictionary<bool, Action<bool>> _updatePanePresentation = new();
    private Grid? _workspaceGrid;
    private Action? _updatePaneLimits;

    private bool IsWorkspacePaneCollapsed(int pane) =>
        pane == 1 && _notesCollapsed || pane == 2 && _pagesCollapsed;

    private void ToggleWorkspacePane(bool notes)
    {
        if (IsDetachedEditor || _isSlideshow || _workspaceGrid is not { } grid || OwnedWindows.Any(w => w.IsVisible)) return;
        var collapsed = notes ? _notesCollapsed : _pagesCollapsed;
        var column = grid.ColumnDefinitions[notes ? 1 : 3];
        if (!collapsed)
        {
            if (notes) _expandedNotesWidth = column.Width.Value;
            else _expandedPagesWidth = column.Width.Value;
        }
        var hadFocus = (notes ? _notes : _pages).IsKeyboardFocusWithin ||
            grid.Children.First(c => Grid.GetColumn(c) == (notes ? 1 : 3)).IsKeyboardFocusWithin;
        if (notes) _notesCollapsed = !collapsed;
        else _pagesCollapsed = !collapsed;
        column.MaxWidth = double.PositiveInfinity;
        column.MinWidth = collapsed ? (notes ? MinimumNotesWidth : MinimumPagesWidth) : CollapsedPaneWidth;
        column.Width = new GridLength(collapsed ? (notes ? _expandedNotesWidth : _expandedPagesWidth) : CollapsedPaneWidth);
        _updatePanePresentation[notes](!collapsed);
        _updatePaneLimits?.Invoke();
        if (!collapsed && hadFocus)
        {
            if (_activePageId is not null) FocusEditingSurface();
            else FocusPane(0);
        }
    }

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
        _workspaceGrid = grid;
        // The two list columns have pixel widths. Their combined width must
        // leave space for the editor, including after resizing the window.
        void UpdatePaneLimits()
        {
            if (_isSlideshow) return;
            var notesMinimum = _notesCollapsed ? CollapsedPaneWidth : MinimumNotesWidth;
            var pagesMinimum = _pagesCollapsed ? CollapsedPaneWidth : MinimumPagesWidth;
            var splitterWidth = (_notesCollapsed ? 0 : 4) + (_pagesCollapsed ? 0 : 4);
            var workspaceMinimum = 64 + splitterWidth + notesMinimum + pagesMinimum + MinimumEditorWidth;
            MinWidth = workspaceMinimum + (_searchResultsPane.IsVisible ? MinimumSearchResultsWidth : 0);
            if (Width < MinWidth) Width = MinWidth;
            if (grid.Bounds.Width <= 0) return;
            var layoutWidth = Math.Max(grid.Bounds.Width, MinWidth);
            var resultsWidth = _searchResultsPane.IsVisible
                ? Math.Min(320, layoutWidth - workspaceMinimum) : 0;
            grid.ColumnDefinitions[6].Width = new GridLength(resultsWidth);
            var available = layoutWidth - 64 - splitterWidth - MinimumEditorWidth - resultsWidth;
            if (available <= 0) return;
            var notes = grid.ColumnDefinitions[1];
            var pages = grid.ColumnDefinitions[3];
            var notesWidth = _notesCollapsed ? CollapsedPaneWidth : Math.Clamp(notes.Width.Value, notesMinimum, available - pagesMinimum);
            var pagesWidth = _pagesCollapsed ? CollapsedPaneWidth : Math.Clamp(pages.Width.Value, pagesMinimum, available - notesWidth);
            notes.Width = new GridLength(notesWidth);
            pages.Width = new GridLength(pagesWidth);
            notes.MaxWidth = _notesCollapsed ? CollapsedPaneWidth : available - pagesWidth;
            pages.MaxWidth = _pagesCollapsed ? CollapsedPaneWidth : available - notesWidth;
            grid.ColumnDefinitions[2].Width = new GridLength(_notesCollapsed ? 0 : 4);
            grid.ColumnDefinitions[4].Width = new GridLength(_pagesCollapsed ? 0 : 4);
            foreach (var splitter in grid.Children.OfType<GridSplitter>())
                splitter.IsVisible = Grid.GetColumn(splitter) == 2 ? !_notesCollapsed : !_pagesCollapsed;
        }
        _updatePaneLimits = UpdatePaneLimits;
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
                var width = column == 2 ? Math.Max(DefaultNotesWidth, MinimumNotesWidth) : Math.Max(DefaultPagesWidth, MinimumPagesWidth);
                var other = grid.ColumnDefinitions[column == 2 ? 3 : 1];
                var splitters = (_notesCollapsed ? 0 : 4) + (_pagesCollapsed ? 0 : 4);
                var available = grid.Bounds.Width - 64 - splitters - MinimumEditorWidth - grid.ColumnDefinitions[6].Width.Value;
                other.Width = new GridLength(Math.Max(other.MinWidth, Math.Min(other.Width.Value, available - width)));
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
