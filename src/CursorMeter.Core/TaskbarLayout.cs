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
}
