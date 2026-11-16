using System.Diagnostics;
using Avalonia;
using Avalonia.Threading;

namespace CursorMeter.App.Windows;

/// <summary>
/// Keeps the bars on the primary taskbar, just left of the tray.
/// Windows 10 accepts them as a child of the taskbar. Windows 11 does not present that child:
/// at 100% the XAML surface covers it and nothing is drawn, while at 125% DPI hosting
/// happens to scale the same window into view. On Windows 11 the bars are a topmost
/// window in screen pixels, sized from the taskbar's own DPI. Opening Start lifts the
/// taskbar into a higher band that covers that window, so the bars step just outside
/// the bar until the menu closes.
/// </summary>
internal sealed class TaskbarEmbedder : IDisposable
{
    private const uint EventObjectCreate = 0x8000;
    private const uint EventObjectLocationChange = 0x800B;
    private const string XamlBridgeClass = "Windows.UI.Composition.DesktopWindowContentBridge";

    private readonly WidgetWindow _widget;
    private readonly NativeMethods.WinEventDelegate _onTaskbarEvent;
    private readonly DispatcherTimer _settleTimer;
    private IntPtr _taskbarHook;
    private uint _hookedThread;
    private int _delta;
    private int _settleRemaining;
    private bool _placeQueued;
    private static uint _menuPid;
    private static bool _menuVisible;
    private static readonly NativeMethods.EnumWindowsProc FindMenuWindow = OnFindMenuWindow;

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

        var taskbarPx = ToPx(taskbarRect);
        bool overlay = DrawOverTaskbar(taskbar);
        if (TaskbarLayout.IsCollapsed(taskbarPx))
        {
            // A child is clipped by the shrunk taskbar. An overlay would sit on the desktop edge.
            if (overlay)
                HoldHidden();
            return;
        }

        var monitor = MonitorOf(taskbar, taskbarRect);
        var tray = FindTray(taskbar);
        var trayPx = tray != IntPtr.Zero && NativeMethods.GetWindowRect(tray, out var found) ? ToPx(found) : default;
        var edge = TaskbarLayout.DetectEdge(taskbarPx, monitor);
        int dpi = DpiOf(taskbar);
        int width = TaskbarLayout.Scale(TaskbarLayout.PreferredWidth, dpi);
        int height = TaskbarLayout.FitThickness(taskbarPx.Height, dpi);
        var screenSlot = TaskbarLayout.Place(taskbarPx, trayPx, edge, _delta, width, height);
        if (screenSlot.IsEmpty)
        {
            if (overlay)
                HoldHidden();
            return;
        }

