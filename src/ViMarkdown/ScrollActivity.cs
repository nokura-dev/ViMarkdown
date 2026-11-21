using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace ViMarkdown;

// Hide only the visuals, preserving the viewport and the scroll range.
internal sealed class ScrollActivity : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ScrollActivity, ScrollViewer, bool>("Enabled");
    private static readonly ConditionalWeakTable<ScrollViewer, Activity> Activities = new();

    static ScrollActivity()
    {
        EnabledProperty.Changed.AddClassHandler<ScrollViewer>((viewer, _) =>
        {
            if (viewer.GetValue(EnabledProperty)) Activities.GetValue(viewer, v => new Activity(v));
            else if (Activities.TryGetValue(viewer, out var activity))
            {
                activity.Dispose();
                Activities.Remove(viewer);
            }
        });
    }

    private sealed class Activity : IDisposable
    {
        private readonly ScrollViewer _viewer;
        private readonly List<ScrollBar> _bars = new();
        private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(650) };
        private bool _showing, _interacting, _disposed;

        public Activity(ScrollViewer viewer)
        {
            _viewer = viewer;
            _hideTimer.Tick += (_, _) =>
            {
                _hideTimer.Stop();
                if (!_interacting) SetVisible(false);
            };
            viewer.TemplateApplied += OnTemplateApplied;
            viewer.AttachedToVisualTree += OnAttached;
            viewer.DetachedFromVisualTree += OnDetached;
            viewer.ScrollChanged += OnScroll;
            viewer.AddHandler(InputElement.PointerWheelChangedEvent, OnWheel,
                RoutingStrategies.Bubble, handledEventsToo: true);
            Dispatcher.UIThread.Post(FindBars, DispatcherPriority.Loaded);
        }

        private void OnTemplateApplied(object? sender, TemplateAppliedEventArgs e) => Dispatcher.UIThread.Post(FindBars, DispatcherPriority.Loaded);
        private void OnAttached(object? sender, VisualTreeAttachmentEventArgs e) => Dispatcher.UIThread.Post(FindBars, DispatcherPriority.Loaded);
        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            _hideTimer.Stop();
            _interacting = false;
            SetVisible(false);
        }

        private void FindBars()
        {
            if (_disposed) return;
            RemoveBarHandlers();
            _bars.Clear();
            _bars.AddRange(_viewer.GetVisualDescendants().OfType<ScrollBar>().Where(b =>
                b.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() == _viewer));
            foreach (var bar in _bars)
            {
                bar.AddHandler(InputElement.PointerPressedEvent, OnBarPressed,
                    RoutingStrategies.Tunnel, handledEventsToo: true);
                bar.AddHandler(InputElement.PointerReleasedEvent, OnBarReleased,
                    RoutingStrategies.Bubble, handledEventsToo: true);
                bar.PointerCaptureLost += OnBarCaptureLost;
            }
            SetVisible(_showing);
        }

        private void SetVisible(bool visible)
        {
            _showing = visible;
            foreach (var bar in _bars)
            {
                bar.Opacity = visible ? 1 : 0;
                bar.IsHitTestVisible = visible;
            }
        }

        private void Show()
        {
            SetVisible(true);
            _hideTimer.Stop();
            _hideTimer.Start();
        }

        private void OnScroll(object? sender, ScrollChangedEventArgs e)
        {
            if (e.OffsetDelta != default) Show();
        }
        private void OnWheel(object? sender, PointerWheelEventArgs e) => Show();
        private void OnBarPressed(object? sender, PointerPressedEventArgs e)
        {
            _interacting = true;
            Show();
            _hideTimer.Stop();
        }
        private void OnBarReleased(object? sender, PointerReleasedEventArgs e) => EndInteraction();
        private void OnBarCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndInteraction();
        private void EndInteraction()
        {
            if (!_interacting) return;
            _interacting = false;
            Show();
        }
        private void RemoveBarHandlers()
        {
            foreach (var bar in _bars)
            {
                bar.RemoveHandler(InputElement.PointerPressedEvent, OnBarPressed);
                bar.RemoveHandler(InputElement.PointerReleasedEvent, OnBarReleased);
                bar.PointerCaptureLost -= OnBarCaptureLost;
            }
        }
        public void Dispose()
        {
            _disposed = true;
            _hideTimer.Stop();
            RemoveBarHandlers();
            _viewer.TemplateApplied -= OnTemplateApplied;
            _viewer.AttachedToVisualTree -= OnAttached;
            _viewer.DetachedFromVisualTree -= OnDetached;
            _viewer.ScrollChanged -= OnScroll;
            _viewer.RemoveHandler(InputElement.PointerWheelChangedEvent, OnWheel);
            SetVisible(true);
        }
    }
}
