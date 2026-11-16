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

    public static bool IsCollapsed(PxRect taskbar)
    {
        int thickness = Math.Min(Math.Abs(taskbar.Width), Math.Abs(taskbar.Height));
        return thickness < CollapsedThickness;
    }

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

    public static PxRect Place(PxRect taskbar, PxRect tray, BarEdge edge, int delta, int width, int height)
    {
        if (taskbar.IsEmpty)
            return new PxRect(0, 0, width, height);

        if (tray.IsEmpty)
        {
            tray = edge is BarEdge.Left or BarEdge.Right
                ? new PxRect(taskbar.Left, taskbar.Bottom - Math.Min(160, taskbar.Height), taskbar.Right, taskbar.Bottom)
                : new PxRect(taskbar.Right - Math.Min(160, taskbar.Width), taskbar.Top, taskbar.Right, taskbar.Bottom);
        }

        width = Math.Min(width, Math.Max(80, taskbar.Width - 8));
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
        x = Math.Clamp(x, taskbar.Left + 2, maxX);
        y = Math.Clamp(y, taskbar.Top + 2, maxY);
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
