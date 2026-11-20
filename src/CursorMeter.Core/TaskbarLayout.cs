namespace CursorMeter;

public enum BarEdge
{
    Bottom,
    Top,
    Left,
    Right,
}

/// <summary>
/// Places the two-bar widget inside the taskbar, just beside the notification area.
/// Coordinates are in the taskbar's own client space.
/// </summary>
public static class TaskbarLayout
{
    public const int PreferredWidth = 252;
    public const int PreferredHeight = 40;
    public const int Gap = 6;

    /// <summary>Height of the two rings on a taskbar at the left or right of the screen, at 96 DPI.</summary>
    public const int VerticalHeight = 128;

    /// <summary>Narrowest the stacked bars get on a vertical taskbar, at 96 DPI.</summary>
    public const int VerticalMinWidth = 40;

    /// <summary>An auto-hidden taskbar shrinks to a sliver of about two pixels.</summary>
    public const int CollapsedThickness = 16;

    public static int Scale(int dip, int dpi)
    {
        if (dpi <= 0)
            dpi = 96;
        return (int)Math.Round(dip * (dpi / 96.0));
    }

    /// <summary>
    /// Height of the bars for a taskbar of <paramref name="taskbarThickness"/> pixels.
    /// Stays inside the bar at 100% (48px), 125% (60px) and 150% (72px).
    /// </summary>
    public static int FitThickness(int taskbarThickness, int dpi)
    {
        if (taskbarThickness <= 0)
            return Scale(PreferredHeight, dpi);
        return Math.Clamp(taskbarThickness - Scale(4, dpi), Scale(30, dpi), Scale(48, dpi));
    }

    public static bool IsVertical(BarEdge edge) => edge is BarEdge.Left or BarEdge.Right;

    /// <summary>
    /// Size of the bars for this taskbar. Along the top or bottom they are one wide strip as high
    /// as the bar allows. Along the left or right they are stacked, as wide as the bar allows.
    /// </summary>
    public static (int Width, int Height) FitSize(PxRect taskbar, BarEdge edge, int dpi)
    {
        if (!IsVertical(edge))
            return (Scale(PreferredWidth, dpi), FitThickness(taskbar.Height, dpi));

        int width = taskbar.Width <= 0
            ? Scale(PreferredWidth, dpi)
            : Math.Clamp(taskbar.Width - Scale(8, dpi), Scale(VerticalMinWidth, dpi), Scale(PreferredWidth, dpi));
        return (width, Scale(VerticalHeight, dpi));
    }

    public static bool IsCollapsed(PxRect taskbar)
    {
        int thickness = Math.Min(Math.Abs(taskbar.Width), Math.Abs(taskbar.Height));
        return thickness < CollapsedThickness;
    }

    /// <summary>
    /// How many pixels of the taskbar's thickness lie on <paramref name="monitor"/>. A taskbar that an
    /// auto-hide slid off the screen keeps its size but leaves a sliver or nothing in view.
    /// </summary>
    public static int VisibleThickness(PxRect taskbar, PxRect monitor)
    {
        int width = Math.Min(taskbar.Right, monitor.Right) - Math.Max(taskbar.Left, monitor.Left);
        int height = Math.Min(taskbar.Bottom, monitor.Bottom) - Math.Max(taskbar.Top, monitor.Top);
        return Math.Max(0, Math.Min(width, height));
    }

    /// <summary>True when too little of the taskbar is on screen for the bars to sit in it.</summary>
    public static bool IsOffScreen(PxRect taskbar, PxRect monitor) => VisibleThickness(taskbar, monitor) < CollapsedThickness;

    /// <summary>
    /// The line along the screen edge where an auto-hidden taskbar rests. The floating bars are placed
    /// against it, so they hug the edge whatever way the taskbar was hidden.
    /// </summary>
    public static PxRect EdgeLine(PxRect monitor, BarEdge edge) => edge switch
    {
        BarEdge.Top => new PxRect(monitor.Left, monitor.Top, monitor.Right, monitor.Top),
        BarEdge.Left => new PxRect(monitor.Left, monitor.Top, monitor.Left, monitor.Bottom),
        BarEdge.Right => new PxRect(monitor.Right, monitor.Top, monitor.Right, monitor.Bottom),
        _ => new PxRect(monitor.Left, monitor.Bottom, monitor.Right, monitor.Bottom),
    };

    public static BarEdge DetectEdge(PxRect taskbar, PxRect screen)
    {
        bool horizontal = taskbar.Width >= taskbar.Height;
        if (horizontal)
        {
            int mid = screen.Top + (screen.Height / 3);
            return taskbar.Top <= mid ? BarEdge.Top : BarEdge.Bottom;
        }

        int side = screen.Left + (screen.Width / 3);
        return taskbar.Left <= side ? BarEdge.Left : BarEdge.Right;
    }

    public static PxRect Place(PxRect taskbar, PxRect tray, BarEdge edge, int delta)
    {
        return Place(taskbar, tray, edge, delta, PreferredWidth, PreferredHeight);
    }

    /// <summary>No limit: the bars may go anywhere along the taskbar.</summary>
    public const int NoLimit = int.MinValue;

