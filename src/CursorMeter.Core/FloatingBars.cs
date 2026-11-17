using System.Globalization;

namespace CursorMeter;

/// <summary>
/// What the user chose for the floating bars: where they were dropped and whether they are hidden.
/// Kept as one line of text, <c>left top hidden</c>, with <c>-</c> for a position not set yet.
/// </summary>
public readonly record struct FloatingState(int? Left, int? Top, bool Hidden)
{
    public static FloatingState Parse(string? text)
    {
        var parts = (text ?? string.Empty).Split([' ', ',', ';', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        int? left = null;
        int? top = null;
        if (parts.Length >= 2
            && int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var x)
            && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var y))
        {
            left = x;
            top = y;
        }

        return new FloatingState(left, top, parts.Length >= 3 && parts[2] == "1");
    }

    public string Format()
    {
        string position = Left is { } left && Top is { } top
            ? string.Create(CultureInfo.InvariantCulture, $"{left} {top}")
            : "- -";
        return $"{position} {(Hidden ? 1 : 0)}";
    }
}

/// <summary>
/// Where the bars float when the taskbar is cut into islands and there is no room left for them.
/// All rectangles are in screen pixels.
/// </summary>
public static class FloatingBars
{
    /// <summary>Gap between the bars and the taskbar edge, in pixels at 96 DPI.</summary>
    public const int Gap = 4;

    /// <summary>Just outside the taskbar, at the screen edge where the island would have been.</summary>
    public static PxRect DefaultSlot(PxRect taskbar, PxRect monitor, BarEdge edge, int edgeMargin, int width, int height, int gap)
    {
        int left = taskbar.Left + Math.Max(0, edgeMargin);
        int top = edge == BarEdge.Top ? taskbar.Bottom + gap : taskbar.Top - gap - height;
        return ClampInto(new PxRect(left, top, left + width, top + height), monitor);
    }

    /// <summary>
    /// The slot the bars take: where the user dropped them, kept inside the screen it is on, or
    /// <paramref name="home"/> when there is no saved position or its screen is gone.
    /// </summary>
    public static PxRect Resolve(FloatingState state, PxRect home, IReadOnlyList<PxRect> monitors)
    {
        if (state.Left is not { } left || state.Top is not { } top)
            return home;

        var moved = new PxRect(left, top, left + home.Width, top + home.Height);
        int centerX = left + (home.Width / 2);
        int centerY = top + (home.Height / 2);
        foreach (var monitor in monitors)
        {
            if (centerX >= monitor.Left && centerX < monitor.Right && centerY >= monitor.Top && centerY < monitor.Bottom)
                return ClampInto(moved, monitor);
        }

        return home;
    }

    /// <summary>Moves <paramref name="slot"/> the least needed to lie wholly inside <paramref name="area"/>.</summary>
    public static PxRect ClampInto(PxRect slot, PxRect area)
    {
        if (area.IsEmpty)
            return slot;

        int left = Math.Clamp(slot.Left, area.Left, Math.Max(area.Left, area.Right - slot.Width));
        int top = Math.Clamp(slot.Top, area.Top, Math.Max(area.Top, area.Bottom - slot.Height));
        return new PxRect(left, top, left + slot.Width, top + slot.Height);
    }
}
