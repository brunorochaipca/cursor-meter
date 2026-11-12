using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;

namespace CursorMeter.App;

internal static class AppIcon
{
    public static WindowIcon Create()
    {
        using var bitmap = new RenderTargetBitmap(new PixelSize(32, 32));
        using (var context = bitmap.CreateDrawingContext())
        {
            context.FillRectangle(Palette.Background, new Rect(0, 0, 32, 32));
            context.FillRectangle(Palette.CursorPool, new Rect(4, 9, 24, 5));
            context.FillRectangle(Palette.OtherPool, new Rect(4, 18, 15, 5));
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        return new WindowIcon(stream);
    }
}
