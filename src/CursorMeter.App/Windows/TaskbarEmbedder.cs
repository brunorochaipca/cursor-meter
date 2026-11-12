using Avalonia;
using Avalonia.Threading;

namespace CursorMeter.App.Windows;

/// <summary>
/// Reparents the widget into the primary taskbar, to the left of the tray.
/// If Explorer will not take the window, it stays docked on top of the taskbar.
/// </summary>
internal sealed class TaskbarEmbedder : IDisposable
{
    private const uint EventObjectCreate = 0x8000;
    private const uint EventObjectLocationChange = 0x800B;

    private readonly WidgetWindow _widget;
    private readonly NativeMethods.WinEventDelegate _onTaskbarEvent;
    private readonly DispatcherTimer _settleTimer;
    private IntPtr _taskbarHook;
    private uint _hookedThread;
    private int _delta;
    private int _settleRemaining;
    private bool _placeQueued;

    public TaskbarEmbedder(WidgetWindow widget)
    {
        _widget = widget;
        _delta = ReadDelta();
        _onTaskbarEvent = OnTaskbarEvent;
        _settleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(160) };
        _settleTimer.Tick += (_, _) => OnSettleTick();
        WatchTaskbar();
    }

    public bool Embedded { get; private set; }

    public void Attach()
    {
        WatchTaskbar();
        Place(allowReparent: true);
    }

    public void Place(bool allowReparent = false)
    {
        if (!_widget.IsAlive)
            return;

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

        var taskbarClient = RelativeTo(taskbarRect, taskbarRect);
        var trayClient = tray == IntPtr.Zero ? default : RelativeTo(taskbarRect, trayRect);
        var edge = TaskbarLayout.DetectEdge(ToPx(taskbarRect), MonitorOf(taskbar, taskbarRect));
        var slot = TaskbarLayout.Place(taskbarClient, trayClient, edge, _delta, PreferredWidth, FitHeight(taskbarClient.Height));

        if (allowReparent || NativeMethods.GetParent(_widget.Handle) != taskbar)
            Reparent(taskbar);
        if (NativeMethods.GetParent(_widget.Handle) != taskbar)
        {
            DockFallback();
            return;
        }

        KeepAboveSiblings();
        _widget.LockedSlot = slot;
        NativeMethods.SetWindowPos(
            _widget.Handle,
            IntPtr.Zero,
            slot.Left,
            slot.Top,
            slot.Width,
            slot.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
        NativeMethods.RedrawWindow(
            _widget.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.RdwInvalidate | NativeMethods.RdwNoErase | NativeMethods.RdwUpdateNow);
    }

    public void DragTo(int screenLeft, int screenTop)
    {
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !NativeMethods.GetWindowRect(taskbar, out var taskbarRect))
            return;

        var tray = FindTray(taskbar);
        var trayRect = tray != IntPtr.Zero && NativeMethods.GetWindowRect(tray, out var found) ? found : default;
        var taskbarClient = RelativeTo(taskbarRect, taskbarRect);
        var trayClient = tray == IntPtr.Zero ? default : RelativeTo(taskbarRect, trayRect);
        var edge = TaskbarLayout.DetectEdge(ToPx(taskbarRect), MonitorOf(taskbar, taskbarRect));
        int height = FitHeight(taskbarClient.Height);
        var natural = TaskbarLayout.Place(taskbarClient, trayClient, edge, 0, PreferredWidth, height);
        int x = screenLeft - taskbarRect.Left;
        int y = screenTop - taskbarRect.Top;
        _delta = edge is BarEdge.Left or BarEdge.Right ? y - natural.Top : x - natural.Left;
        WriteDelta(_delta);
        Place();
    }

    public void Dispose()
    {
        _settleTimer.Stop();
        Unhook();
    }

    private int PreferredWidth => _widget.ToPixels(TaskbarLayout.PreferredWidth);

    private void Reparent(IntPtr taskbar)
    {
        if (NativeMethods.GetParent(_widget.Handle) == taskbar)
        {
            Embedded = _widget.Embedded = true;
            KeepAboveSiblings();
            return;
        }

        int style = NativeMethods.GetWindowLong(_widget.Handle, NativeMethods.GwlStyle);
        style &= ~NativeMethods.WsPopup;
        style &= ~NativeMethods.WsClipSiblings;
        style |= NativeMethods.WsChild | NativeMethods.WsVisible;
        NativeMethods.SetWindowLong(_widget.Handle, NativeMethods.GwlStyle, style);
        NativeMethods.SetParent(_widget.Handle, taskbar);
        Embedded = _widget.Embedded = NativeMethods.GetParent(_widget.Handle) == taskbar;
        if (Embedded)
            _widget.Topmost = false;
    }

    private void OnSettleTick()
    {
        if (!_widget.IsAlive || _widget.IsDragging)
        {
            _settleTimer.Stop();
            return;
        }

        Place();
        if (--_settleRemaining <= 0)
            _settleTimer.Stop();
    }

    private void WatchTaskbar()
    {
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero)
            return;
        var thread = NativeMethods.GetWindowThreadProcessId(taskbar, out var process);
        if (_taskbarHook != IntPtr.Zero && thread == _hookedThread)
            return;

        Unhook();
        _taskbarHook = NativeMethods.SetWinEventHook(
            EventObjectCreate,
            EventObjectLocationChange,
            IntPtr.Zero,
            _onTaskbarEvent,
            process,
            thread,
            NativeMethods.WineventOutOfContext | NativeMethods.WineventSkipOwnProcess);
        _hookedThread = _taskbarHook == IntPtr.Zero ? 0 : thread;
    }

    private void Unhook()
    {
        if (_taskbarHook == IntPtr.Zero)
            return;
        NativeMethods.UnhookWinEvent(_taskbarHook);
        _taskbarHook = IntPtr.Zero;
        _hookedThread = 0;
    }

    private void OnTaskbarEvent(
        IntPtr hook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint eventThread,
        uint eventTime)
    {
        if (!_widget.IsAlive || _widget.IsDragging || hwnd == IntPtr.Zero || hwnd == _widget.Handle)
            return;
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero)
            return;
        if (hwnd != taskbar && NativeMethods.GetAncestor(hwnd, NativeMethods.GaRoot) != taskbar)
            return;

        if (_placeQueued)
            return;

        _placeQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _placeQueued = false;
            Place();
            _settleRemaining = 3;
            _settleTimer.Stop();
            _settleTimer.Start();
        });
    }

    private void KeepAboveSiblings()
    {
        if (!_widget.IsAlive)
            return;
        int style = NativeMethods.GetWindowLong(_widget.Handle, NativeMethods.GwlStyle);
        if ((style & NativeMethods.WsClipSiblings) == 0)
            return;
        NativeMethods.SetWindowLong(_widget.Handle, NativeMethods.GwlStyle, style & ~NativeMethods.WsClipSiblings);
    }

    private void DockFallback()
    {
        Embedded = _widget.Embedded = false;
        _widget.LockedSlot = null;
        if (!_widget.IsAlive)
            return;

        int style = NativeMethods.GetWindowLong(_widget.Handle, NativeMethods.GwlStyle);
        style &= ~NativeMethods.WsChild;
        style |= NativeMethods.WsPopup | NativeMethods.WsVisible;
        NativeMethods.SetWindowLong(_widget.Handle, NativeMethods.GwlStyle, style);
        NativeMethods.SetParent(_widget.Handle, IntPtr.Zero);

        var area = _widget.Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
        int width = PreferredWidth;
        int height = _widget.ToPixels(TaskbarLayout.PreferredHeight);
        _widget.Topmost = true;
        NativeMethods.SetWindowPos(
            _widget.Handle,
            IntPtr.Zero,
            area.Right - width - _widget.ToPixels(12),
            area.Bottom - height - _widget.ToPixels(8),
            width,
            height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpNoZOrder);
    }

    private int FitHeight(int taskbarHeight)
    {
        if (taskbarHeight <= 0)
            return _widget.ToPixels(TaskbarLayout.PreferredHeight);
        return Math.Clamp(taskbarHeight - _widget.ToPixels(4), _widget.ToPixels(30), _widget.ToPixels(48));
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

    private static PxRect RelativeTo(NativeMethods.Rect origin, NativeMethods.Rect screen)
        => new(
            screen.Left - origin.Left,
            screen.Top - origin.Top,
            screen.Right - origin.Left,
            screen.Bottom - origin.Top);

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
