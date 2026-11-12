using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace CursorMeter.App;

internal static class Palette
{
    public static readonly IImmutableSolidColorBrush Background = Brush(28, 28, 28);
    public static readonly IImmutableSolidColorBrush Border = Brush(58, 58, 58);
    public static readonly IImmutableSolidColorBrush Text = Brush(236, 234, 226);
    public static readonly IImmutableSolidColorBrush Muted = Brush(196, 192, 180);
    public static readonly IImmutableSolidColorBrush Track = Brush(48, 48, 48);
    public static readonly IImmutableSolidColorBrush CursorPool = Brush(125, 206, 160);
    public static readonly IImmutableSolidColorBrush OtherPool = Brush(224, 177, 90);
    public static readonly IImmutableSolidColorBrush Hot = Brush(224, 106, 92);

    private static ImmutableSolidColorBrush Brush(byte r, byte g, byte b) => new(Color.FromRgb(r, g, b));
}
