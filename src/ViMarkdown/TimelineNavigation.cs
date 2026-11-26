using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Automation;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private int _timelinePeriod;
    private Action? _updateTimelineSelection;
    private void SelectTimelinePeriod(int period)
    {
        if (!SaveCurrent()) return;
        _timelinePeriod = period;
        _updateTimelineSelection?.Invoke();
        ReloadLists();
        // Clearing an unmatched page focuses the notes list. Keep an empty
        // timeline selection on its button so period navigation can continue.
        if (_pages.ItemCount == 0) _periodButtons[period].Focus();
    }

    private Control BuildTimeline()
    {
        var stack = new StackPanel { Background = WorkspaceColors.Chrome };
        var timelineHeader = new Border
        {
            Height = 38,
            BorderBrush = WorkspaceColors.Divider,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        AutomationProperties.SetName(timelineHeader, "期間");
        ToolTip.SetTip(timelineHeader, "更新日で絞り込み");
        
        var labels = new[] { "全て", "今日", "昨日", "今週", "先週", "今月", "先月", "先々月" };
        var buttons = new List<Button>();
        for (var index = 0; index < labels.Length; index++)
        {
            var period = index;
            var content = new StackPanel { Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var geometry = index == 0
                ? "M2,5 L8,2 L14,5 L8,8 Z M2,8 L8,11 L14,8 M2,11 L8,14 L14,11"
                : "M2,4 L14,4 L14,14 L2,14 Z M2,7 L14,7 M5,2 L5,5 M11,2 L11,5";
            if (index > 0) geometry += index switch
            {
                1 => " M8,9 L8,12",
                2 => " M9,9 L6,11 L9,13 M6,11 L11,11",
                3 => " M5,10 L11,10",
                4 => " M8,9 L5,11 L8,13 M5,11 L11,11",
                5 => " M5,9 L5,12 M8,9 L8,12 M11,9 L11,12",
                6 => " M9,9 L6,11 L9,13",
                _ => " M7,9 L4,11 L7,13 M11,9 L8,11 L11,13"
            };
            content.Children.Add(new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(geometry), Width = 14, Height = 14, Stretch = Stretch.Uniform, Stroke = WorkspaceColors.Muted, StrokeThickness = 1.2, HorizontalAlignment = HorizontalAlignment.Center });
            content.Children.Add(new TextBlock { Text = labels[index], FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center });
            var button = new Button { Name = "Timeline" + index, Content = content, Width = 63, Height = 54, Padding = new Thickness(0), CornerRadius = new CornerRadius(0), BorderThickness = new Thickness(3,0,0,0), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(button, labels[index]);
            ToolTip.SetTip(button, labels[index] + "に更新したページ");
            buttons.Add(button);
            _periodButtons.Add(button);
            button.GotFocus += (_, _) => _periodFocus = period;
            button.Click += (_, _) => SelectTimelinePeriod(period);
            stack.Children.Add(button);
        }
        void UpdateSelection()
        {
            for (var i = 0; i < buttons.Count; i++)
            {
                buttons[i].Background = i == _timelinePeriod ? WorkspaceColors.Selected : Brushes.Transparent;
                buttons[i].BorderBrush = i == _timelinePeriod ? WorkspaceColors.Accent : Brushes.Transparent;
                buttons[i].Foreground = i == _timelinePeriod ? WorkspaceColors.Accent : WorkspaceColors.Text;
            }
        }
        _updateTimelineSelection = UpdateSelection;
        UpdateSelection();
        var trash = ToolbarButtons.Create("delete", "ごみ箱", "ごみ箱");
        // This is a timeline navigation item, rather than a toolbar action.
        trash.Focusable = true;
        trash.IsTabStop = true;
        trash.Name = "TrashButton";
        trash.Width = 63;
        trash.Height = 54;
        trash.CornerRadius = new CornerRadius(0);
        trash.Click += async (_, _) =>
        {
            if (!OwnedWindows.Any(w => w.IsVisible) && SaveCurrent()) await ShowTrash();
        };
        var layout = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        layout.Children.Add(stack);
        _periodButtons.Add(trash);
        trash.GotFocus += (_, _) => _periodFocus = _periodButtons.Count - 1;
        Grid.SetRow(trash, 1);
        layout.Children.Add(trash);
        var pane = new DockPanel();
        DockPanel.SetDock(timelineHeader, Dock.Top);
        pane.Children.Add(timelineHeader);
        pane.Children.Add(WithPaneFocus(layout, 0));
        return new Border { Background = WorkspaceColors.Chrome, BorderBrush = WorkspaceColors.Divider, BorderThickness = new Thickness(0,0,1,0), Child = pane };
    }
}
