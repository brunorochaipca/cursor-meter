using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CursorMeter.App;

/// <summary>The tray icon: the logo in assets/logo.svg, drawn on the same 256 grid at <see cref="Size"/> pixels.</summary>
internal static class AppIcon
{
    public const int Size = 32;

    private const double Grid = 256;
    private static readonly IBrush Fill = Palette.Background;
    private static readonly IPen Edge = new Pen(new SolidColorBrush(Color.FromRgb(0x5E, 0x81, 0xAC)), 8);
    private static readonly IBrush Track = new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3A));

    public static WindowIcon Create()
    {
        using var bitmap = Render();
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        return new WindowIcon(stream);
    }

    public static RenderTargetBitmap Render(int size = Size)
    {
        var bitmap = new RenderTargetBitmap(new PixelSize(size, size));
        using (var context = bitmap.CreateDrawingContext())
        using (context.PushTransform(Matrix.CreateScale(size / Grid, size / Grid)))
        {
            context.DrawRectangle(Fill, Edge, new Rect(6, 6, 244, 244), 60, 60);
            Bar(context, 72, 0.42, Palette.CursorPool);
            Bar(context, 140, 0.78, Palette.OtherPool);
        }

        return bitmap;
    }

    private static void Bar(DrawingContext context, double y, double share, IBrush color)
    {
        context.DrawRectangle(Track, null, new Rect(44, y, 168, 44), 14, 14);
        context.DrawRectangle(color, null, new Rect(44, y, 168 * share, 44), 14, 14);
    }
}