        if (overlay)
        {
            bool lift = MenuCoversTaskbar();
            var slot = lift ? TaskbarLayout.Beside(taskbarPx, screenSlot, edge, monitor) : screenSlot;
            DockOver(slot, lift);
        }
        else
        {
            Embed(taskbar, taskbarPx, screenSlot, allowReparent);
        }
    }

    public void DragTo(int screenLeft, int screenTop)
    {
        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero || !NativeMethods.GetWindowRect(taskbar, out var taskbarRect))
            return;

        var taskbarPx = ToPx(taskbarRect);
        var tray = FindTray(taskbar);
        var trayPx = tray != IntPtr.Zero && NativeMethods.GetWindowRect(tray, out var found) ? ToPx(found) : default;
        var edge = TaskbarLayout.DetectEdge(taskbarPx, MonitorOf(taskbar, taskbarRect));
        int dpi = DpiOf(taskbar);
        int height = TaskbarLayout.FitThickness(taskbarPx.Height, dpi);
        var natural = TaskbarLayout.Place(taskbarPx, trayPx, edge, 0, TaskbarLayout.Scale(TaskbarLayout.PreferredWidth, dpi), height);
        _delta = edge is BarEdge.Left or BarEdge.Right
            ? screenTop - natural.Top
            : screenLeft - natural.Left;
        WriteDelta(_delta);
        Place();
    }

    public void Dispose()
    {
        _settleTimer.Stop();
        Unhook();
    }

    private void Embed(IntPtr taskbar, PxRect taskbarPx, PxRect screenSlot, bool allowReparent)
    {
        var local = new PxRect(
            screenSlot.Left - taskbarPx.Left,
            screenSlot.Top - taskbarPx.Top,
            screenSlot.Right - taskbarPx.Left,
            screenSlot.Bottom - taskbarPx.Top);

        _widget.PinTopmost = false;
        _widget.HeldHidden = false;
        if (allowReparent || NativeMethods.GetParent(_widget.Handle) != taskbar)
            Reparent(taskbar);
        if (NativeMethods.GetParent(_widget.Handle) != taskbar)
        {
            DockOver(screenSlot, lifted: false);
            return;
        }

        KeepAboveSiblings();
        ShowSlot(local, topmost: false);
    }

    private void DockOver(PxRect screenSlot, bool lifted)
    {
        if (_widget.PinTopmost
            && !_widget.HeldHidden
            && _widget.LockedSlot is { } locked
            && locked.Left == screenSlot.Left
            && locked.Top == screenSlot.Top
            && locked.Width == screenSlot.Width
            && locked.Height == screenSlot.Height)
        {
            ArmLiftWatch(lifted);
            return;
        }

        Embedded = _widget.Embedded = false;
        _widget.PinTopmost = true;
        _widget.HeldHidden = false;
        MakePopup();
        _widget.Topmost = true;
        ShowSlot(screenSlot, topmost: true);
        ArmLiftWatch(lifted);
    }

    private void ArmLiftWatch(bool lifted)
    {
        if (!lifted)
            return;
        _settleRemaining = 8;
        _settleTimer.Stop();
        _settleTimer.Start();
    }

    private static bool MenuCoversTaskbar()
    {
        // StartMenuExperienceHost stays running after the menu closes, but its window is hidden.
        try
        {
            var processes = Process.GetProcessesByName("StartMenuExperienceHost");
            try
            {
                foreach (var process in processes)
                {
                    if (HasVisibleMenu((uint)process.Id))
                        return true;
                }
            }
            finally
            {
                foreach (var process in processes)
                    process.Dispose();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
        }

        return false;
    }

    private static bool HasVisibleMenu(uint processId)
    {
        _menuPid = processId;
        _menuVisible = false;
        NativeMethods.EnumWindows(FindMenuWindow, IntPtr.Zero);
        return _menuVisible;
    }

    private static bool OnFindMenuWindow(IntPtr hwnd, IntPtr lparam)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
        if (pid != _menuPid || !NativeMethods.IsWindowVisible(hwnd))
            return true;
        // The Start process keeps a cloaked window after the menu closes.
        if (NativeMethods.DwmGetWindowAttribute(hwnd, NativeMethods.DwmwaCloaked, out var cloaked, sizeof(int)) == 0
            && cloaked != 0)
            return true;
        if (!NativeMethods.GetWindowRect(hwnd, out var rect))
            return true;
        if (rect.Right - rect.Left < 80 || rect.Bottom - rect.Top < 80)
            return true;
        _menuVisible = true;
        return false;
    }

    private void ShowSlot(PxRect slot, bool topmost)
    {
        _widget.LockedSlot = slot;
        NativeMethods.SetWindowPos(
            _widget.Handle,
            topmost ? NativeMethods.HwndTopMost : IntPtr.Zero,
            slot.Left,
            slot.Top,
            slot.Width,
            slot.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow | NativeMethods.SwpFrameChanged);
        NativeMethods.RedrawWindow(
            _widget.Handle,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeMethods.RdwInvalidate | NativeMethods.RdwNoErase | NativeMethods.RdwUpdateNow);
    }

    private void MakePopup()
    {
        var hwnd = _widget.Handle;
        int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlStyle);
        if ((style & NativeMethods.WsChild) != 0 || NativeMethods.GetParent(hwnd) != IntPtr.Zero)
        {
            style &= ~NativeMethods.WsChild;
            style |= NativeMethods.WsPopup | NativeMethods.WsVisible;
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GwlStyle, style);
            NativeMethods.SetParent(hwnd, IntPtr.Zero);
        }

        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);
        int wanted = exStyle | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate | NativeMethods.WsExTopMost;
        if (wanted != exStyle)
            NativeMethods.SetWindowLong(hwnd, NativeMethods.GwlExStyle, wanted);
    }

    private void Reparent(IntPtr taskbar)
    {
        var hwnd = _widget.Handle;
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlExStyle);
        exStyle &= ~NativeMethods.WsExTopMost;
        exStyle |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GwlExStyle, exStyle);

        if (NativeMethods.GetParent(hwnd) == taskbar)
        {
            Embedded = _widget.Embedded = true;
            KeepAboveSiblings();
            return;
        }

        int style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GwlStyle);
        style &= ~NativeMethods.WsPopup;
        style &= ~NativeMethods.WsClipSiblings;
        style |= NativeMethods.WsChild | NativeMethods.WsVisible;
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GwlStyle, style);
        NativeMethods.SetParent(hwnd, taskbar);
        Embedded = _widget.Embedded = NativeMethods.GetParent(hwnd) == taskbar;
        if (Embedded)
            _widget.Topmost = false;
    }

    private static bool DrawOverTaskbar(IntPtr taskbar)
    {
        // 22000 is Windows 11. The XAML taskbar presents its own surface and not a foreign child.
        if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            return true;
        if (NativeMethods.FindWindowEx(taskbar, IntPtr.Zero, XamlBridgeClass, null) != IntPtr.Zero)
            return true;

        IntPtr child = IntPtr.Zero;
        while ((child = NativeMethods.FindWindowEx(taskbar, child, null, null)) != IntPtr.Zero)
        {
            if (NativeMethods.FindWindowEx(child, IntPtr.Zero, XamlBridgeClass, null) != IntPtr.Zero)
                return true;
        }

        return false;
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

    private void HoldHidden()
    {
        Embedded = _widget.Embedded = false;
        _widget.PinTopmost = false;
        _widget.HeldHidden = true;
        _widget.LockedSlot = null;
        _widget.Topmost = false;
        NativeMethods.ShowWindow(_widget.Handle, NativeMethods.SwHide);
    }

    private void DockFallback()
    {
        Embedded = _widget.Embedded = false;
        _widget.PinTopmost = true;
        _widget.HeldHidden = false;
        if (!_widget.IsAlive)
            return;

        MakePopup();
        var area = _widget.Screens.Primary?.WorkingArea ?? new PixelRect(0, 0, 1280, 720);
        int dpi = DpiOf(_widget.Handle);
        int width = TaskbarLayout.Scale(TaskbarLayout.PreferredWidth, dpi);
        int height = TaskbarLayout.Scale(TaskbarLayout.PreferredHeight, dpi);
        int left = area.Right - width - TaskbarLayout.Scale(12, dpi);
        int top = area.Bottom - height - TaskbarLayout.Scale(8, dpi);
        _widget.Topmost = true;
        ShowSlot(new PxRect(left, top, left + width, top + height), topmost: true);
    }

    private int DpiOf(IntPtr window)
    {
        try
        {
            uint dpi = window == IntPtr.Zero ? 0 : NativeMethods.GetDpiForWindow(window);
            if (dpi >= 96)
                return (int)dpi;
        }
        catch (EntryPointNotFoundException)
        {
            // GetDpiForWindow arrived in Windows 10 1607. Fall back to the window scale.
        }

        var scaled = (int)Math.Round(96 * _widget.RenderScaling);
        return scaled >= 96 ? scaled : 96;
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