    /// <summary>
    /// How far along the taskbar the bars must stay clear of what sits at its start: the Start button,
    /// the search box, Task View. Windows 10 draws those above a child of the taskbar, so bars dragged
    /// over them vanish. <paramref name="elements"/> are the rectangles of the taskbar's children, in
    /// the same space as the taskbar. Returns the right edge (bottom edge on a vertical taskbar) of the
    /// last one, or <see cref="NoLimit"/>. A child that spans most of the taskbar is the container of
    /// the apps or of a XAML surface, not a button, and is not counted. Neither is anything past the
    /// middle of the taskbar or beyond the notification area.
    /// </summary>
    public static int KeepClearAlong(PxRect taskbar, PxRect tray, BarEdge edge, IEnumerable<PxRect> elements)
    {
        if (taskbar.IsEmpty)
            return NoLimit;

        bool vertical = IsVertical(edge);
        int length = vertical ? taskbar.Height : taskbar.Width;
        int start = vertical ? taskbar.Top : taskbar.Left;
        int trayStart = tray.IsEmpty ? int.MaxValue : vertical ? tray.Top : tray.Left;

        int limit = NoLimit;
        foreach (var element in elements)
        {
            if (element.IsEmpty)
                continue;
            int elementStart = vertical ? element.Top : element.Left;
            int elementEnd = vertical ? element.Bottom : element.Right;
            int elementLength = elementEnd - elementStart;
            if (elementLength >= length * 6 / 10
                || elementStart >= start + (length / 2)
                || elementEnd > trayStart)
                continue;
            limit = Math.Max(limit, elementEnd);
        }

        return limit;
    }

    public static PxRect Place(PxRect taskbar, PxRect tray, BarEdge edge, int delta, int width, int height)
        => Place(taskbar, tray, edge, delta, width, height, NoLimit);

    /// <summary>
    /// Like the overload without a limit. <paramref name="keepClearAlong"/> is the position along the
    /// taskbar (x, or y on a vertical taskbar) the bars may not go before, see <see cref="KeepClearAlong"/>.
    /// </summary>
    public static PxRect Place(PxRect taskbar, PxRect tray, BarEdge edge, int delta, int width, int height, int keepClearAlong)
    {
        if (taskbar.IsEmpty)
            return new PxRect(0, 0, width, height);

        if (tray.IsEmpty)
        {
            tray = edge is BarEdge.Left or BarEdge.Right
                ? new PxRect(taskbar.Left, taskbar.Bottom - Math.Min(160, taskbar.Height), taskbar.Right, taskbar.Bottom)
                : new PxRect(taskbar.Right - Math.Min(160, taskbar.Width), taskbar.Top, taskbar.Right, taskbar.Bottom);
        }

        int minWidth = IsVertical(edge) ? VerticalMinWidth : 80;
        width = Math.Min(width, Math.Max(minWidth, taskbar.Width - 8));
        height = Math.Min(Math.Max(28, height), Math.Max(28, taskbar.Height - 4));

        int x;
        int y;
        if (edge is BarEdge.Left or BarEdge.Right)
        {
            x = taskbar.Left + Math.Max(0, (taskbar.Width - width) / 2);
            y = tray.Top - Gap - height + delta;
        }
        else
        {
            y = taskbar.Top + Math.Max(0, (taskbar.Height - height) / 2);
            x = tray.Left - Gap - width + delta;
        }

        int maxX = Math.Max(taskbar.Left + 2, taskbar.Right - width - 2);
        int maxY = Math.Max(taskbar.Top + 2, taskbar.Bottom - height - 2);
        bool vertical = edge is BarEdge.Left or BarEdge.Right;
        int minX = vertical || keepClearAlong == NoLimit ? taskbar.Left + 2 : Math.Min(Math.Max(taskbar.Left + 2, keepClearAlong), maxX);
        int minY = !vertical || keepClearAlong == NoLimit ? taskbar.Top + 2 : Math.Min(Math.Max(taskbar.Top + 2, keepClearAlong), maxY);
        x = Math.Clamp(x, minX, maxX);
        y = Math.Clamp(y, minY, maxY);
        return new PxRect(x, y, x + width, y + height);
    }

    /// <summary>
    /// Places the same slot just outside the taskbar. Opening Start moves the taskbar
    /// into a higher window band, which covers a normal topmost window sitting on the bar.
    /// </summary>
    public static PxRect Beside(PxRect taskbar, PxRect slot, BarEdge edge, PxRect monitor)
    {
        int x = slot.Left;
        int y = slot.Top;
        switch (edge)
        {
            case BarEdge.Bottom:
                y = taskbar.Top - slot.Height;
                break;
            case BarEdge.Top:
                y = taskbar.Bottom;
                break;
            case BarEdge.Left:
                x = taskbar.Right;
                break;
            case BarEdge.Right:
                x = taskbar.Left - slot.Width;
                break;
        }

        if (!monitor.IsEmpty)
        {
            x = Math.Clamp(x, monitor.Left, Math.Max(monitor.Left, monitor.Right - slot.Width));
            y = Math.Clamp(y, monitor.Top, Math.Max(monitor.Top, monitor.Bottom - slot.Height));
        }

        return new PxRect(x, y, x + slot.Width, y + slot.Height);
    }
}
