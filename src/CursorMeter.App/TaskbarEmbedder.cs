namespace CursorMeter.App;

/// <summary>
/// Reparents the widget into the primary taskbar, to the left of the tray.
/// If Explorer will not take the window, it stays docked on top of the taskbar.
/// </summary>
internal sealed class TaskbarEmbedder
{
    private readonly WidgetForm _widget;
    private int _delta;

    public TaskbarEmbedder(WidgetForm widget)
    {
        _widget = widget;
        _delta = ReadDelta();
    }

    public bool Embedded { get; private set; }

    public void Attach() => Place(allowReparent: true);

    public void Place(bool allowReparent = false)
    {
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !NativeMethods.GetWindowRect(taskbar, out var taskbarRect))
        {
            DockFallback();
            return;
        }

        var tray = FindTray(taskbar);
        var trayRect = tray != IntPtr.Zero && NativeMethods.GetWindowRect(tray, out var found)
            ? found
            : default;

        var taskbarClient = ToClient(taskbar, taskbarRect);
        var trayClient = tray == IntPtr.Zero ? default : ToClient(taskbar, trayRect);
        var edge = TaskbarLayout.DetectEdge(ToPx(taskbarRect), MonitorOf(taskbar, taskbarRect));
        var slot = TaskbarLayout.Place(taskbarClient, trayClient, edge, _delta, _widget.Width, FitHeight(taskbarClient.Height));

        if (allowReparent || Embedded)
            Reparent(taskbar);

        NativeMethods.SetWindowPos(
            _widget.Handle,
            IntPtr.Zero,
            slot.Left,
            slot.Top,
            slot.Width,
            slot.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        _widget.Bounds = new Rectangle(slot.Left, slot.Top, slot.Width, slot.Height);
    }

    public void DragBy(int x, int y)
    {
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !NativeMethods.GetWindowRect(taskbar, out var taskbarRect))
            return;

        var tray = FindTray(taskbar);
        var trayRect = tray != IntPtr.Zero && NativeMethods.GetWindowRect(tray, out var found) ? found : default;
        var taskbarClient = ToClient(taskbar, taskbarRect);
        var trayClient = tray == IntPtr.Zero ? default : ToClient(taskbar, trayRect);
        var edge = TaskbarLayout.DetectEdge(ToPx(taskbarRect), MonitorOf(taskbar, taskbarRect));
        var natural = TaskbarLayout.Place(taskbarClient, trayClient, edge, 0, _widget.Width, _widget.Height);
        _delta = edge is BarEdge.Left or BarEdge.Right ? y - natural.Top : x - natural.Left;
        WriteDelta(_delta);
        Place();
    }

    private void Reparent(IntPtr taskbar)
    {
        if (NativeMethods.GetParent(_widget.Handle) == taskbar)
        {
            Embedded = true;
            return;
        }

        NativeMethods.SetParent(_widget.Handle, taskbar);
        if (NativeMethods.GetParent(_widget.Handle) != taskbar)
            return;

        int style = NativeMethods.GetWindowLong(_widget.Handle, NativeMethods.GwlStyle);
        style |= NativeMethods.WsChild | NativeMethods.WsVisible | NativeMethods.WsClipSiblings;
        NativeMethods.SetWindowLong(_widget.Handle, NativeMethods.GwlStyle, style);
        Embedded = true;
        _widget.TopMost = false;
    }

    private void DockFallback()
    {
        Embedded = false;
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        _widget.TopMost = true;
        _widget.Bounds = new Rectangle(area.Right - _widget.Width - 12, area.Bottom - _widget.Height - 8, _widget.Width, _widget.Height);
    }

    private static int FitHeight(int taskbarHeight)
    {
        if (taskbarHeight <= 0)
            return TaskbarLayout.PreferredHeight;
        return Math.Clamp(taskbarHeight - 4, 30, 48);
    }

    private static IntPtr FindTray(IntPtr taskbar)
    {
        var direct = NativeMethods.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
        if (direct != IntPtr.Zero)
            return direct;

        IntPtr child = IntPtr.Zero;
        while ((child = NativeMethods.FindWindowEx(taskbar, child, null, null)) != IntPtr.Zero)
        {
            var nested = NativeMethods.FindWindowEx(child, IntPtr.Zero, "TrayNotifyWnd", null);
            if (nested != IntPtr.Zero)
                return nested;
        }

        return IntPtr.Zero;
    }

    private static PxRect ToClient(IntPtr parent, NativeMethods.Rect screen)
    {
        var topLeft = new NativeMethods.Point { X = screen.Left, Y = screen.Top };
        var bottomRight = new NativeMethods.Point { X = screen.Right, Y = screen.Bottom };
        NativeMethods.ScreenToClient(parent, ref topLeft);
        NativeMethods.ScreenToClient(parent, ref bottomRight);
        return new PxRect(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
    }

    private static PxRect ToPx(NativeMethods.Rect rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    private static PxRect MonitorOf(IntPtr taskbar, NativeMethods.Rect taskbarRect)
    {
        var monitor = NativeMethods.MonitorFromWindow(taskbar, NativeMethods.MonitorDefaultToNearest);
        var info = NativeMethods.MonitorInfo.Create();
        if (monitor != IntPtr.Zero && NativeMethods.GetMonitorInfo(monitor, ref info))
            return ToPx(info.Monitor);
        return new PxRect(0, 0, Math.Max(taskbarRect.Right, 1), Math.Max(taskbarRect.Bottom, 1));
    }

    private static string OffsetPath
    {
        get
        {
            var directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CursorMeter");
            Directory.CreateDirectory(directory);
            return Path.Combine(directory, "offset.txt");
        }
    }

    private static int ReadDelta()
    {
        try
        {
            var path = OffsetPath;
            return File.Exists(path) && int.TryParse(File.ReadAllText(path), out var delta) ? delta : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    private static void WriteDelta(int delta)
    {
        try
        {
            File.WriteAllText(OffsetPath, delta.ToString());
        }
        catch (IOException)
        {
        }
    }
}
