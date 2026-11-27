using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Controls.Presenters;
using Path = Avalonia.Controls.Shapes.Path;

namespace ViMarkdown;

internal static class ToolbarButtons
{
    // Small, font-independent outline icons on a 16 × 16 grid.
    private static readonly Dictionary<string, string> Icons = new()
    {
        ["menu"] = "M2,4 L14,4 M2,8 L14,8 M2,12 L14,12",
        ["windows"] = "M5,4 L5,1 L15,1 L15,11 L12,11 M1,5 L11,5 L11,15 L1,15 Z M1,8 L11,8 M5,4 L15,4",
        ["slideshow"] = "M2,2 L14,2 L14,11 L2,11 Z M8,11 L8,14 M5,15 L8,13 L11,15 M6,4 L10,6.5 L6,9 Z",
        ["normal"] = "M3,2 L3,13 L6,10 L8,14 L10,13 L8,9 L12,9 Z",
        ["visual"] = "M2,6 L2,2 L6,2 M10,2 L14,2 L14,6 M14,10 L14,14 L10,14 M6,14 L2,14 L2,10 M5,8 L11,8",
        ["insert"] = "M3,11 L2,14 L5,13 L13,5 L10,2 Z M9,3 L12,6",
        ["undo"] = "M6,3 L2,7 L6,11 M2,7 L10,7 Q14,7 14,11 L14,13",
        ["redo"] = "M10,3 L14,7 L10,11 M14,7 L6,7 Q2,7 2,11 L2,13",
        ["note"] = "M8,2 L8,14 M2,8 L14,8",
        ["page"] = "M7,14 L3,14 L3,2 L9,2 L13,6 L13,8 M9,2 L9,6 L13,6 M11,9 L11,15 M8,12 L14,12",
        ["rename"] = "M3,11 L2,14 L5,13 L13,5 L10,2 Z M9,3 L12,6",
        ["move"] = "M7,3 L2,3 L2,13 L7,13 M6,8 L14,8 M10,4 L14,8 L10,12",
        ["delete"] = "M2,4 L14,4 M6,4 L6,2 L10,2 L10,4 M4,4 L5,14 L11,14 L12,4 M7,6 L7,12 M9,6 L9,12",
        ["export-md"] = "M12,7 L12,5 L9,2 L3,2 L3,14 L13,14 L13,8 M9,2 L9,5 L12,5 M9,7 L9,12 M6,9 L9,12 L12,9",
        ["export-docx"] = "M5,11 L2,11 L2,1 L8,1 M14,7 L14,5 L11,2 L5,2 L5,14 L15,14 L15,8 M11,2 L11,5 L14,5 M11,7 L11,12 M8,9 L11,12 L14,9"
    };

    public static Path CreateIcon(string icon, IBrush? stroke = null) => new()
    {
        Data = Geometry.Parse(Icons[icon]), Stroke = stroke ?? WorkspaceColors.Muted,
        StrokeThickness = 1.25, Width = 16, Height = 16, Stretch = Stretch.Uniform
    };

    public static Button Create(string icon, string name, string tooltip, string? tag = null)
    {
        var button = new Button
        {
            // Action icons must not take focus away from the pane's contents.
            Focusable = false, IsTabStop = false,
            Width = 28, Height = 28, MinWidth = 28, MinHeight = 28,
            Padding = new Thickness(4), Margin = new Thickness(0),
            Background = Brushes.Transparent, Foreground = WorkspaceColors.Muted,
            BorderThickness = new Thickness(0), CornerRadius = new CornerRadius(3),
            HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            Tag = tag ?? name,
            Content = CreateIcon(icon)
        };
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, tooltip);
        var hover = new Style(s => s.OfType<Button>().Class(":pointerover").Template().OfType<ContentPresenter>());
        hover.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, WorkspaceColors.Hover));
        button.Styles.Add(hover);
        var pressed = new Style(s => s.OfType<Button>().Class(":pressed").Template().OfType<ContentPresenter>());
        pressed.Setters.Add(new Setter(ContentPresenter.BackgroundProperty, WorkspaceColors.Pressed));
        button.Styles.Add(pressed);
        return button;
    }
}
