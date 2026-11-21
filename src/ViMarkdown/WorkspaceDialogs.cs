using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private async Task<string?> AskName(Control anchor, string title, string initial, string action = "保存")
    {
        // Use the Japanese font's metrics for Latin text as well, so entering
        // Japanese does not switch the baseline or resize the input line.
        var nameFont = Typography.JapaneseUiFont;
        using var japaneseLine = new Avalonia.Media.TextFormatting.TextLayout(
            "名前Ag", new Typeface(nameFont), Typography.Ui, WorkspaceColors.Text);
        var input = new TextBox
        {
            Text = initial,
            Watermark = "名前",
            Background = WorkspaceColors.Editor,
            Foreground = WorkspaceColors.Text,
            BorderBrush = WorkspaceColors.Divider,
            CornerRadius = new CornerRadius(3),
            FontSize = Typography.Ui,
            FontFamily = nameFont,
            LineHeight = Math.Ceiling(japaneseLine.Height),
            MinHeight = 32,
            Padding = new Thickness(6, 4),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        return await Confirm(anchor, title, input, action)
            ? input.Text!.Trim() : null;
    }

    private async Task<bool> Confirm(Control anchor, string title, Control content, string action)
    {
        var dialog = new Window
        {
            Background = WorkspaceColors.Editor,
            Foreground = WorkspaceColors.Text,
            Title = title,
            Width = 300,
            SizeToContent = SizeToContent.Height,
            CanResize = false,
            ShowInTaskbar = false,
            SystemDecorations = SystemDecorations.None,
            WindowStartupLocation = WindowStartupLocation.Manual
        };
        var stack = new StackPanel { Margin = new Thickness(12), Spacing = 10 };
        stack.Children.Add(new TextBlock { Text = title, FontWeight = FontWeight.SemiBold, FontSize = Typography.Ui });
        stack.Children.Add(content);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        Button ActionButton(string label, bool primary) => new()
        {
            Content = label,
            FontSize = Typography.Ui,
            MinHeight = 28,
            Padding = new Thickness(10, 4),
            CornerRadius = new CornerRadius(3),
            Background = primary ? WorkspaceColors.Accent : WorkspaceColors.Pages,
            Foreground = primary ? Brushes.White : WorkspaceColors.Text,
            BorderBrush = primary ? WorkspaceColors.Accent : WorkspaceColors.Divider,
            BorderThickness = new Thickness(1)
        };
        var cancel = ActionButton("キャンセル", false);
        var ok = ActionButton(action, true);
        cancel.Click += (_, _) => dialog.Close(false);
        ok.Click += (_, _) => dialog.Close(true);
        if (content is TextBox input)
        {
            void Validate() => ok.IsEnabled = !string.IsNullOrWhiteSpace(input.Text);
            input.TextChanged += (_, _) => Validate();
            Validate();
        }
        buttons.Children.Add(cancel); buttons.Children.Add(ok); stack.Children.Add(buttons);
        dialog.Content = new Border
        {
            BorderBrush = WorkspaceColors.Divider,
            BorderThickness = new Thickness(1),
            Background = WorkspaceColors.Editor,
            Child = stack
        };
        void PositionPanel()
        {
            var below = anchor.PointToScreen(new Point(0, anchor.Bounds.Height + 4));
            var screen = Screens.ScreenFromPoint(below);
            if (screen is null) { dialog.Position = below; return; }
            var area = screen.WorkingArea;
            var width = (int)Math.Ceiling(dialog.Width * dialog.RenderScaling);
            var height = (int)Math.Ceiling(dialog.Bounds.Height * dialog.RenderScaling);
            var y = below.Y;
            if (y + height > area.Bottom)
                y = anchor.PointToScreen(new Point(0, -4)).Y - height;
            dialog.Position = new PixelPoint(
                Math.Clamp(below.X, area.X, Math.Max(area.X, area.Right - width)),
                Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - height)));
        }
        dialog.AddHandler(KeyDownEvent, (_, e) =>
        {
            if (e.KeyModifiers != KeyModifiers.None || e.Key is not (Key.Escape or Key.Enter)) return;
            // Conversion keys belong to the IME until composition has finished.
            if (dialog.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.TextPresenter>()
                .Any(p => !string.IsNullOrEmpty(p.PreeditText))) return;
            e.Handled = true;
            if (e.Key == Key.Escape) dialog.Close(false);
            else if (action != "削除" && ok.IsEnabled) dialog.Close(true);
            // Destructive actions require explicitly activating the delete button.
            else if (action == "削除" && ok.IsFocused) dialog.Close(true);
        }, RoutingStrategies.Bubble);
        dialog.Opened += (_, _) =>
        {
            dialog.UpdateLayout();
            PositionPanel();
            if (content is TextBox input) { input.Focus(); input.SelectAll(); }
            else if (action == "削除") cancel.Focus();
            else content.Focus();
        };
        PositionPanel();
        return await dialog.ShowDialog<bool>(this);
    }
}
