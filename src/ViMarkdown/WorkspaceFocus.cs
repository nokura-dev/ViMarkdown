using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private readonly Dictionary<int, Control> _paneBodies = new();
    private readonly List<Button> _periodButtons = new();
    private int _periodFocus;

    // Overlay the ring so focus changes never resize or reflow the content.
    private Control WithPaneFocus(Control body, int pane)
    {
        _paneBodies[pane] = body;
        body.Focusable = true;
        var ring = new Border
        {
            Name = "PaneFocus" + pane,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(2),
            Margin = new Thickness(pane is 1 or 2 ? 2 : 0, 0),
            IsHitTestVisible = false
        };
        var grid = new Grid();
        grid.Children.Add(body);
        grid.Children.Add(ring);
        body.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsKeyboardFocusWithinProperty)
                ring.BorderBrush = body.IsKeyboardFocusWithin ? WorkspaceColors.Accent : Brushes.Transparent;
        };
        return grid;
    }

    private void FocusPane(int pane)
    {
        if (pane == 0) { _periodButtons[_periodFocus].Focus(); return; }
        if (pane == 3)
        {
            if (_activePageId is not null) FocusEditingSurface();
            return;
        }
        FocusPaneList(pane == 1 ? _notes : pane == 2 ? _pages : _searchResults);
    }

    private void FocusPaneList(ListBox list)
    {
        // Keep focus on the list: focusing a row issues another bring-into-view
        // request after selection/virtualization and can accumulate padding offsets.
        list.Focus();
        if (list.SelectedIndex < 0) return;
        if (list.SelectedIndex == 0)
        {
            UpdateLayout();
            list.GetVisualDescendants().OfType<ScrollViewer>().FirstOrDefault()?.ScrollToHome();
        }
        else
        {
            // ScrollIntoView on an already visible virtualized row shifts its
            // margin into the panel's offset on every call. Only reveal rows
            // that actually fall outside the viewport.
            UpdateLayout();
            var row = list.ContainerFromIndex(list.SelectedIndex);
            var point = row?.TranslatePoint(default, list);
            if (row is null || point is null || point.Value.Y < 0 ||
                point.Value.Y + row.Bounds.Height > list.Bounds.Height)
                list.ScrollIntoView(list.SelectedIndex);
        }
        UpdateLayout();
    }

    private bool HandlePaneNavigation(KeyEventArgs e)
    {
        if (IsDetachedEditor && WorkspaceModifiers(e.KeyModifiers) == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key is Key.Left or Key.Right)
        {
            if (e.Key == Key.Right && _searchResultsPane.IsVisible) _searchResults.Focus();
            else FocusEditingSurface();
            e.Handled = true;
            return true;
        }
        if (IsPageDetached && _detachedPageNotice?.IsKeyboardFocusWithin == true &&
            WorkspaceModifiers(e.KeyModifiers) == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key is Key.Left or Key.Right)
        {
            if (e.Key == Key.Left) FocusPaneList(_pages);
            else if (_searchResultsPane.IsVisible) FocusPaneList(_searchResults);
            e.Handled = true;
            return true;
        }
        var pane = _paneBodies.FirstOrDefault(p => p.Value.IsKeyboardFocusWithin).Key;
        var within = _paneBodies.Values.Any(p => p.IsKeyboardFocusWithin);
        if (WorkspaceModifiers(e.KeyModifiers) == (KeyModifiers.Control | KeyModifiers.Shift) && e.Key is Key.Left or Key.Right)
        {
            // Toolbar focus belongs to its surrounding pane for horizontal navigation.
            if (!within)
            {
                var owner = _paneBodies.FirstOrDefault(p =>
                    p.Value.GetVisualAncestors().OfType<DockPanel>().FirstOrDefault()?.IsKeyboardFocusWithin == true);
                if (owner.Value is null) return false;
                pane = owner.Key;
            }
            var target = pane + (e.Key == Key.Left ? -1 : 1);
            if (target >= 0 && target <= (_searchResultsPane.IsVisible ? 4 : 3))
            {
                CancelPendingCommand();
                _commandPrompt = null;
                _promptOpeningText = null;
                FocusPane(target);
            }
            e.Handled = true;
            return true;
        }
        if (within && pane == 2 && e.KeyModifiers is (KeyModifiers.None or KeyModifiers.Shift) && e.Key is (Key.Up or Key.Down))
        {
            NavigatePages(e.Key == Key.Up ? -1 : 1, e.KeyModifiers == KeyModifiers.Shift);
            e.Handled = true;
            return true;
        }
        if (!within || e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Up or Key.Down)) return false;
        if (pane == 3) return false; // Preserve native editor and Vim cursor movement.
        var delta = e.Key == Key.Up ? -1 : 1;
        if (pane == 0)
        {
            _periodFocus = Math.Clamp(_periodFocus + delta, 0, _periodButtons.Count - 1);
            _periodButtons[_periodFocus].Focus();
            // Moving onto the trash button must not open its dialog.
            if (_periodFocus < _periodButtons.Count - 1 && _timelinePeriod != _periodFocus)
                SelectTimelinePeriod(_periodFocus);
        }
        else
        {
            var list = pane == 1 ? _notes : pane == 2 ? _pages : _searchResults;
            if (list.ItemCount > 0)
                list.SelectedIndex = list.SelectedIndex < 0 ? 0 : Math.Clamp(list.SelectedIndex + delta, 0, list.ItemCount - 1);
            FocusPaneList(list);
        }
        e.Handled = true;
        return true;
    }
}
