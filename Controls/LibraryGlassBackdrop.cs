using System.Windows;
using System.Windows.Media;
using LiquidGlass.Skia;
using LiquidGlass.Wpf;
using SkiaSharp;

namespace TaskDesk.Controls;

// WPF hosting only: all material/shader rendering comes from the LiquidGlass NuGet packages.
public sealed class LibraryGlassBackdrop : IDisposable
{
    private readonly FrameworkElement source;
    private GlassPainter? painter;
    private SKImage? snapshot;
    private double snapshotScale;
    private bool dirty = true, disposed;
    public event EventHandler? Invalidated;
    public event EventHandler<string>? RenderFailed;

    public LibraryGlassBackdrop(FrameworkElement source)
    {
        this.source = source;
        source.SizeChanged += SourceChanged;
    }
    private void SourceChanged(object sender, SizeChangedEventArgs e) => Refresh();
    public void Refresh()
    {
        if (disposed) return;
        dirty = true;
        Invalidated?.Invoke(this, EventArgs.Empty);
    }
    internal ImageBrush? Render(FrameworkElement target, double radius, double tintAmount)
    {
        if (disposed || !source.IsLoaded || source.ActualWidth <= 0 || source.ActualHeight <= 0 ||
            target.ActualWidth <= 0 || target.ActualHeight <= 0) return null;
        // Render only the material, at the window DPI. Labels stay native WPF text.
        var scale = VisualTreeHelper.GetDpi(target).DpiScaleX;
        painter ??= new GlassPainter();
        if (dirty || snapshot is null || snapshotScale != scale)
        {
            var next = VisualBackdrop.Capture(source, Color.FromRgb(230, 230, 234), scale);
            try { painter.SetSource(next); }
            catch { next.Dispose(); throw; }
            var previous = snapshot;
            snapshot = next; snapshotScale = scale; dirty = false;
            previous?.Dispose();
        }
        var origin = target.TransformToVisual(source).Transform(new Point());
        var width = Math.Max(1, (int)Math.Ceiling(target.ActualWidth * scale));
        var height = Math.Max(1, (int)Math.Ceiling(target.ActualHeight * scale));
        var bitmap = new GlassBitmap(width, height, scale);
        var bounds = SKRect.Create((float)(origin.X * scale), (float)(origin.Y * scale), width, height);
        bitmap.Render(canvas =>
        {
            canvas.Translate(-bounds.Left, -bounds.Top);
            painter.DrawSurface(canvas, bounds, (float)(radius * scale),
                new GlassSurfaceOptions(TintAmount: (float)Math.Clamp(tintAmount, 0, 100), Density: (float)scale));
        });
        bitmap.Bitmap.Freeze();
        var brush = new ImageBrush(bitmap.Bitmap) { Stretch = Stretch.Fill };
        brush.Freeze();
        return brush;
    }
    internal void ReportFailure(Exception error) => RenderFailed?.Invoke(this, error.GetBaseException().Message);
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        source.SizeChanged -= SourceChanged;
        painter?.Dispose(); painter = null;
        snapshot?.Dispose(); snapshot = null;
        Invalidated = null; RenderFailed = null;
    }
}
