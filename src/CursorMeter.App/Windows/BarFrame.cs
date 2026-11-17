using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace CursorMeter.App.Windows;

/// <summary>
/// The box behind the bars, in device-independent pixels: the fill, the outline, the corners and
/// how far the box sits in from the window edge. <see cref="Resolve"/> settles it from the
/// settings, the island look and whether the bars float.
/// </summary>
/// <param name="Transparent">The window itself must be transparent, because the box does not cover it or is not opaque.</param>
internal sealed record BarFrame(
    Color Fill,
    Color? Outline,
    double OutlineWidth,
    double Radius,
    double Margin,
    double Inset,
    bool Transparent)
{
    public IBrush FillBrush => new ImmutableSolidColorBrush(Fill);

    public IPen? OutlinePen => Outline is { } color ? new Pen(new ImmutableSolidColorBrush(color), OutlineWidth) : null;

    /// <summary>
    /// An island of the Start11 taskbar always keeps the colours Start11 gives its islands. Anywhere
    /// else, floating or in the plain box, the theme of the settings decides.
    /// </summary>
    /// <param name="island">The island look found on the taskbar, or null when there is no island to copy.</param>
    /// <param name="floating">The bars float above the taskbar because the island has no room.</param>
    /// <param name="scaling">Window pixels per device-independent pixel.</param>
    public static BarFrame Resolve(AppSettings settings, IslandLook? island, bool floating, double scaling)
    {
        double pixel = 1 / scaling;
        if (island is not null && !floating)
        {
            return new BarFrame(
                island.Fill, island.Outline, island.OutlineWidth, island.Radius * pixel, island.Margin * pixel, pixel / 2, true);
        }

        var theme = settings.Theme == BarTheme.Auto
            ? (island is null ? BarTheme.Dark : BarTheme.Start11)
            : settings.Theme;
        // Floating bars sit in a window larger than the island, so the outline has room outside it.
        double margin = (island?.Margin ?? 0) * pixel;
        double inset = island is null ? 0.5 : pixel / 2;

        Color fill;
        Color? outline;
        double width = 1;
        if (theme == BarTheme.Start11 && island is null)
        {
            // In the plain box the bars sit on the one bar Start11 draws (solid or rounded), which already
            // has its colour and outline. A square box with that outline inside a rounded bar looks wrong,
            // so the bars are drawn straight onto it, with no box of their own.
            fill = Colors.Transparent;
            outline = null;
        }
        else if (theme == BarTheme.Start11)
        {
            fill = island!.Fill;
            outline = island.Outline;
            width = island.OutlineWidth;
        }
        else
        {
            var background = Palette.Background.Color;
            byte alpha = theme == BarTheme.Transparent ? (byte)0 : (byte)Math.Round(255d * settings.BackgroundOpacity / 100);
            fill = Color.FromArgb(alpha, background.R, background.G, background.B);
            outline = settings.Outline ? BarStyle.ColorOf(settings.OutlineColor) ?? Palette.Border.Color : null;
        }

        double radius = settings.Radius >= 0 ? settings.Radius : (island?.Radius ?? 0) * pixel;
        bool transparent = fill.A < 255 || radius > 0 || margin > 0;
        return new BarFrame(fill, outline, width, radius, margin, inset, transparent);
    }
}
