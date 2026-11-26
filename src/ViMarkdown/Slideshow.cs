using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace ViMarkdown;

public sealed partial class MainWindow
{
    private bool _isSlideshow;
    private Control? _slideshowWorkspace;
    private WindowState _slideshowWindowState;
    private double _slideshowMinWidth, _slideshowMinHeight;

    private void StartSlideshow()
    {
        if (_isSlideshow || IsPageDetached || _activePageId is null || !_vimInputTail.IsCompleted || _workspaceShortcutRunning ||
            OwnedWindows.Any(w => w.IsVisible)) return;
        _ime.Cancel();
        _commandPrompt = null;
        _promptOpeningText = null;
        SwitchMode(EditorMode.Normal, focusEditor: false);
        CancelPendingCommand();
        // Do not let deferred mode-switch alignment scroll the full-screen view.
        ++_modeSwitchGeneration;
        RememberNormalPlacement();
        _slideshowWindowState = WindowState;
        _slideshowMinWidth = MinWidth;
        _slideshowMinHeight = MinHeight;
        _slideshowWorkspace = (Control)Content!;
        var offset = _preview.Offset;
        _isSlideshow = true;
        _rich.IsPresentation = true;
        _editorArea.Children.Remove(_preview);
        Content = _preview;
        MinWidth = MinHeight = 0;
        WindowState = WindowState.FullScreen;
        _rich.InvalidateVisual();
        Dispatcher.UIThread.Post(() =>
        {
            if (!_isSlideshow) return;
            UpdateLayout();
            _preview.Offset = offset;
            _rich.Focus();
        }, DispatcherPriority.Loaded);
    }

    private void StopSlideshow()
    {
        if (!_isSlideshow) return;
        var offset = _preview.Offset;
        Content = null;
        _editorArea.Children.Insert(0, _preview);
        _rich.IsPresentation = false;
        Content = _slideshowWorkspace;
        _slideshowWorkspace = null;
        MinWidth = _slideshowMinWidth;
        MinHeight = _slideshowMinHeight;
        WindowState = _slideshowWindowState;
        _isSlideshow = false;
        _rich.InvalidateVisual();
        Dispatcher.UIThread.Post(() =>
        {
            if (_isSlideshow) return;
            UpdateLayout();
            _preview.Offset = offset;
            FocusEditingSurface();
        }, DispatcherPriority.Loaded);
    }

    private bool HandleSlideshowKey(KeyEventArgs e)
    {
        if (e.Key == Key.F5 && e.KeyModifiers == KeyModifiers.None)
        {
            if (_isSlideshow) StopSlideshow();
            else StartSlideshow();
            e.Handled = true;
            return true;
        }
        if (!_isSlideshow) return false;
        e.Handled = true;
        if (e.Key == Key.Escape) StopSlideshow();
        else if (e.KeyModifiers == KeyModifiers.None && e.Key is Key.Up or Key.Down)
        {
            var step = Math.Max(1, _rich.BodyLineHeight) * 3;
            _preview.Offset = new Vector(_preview.Offset.X,
                Math.Clamp(_preview.Offset.Y + (e.Key == Key.Up ? -step : step),
                    0, Math.Max(0, _preview.Extent.Height - _preview.Viewport.Height)));
        }
        return true;
    }
}
