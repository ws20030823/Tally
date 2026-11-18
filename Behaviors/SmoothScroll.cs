using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace TaskDesk.Behaviors;

// Animate pixel offsets while leaving the library's scroll bars and virtualizing panel in charge.
public static class SmoothScroll
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(SmoothScroll), new PropertyMetadata(false, EnabledChanged));
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
        "State", typeof(State), typeof(SmoothScroll));
    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);
    private static void EnabledChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        if (element is not FrameworkElement owner) return;
        if (owner.GetValue(StateProperty) is State old) old.Detach();
        owner.ClearValue(StateProperty);
        if ((bool)e.NewValue) owner.SetValue(StateProperty, new State(owner));
    }

    private sealed class State
    {
        private readonly FrameworkElement owner;
        private ScrollViewer? viewer;
        private bool active;
        private double target;
        private TimeSpan? lastFrame;
        private TimeSpan? started;

        public State(FrameworkElement owner)
        {
            this.owner = owner;
            owner.Loaded += Loaded;
            owner.Unloaded += Unloaded;
            owner.PreviewMouseWheel += Wheel;
            owner.PreviewMouseDown += CancelMouse;
            owner.PreviewKeyDown += CancelKey;
            if (owner.IsLoaded) ResolveViewer();
        }
        private void Loaded(object sender, RoutedEventArgs e) => ResolveViewer();
        private void ResolveViewer()
        {
            owner.ApplyTemplate();
            viewer = owner as ScrollViewer ?? FindViewer(owner);
        }
        private static ScrollViewer? FindViewer(DependencyObject root)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);
                if (child is ScrollViewer scroll) return scroll;
                if (FindViewer(child) is { } nested) return nested;
            }
            return null;
        }
        private void Wheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Handled || viewer is null || e.Delta == 0 || Keyboard.Modifiers != ModifierKeys.None) return;
            // Keep nested editors, combo popups and their own scrolling behavior independent.
            for (var node = e.OriginalSource as DependencyObject; node is not null && node != owner;
                 node = node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node))
                if (node is ScrollViewer nested && nested != viewer) return;
            var lines = SystemParameters.WheelScrollLines;
            if (lines == 0) return;
            var step = lines < 0 ? viewer.ViewportHeight : lines * 16.0;
            var next = Math.Clamp((active ? target : viewer.VerticalOffset) - e.Delta / 120.0 * step, 0, viewer.ScrollableHeight);
            if (Math.Abs(next - viewer.VerticalOffset) < .1 && !active) return;
            target = next;
            started = null;
            e.Handled = true;
            if (active) return;
            active = true;
            lastFrame = null;
            CompositionTarget.Rendering += Render;
        }
        private void Render(object? sender, EventArgs e)
        {
            if (viewer is null || !owner.IsVisible) { Stop(); return; }
            var now = ((RenderingEventArgs)e).RenderingTime;
            started ??= now;
            var elapsed = lastFrame is { } previous ? Math.Clamp((now - previous).TotalSeconds, 0, .05) : 1.0 / 60;
            lastFrame = now;
            target = Math.Clamp(target, 0, viewer.ScrollableHeight);
            var distance = target - viewer.VerticalOffset;
            // Layout rounding can leave a sub-pixel residual; never keep a render subscription alive for it.
            if (Math.Abs(distance) < 1.5 || (now - started.Value).TotalSeconds >= .25) { viewer.ScrollToVerticalOffset(target); Stop(); return; }
            viewer.ScrollToVerticalOffset(viewer.VerticalOffset + distance * (1 - Math.Exp(-elapsed / .045)));
        }
        private void CancelMouse(object sender, MouseButtonEventArgs e) => Stop();
        private void CancelKey(object sender, KeyEventArgs e) => Stop();
        private void Unloaded(object sender, RoutedEventArgs e) { Stop(); viewer = null; }
        private void Stop()
        {
            if (active) CompositionTarget.Rendering -= Render;
            active = false;
            lastFrame = null; started = null;
        }
        public void Detach()
        {
            Stop();
            owner.Loaded -= Loaded; owner.Unloaded -= Unloaded;
            owner.PreviewMouseWheel -= Wheel; owner.PreviewMouseDown -= CancelMouse; owner.PreviewKeyDown -= CancelKey;
        }
    }
}
