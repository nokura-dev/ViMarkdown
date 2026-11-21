using Avalonia.Media;

namespace ViMarkdown;

// Shared palette for navigation, editing surfaces, and interaction states.
internal static class WorkspaceColors
{
    public static readonly IBrush Notes = Brush("#F3F3F3");
    public static readonly IBrush Pages = Brush("#FAFAFA");
    public static readonly IBrush Editor = Brush("#FFFFFF");
    public static readonly IBrush Chrome = Brush("#F3F3F3");
    public static readonly IBrush Canvas = Brush("#FFFFFF");
    public static readonly IBrush Divider = Brush("#DEDEDE");
    public static readonly IBrush Text = Brush("#242424");
    public static readonly IBrush Muted = Brush("#616161");
    public static readonly IBrush Accent = Brush("#007ACC");
    public static readonly IBrush Selected = Brush("#CDE8FF");
    public static readonly IBrush Hover = Brush("#E8E8E8");
    public static readonly IBrush Pressed = Brush("#D6D6D6");
    public static readonly IBrush Selection = Brush("#ADD6FF");
    public static readonly IBrush SearchMatch = Brush("#FFE89A");
    public static readonly IBrush Code = Brush("#A43B00");
    public static readonly IBrush CodeBackground = Brush("#F2F2F2");
    public static readonly IBrush Diagnostic = Brush("#A1260D");
    private static IBrush Brush(string color) => new SolidColorBrush(Color.Parse(color));
}
