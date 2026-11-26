using System.Text.Json;
using Avalonia;
using Avalonia.Controls;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private sealed record Placement(int X, int Y, double Width, double Height, bool Maximized);
    private Placement? _normalPlacement;
    private bool _placementOpened;

    private void ConfigureWindowPlacement()
    {
        Placement? saved = null;
        try
        {
            if (_store.GetSetting("window-placement") is { } json)
                saved = JsonSerializer.Deserialize<Placement>(json);
        }
        catch (JsonException) { /* Ignore invalid settings and use the default size. */ }

        if (saved is { } placement && double.IsFinite(placement.Width) && double.IsFinite(placement.Height)
            && placement.Width > 0 && placement.Height > 0)
        {
            Width = Math.Max(MinWidth, placement.Width);
            Height = Math.Max(Math.Max(MinHeight, 200), placement.Height);
            var position = new PixelPoint(placement.X, placement.Y);
            var screen = Screens.ScreenFromPoint(position) ?? Screens.Primary;
            if (screen is not null)
            {
                var area = screen.WorkingArea;
                Width = Math.Max(MinWidth, Math.Min(Width, area.Width / screen.Scaling));
                Height = Math.Max(MinHeight, Math.Min(Height, area.Height / screen.Scaling));
                position = new PixelPoint(
                    Math.Clamp(position.X, area.X, Math.Max(area.X, area.Right - (int)Math.Ceiling(Width * screen.Scaling))),
                    Math.Clamp(position.Y, area.Y, Math.Max(area.Y, area.Bottom - (int)Math.Ceiling(Height * screen.Scaling))));
            }
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = position;
            _normalPlacement = new(position.X, position.Y, Width, Height, false);
            if (placement.Maximized) WindowState = WindowState.Maximized;
        }

        Opened += (_, _) => { _placementOpened = true; RememberNormalPlacement(); };
        PositionChanged += (_, _) => RememberNormalPlacement();
        SizeChanged += (_, _) => RememberNormalPlacement();
    }

    private void RememberNormalPlacement()
    {
        // Keep the restored size when closing a maximized or minimized window.
        if (!_placementOpened || _isSlideshow || WindowState != WindowState.Normal) return;
        _normalPlacement = new(Position.X, Position.Y, ClientSize.Width, ClientSize.Height, false);
    }

    private void SaveWindowPlacement()
    {
        if (_normalPlacement is { } placement)
            _store.SetSetting("window-placement", JsonSerializer.Serialize(placement with
            {
                Maximized = (_isSlideshow ? _slideshowWindowState : WindowState) == WindowState.Maximized
            }));
    }
}
