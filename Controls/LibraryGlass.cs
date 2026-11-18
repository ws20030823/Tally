using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace TaskDesk.Controls;

// Applies the upstream library's bitmap to existing controls; no application-owned glass shader.
public static class LibraryGlass
{
    public static readonly DependencyProperty HostProperty = DependencyProperty.RegisterAttached("Host", typeof(LibraryGlassBackdrop), typeof(LibraryGlass), new PropertyMetadata(null, Changed));
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(LibraryGlass), new PropertyMetadata(false, Changed));
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.RegisterAttached("Radius", typeof(double), typeof(LibraryGlass), new PropertyMetadata(18.0, Changed));
    public static readonly DependencyProperty TintAmountProperty = DependencyProperty.RegisterAttached("TintAmount", typeof(double), typeof(LibraryGlass), new PropertyMetadata(35.0, Changed));
    private static readonly DependencyPropertyKey FramePropertyKey = DependencyProperty.RegisterAttachedReadOnly(
        "Frame", typeof(ImageBrush), typeof(LibraryGlass), new PropertyMetadata(null));
    public static readonly DependencyProperty FrameProperty = FramePropertyKey.DependencyProperty;
    public static ImageBrush? GetFrame(DependencyObject target) => (ImageBrush?)target.GetValue(FrameProperty);
    private static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached("State", typeof(State), typeof(LibraryGlass));
    public static LibraryGlassBackdrop? GetHost(DependencyObject target) => (LibraryGlassBackdrop?)target.GetValue(HostProperty);
    public static void SetHost(DependencyObject target, LibraryGlassBackdrop? value) => target.SetValue(HostProperty, value);
    public static bool GetIsEnabled(DependencyObject target) => (bool)target.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(DependencyObject target, bool value) => target.SetValue(IsEnabledProperty, value);
    public static double GetRadius(DependencyObject target) => (double)target.GetValue(RadiusProperty);
    public static void SetRadius(DependencyObject target, double value) => target.SetValue(RadiusProperty, value);
    public static double GetTintAmount(DependencyObject target) => (double)target.GetValue(TintAmountProperty);
    public static void SetTintAmount(DependencyObject target, double value) => target.SetValue(TintAmountProperty, value);
    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not FrameworkElement element) return;
        var state = (State?)target.GetValue(StateProperty);
        var host = GetHost(target);
        if (GetIsEnabled(target) && host is not null)
        {
            if (state?.Host == host) { state.RequestRender(); return; }
            state?.Dispose(); target.SetValue(StateProperty, new State(element, host));
        }
        else { state?.Dispose(); target.ClearValue(StateProperty); }
    }
    private sealed class State : IDisposable
    {
        public LibraryGlassBackdrop Host { get; }
        private readonly FrameworkElement target;
        private readonly DispatcherTimer timer;
        private bool attached, disposed;
        public State(FrameworkElement target, LibraryGlassBackdrop host)
        {
            this.target = target; Host = host;
            timer = new DispatcherTimer(DispatcherPriority.Background, target.Dispatcher) { Interval = TimeSpan.FromMilliseconds(120) };
            timer.Tick += Render;
            target.Loaded += Loaded; target.Unloaded += Unloaded;
            target.SizeChanged += Resized; target.IsVisibleChanged += VisibilityChanged;
            if (target.IsLoaded) Attach();
        }
        private void Loaded(object sender, RoutedEventArgs e) => Attach();
        private void Attach()
        {
            if (!attached) { Host.Invalidated += HostChanged; attached = true; }
            RequestRender();
        }
        private void Unloaded(object sender, RoutedEventArgs e)
        {
            timer.Stop();
            if (attached) { Host.Invalidated -= HostChanged; attached = false; }
        }
        private void Resized(object sender, SizeChangedEventArgs e) => RequestRender();
        private void HostChanged(object? sender, EventArgs e) => RequestRender();
        private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        { if (target.IsVisible) RequestRender(); else timer.Stop(); }
        public void RequestRender()
        {
            timer.Stop();
            if (!disposed && target.IsLoaded && target.IsVisible) timer.Start();
        }
        private void Render(object? sender, EventArgs e)
        {
            timer.Stop();
            if (disposed || !target.IsVisible) return;
            try
            {
                var brush = Host.Render(target, GetRadius(target), GetTintAmount(target));
                if (brush is null) return;
                // Styles bind to this frame. Changing a style/focus trigger cannot silently replace the library bitmap.
                target.SetValue(FramePropertyKey, brush);
            }
            catch (Exception error) when (error is InvalidOperationException or ArgumentException or TypeInitializationException or DllNotFoundException)
            { Host.ReportFailure(error); }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; timer.Stop(); timer.Tick -= Render;
            if (attached) Host.Invalidated -= HostChanged;
            target.Loaded -= Loaded; target.Unloaded -= Unloaded;
            target.SizeChanged -= Resized; target.IsVisibleChanged -= VisibilityChanged;
            target.ClearValue(FramePropertyKey);
        }
    }
}
