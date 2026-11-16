using System.Diagnostics;
using System.Runtime.InteropServices;
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
/// the bar until the menu closes. A taskbar button click, or a taskbar replacement such as
/// Start11, can also raise the taskbar above the bars without moving it, so the order is
/// checked on every placement and on a short timer. When Start11 cuts the taskbar into
/// islands, the bars become a fixed island of their own on the left, where nothing covers them.
/// </summary>
internal sealed class TaskbarEmbedder : IDisposable
{
    private const uint EventObjectCreate = 0x8000;
    private const uint EventObjectLocationChange = 0x800B;
    private const string XamlBridgeClass = "Windows.UI.Composition.DesktopWindowContentBridge";

    private readonly WidgetWindow _widget;
    private readonly NativeMethods.WinEventDelegate _onTaskbarEvent;
    private readonly DispatcherTimer _settleTimer;
    private readonly DispatcherTimer _orderTimer;
    private IntPtr _taskbarHook;
    private uint _hookedThread;
    private int _delta;
    private int _settleRemaining;
    private bool _placeQueued;
    private bool _lifted;
    private bool _onIsland;
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
        // A click on a taskbar button, or a taskbar replacement such as Start11, raises the
        // taskbar above the bars without moving it, so no taskbar event reports it.
        _orderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        _orderTimer.Tick += (_, _) => OnOrderTick();
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
            Mode("taskbar collapsed, bars hidden");
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
        var island = overlay ? FindIsland(taskbar, taskbarPx, edge, dpi) : null;
        _onIsland = island is not null;
        _widget.ShowIsland(island?.Look);
        if (island is { } onIsland)
        {
            var look = onIsland.Look;
            Mode($"island, slot {Describe(onIsland.Slot)}, radius {look.Radius:0.#}, outline {look.OutlineWidth:0.##}px, dpi {dpi}");
            DockOver(onIsland.Slot, lifted: false);
            return;
        }

        int height = TaskbarLayout.FitThickness(taskbarPx.Height, dpi);
        var screenSlot = TaskbarLayout.Place(taskbarPx, trayPx, edge, _delta, width, height);
        if (screenSlot.IsEmpty)
        {
            Mode("no room on the taskbar, bars hidden");
            if (overlay)
                HoldHidden();
            return;
        }

        if (overlay)
        {
            bool lift = TaskbarInOtherBand(taskbar) || MenuCoversTaskbar();
            var slot = lift ? TaskbarLayout.Beside(taskbarPx, screenSlot, edge, monitor) : screenSlot;
            Mode($"{(lift ? "overlay beside the taskbar" : "overlay on the taskbar")}, no island, slot {Describe(slot)}, dpi {dpi}");
            DockOver(slot, lift);
        }
        else
        {
            Mode($"embedded in the taskbar, slot {Describe(screenSlot)}, dpi {dpi}");
            Embed(taskbar, taskbarPx, screenSlot, allowReparent);
        }
    }

    public void DragTo(int screenLeft, int screenTop)
    {
        if (_onIsland)
            return;

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
        _orderTimer.Stop();
        Unhook();
    }

    private void Embed(IntPtr taskbar, PxRect taskbarPx, PxRect screenSlot, bool allowReparent)
    {
        var local = new PxRect(
            screenSlot.Left - taskbarPx.Left,
            screenSlot.Top - taskbarPx.Top,
            screenSlot.Right - taskbarPx.Left,
            screenSlot.Bottom - taskbarPx.Top);

        _orderTimer.Stop();
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
            // Same slot, but the taskbar may have been raised above the bars since.
            _lifted = lifted;
            RaiseAboveTaskbar(NativeMethods.FindWindow("Shell_TrayWnd", null));
            StartOrderWatch();
            ArmLiftWatch(lifted);
            return;
        }

        Embedded = _widget.Embedded = false;
        _widget.PinTopmost = true;
        _widget.HeldHidden = false;
        _lifted = lifted;
        MakePopup();
        _widget.Topmost = true;
        ShowSlot(screenSlot, topmost: true);
        StartOrderWatch();
        ArmLiftWatch(lifted);
    }

    private void StartOrderWatch()
    {
        if (!_orderTimer.IsEnabled)
            _orderTimer.Start();
    }

    private void OnOrderTick()
    {
        // While lifted the bars sit beside the taskbar, so the order does not matter. The lift
        // watch keeps placing them until the taskbar is back in the bars' band.
        if (!_widget.IsAlive || _widget.IsDragging || !_widget.PinTopmost || _widget.HeldHidden || _lifted)
            return;

        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero)
            return;

        // Task View moves the taskbar into a band above the bars and, with Start11, leaves it
        // there after it closes. No topmost window can sit over a taskbar in a higher band.
        // An island sits outside the taskbar's region, so the band does not hide it.
        if (!_onIsland && TaskbarInOtherBand(taskbar))
        {
            Place();
            return;
        }

        RaiseAboveTaskbar(taskbar);
    }

    /// <summary>
    /// True when the taskbar is in another window band than the bars and cannot be reached
    /// from them in the z-order. A normal topmost window cannot be placed above it.
    /// </summary>
    private bool TaskbarInOtherBand(IntPtr taskbar)
    {
        try
        {
            return NativeMethods.GetWindowBand(taskbar, out var taskbarBand)
                && NativeMethods.GetWindowBand(_widget.Handle, out var widgetBand)
                && taskbarBand != widgetBand
                && !SameBand(_widget.Handle, taskbar);
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static bool SameBand(IntPtr window, IntPtr other)
    {
        // GetWindow only walks the siblings of one band. The taskbar shows up in the chain
        // of the bars when it shares their band, and never when it is in a higher one.
        return Reaches(window, other, NativeMethods.GwHwndPrev) || Reaches(window, other, NativeMethods.GwHwndNext);
    }

    private static bool Reaches(IntPtr window, IntPtr other, uint direction)
    {
        var current = window;
        for (int i = 0; i < 4096; i++)
        {
            current = NativeMethods.GetWindow(current, direction);
            if (current == IntPtr.Zero)
                return false;
            if (current == other)
                return true;
        }

        return false;
    }

    private void RaiseAboveTaskbar(IntPtr taskbar)
    {
        if (taskbar == IntPtr.Zero || !Reaches(_widget.Handle, taskbar, NativeMethods.GwHwndPrev))
            return;

        NativeMethods.SetWindowPos(
            _widget.Handle,
            NativeMethods.HwndTopMost,
            0,
            0,
            0,
            0,
            NativeMethods.SwpNoMove | NativeMethods.SwpNoSize | NativeMethods.SwpNoActivate);
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
        _orderTimer.Stop();
        Embedded = _widget.Embedded = false;
        _widget.PinTopmost = false;
        _widget.HeldHidden = true;
        _widget.LockedSlot = null;
        _widget.Topmost = false;
        NativeMethods.ShowWindow(_widget.Handle, NativeMethods.SwHide);
    }

    private string? _lastMode;

    /// <summary>Logs how the bars are placed, only when that changes.</summary>
    private void Mode(string mode)
    {
        if (mode == _lastMode)
            return;
        _lastMode = mode;
        AppLog.Info("Placement: " + mode);
    }

    private static string Describe(PxRect rect) => $"{rect.Left},{rect.Top} {rect.Width}x{rect.Height}";

    private void DockFallback()
    {
        Mode("no taskbar found, floating in the screen corner");
        _onIsland = false;
        _widget.ShowIsland(null);
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

    private static (PxRect Slot, IslandLook Look)? FindIsland(IntPtr taskbar, PxRect taskbarPx, BarEdge edge, int dpi)
    {
        if (edge is not (BarEdge.Bottom or BarEdge.Top))
            return null;

        var islands = TaskbarIslands.FromRegion(RegionRects(taskbar));
        var slot = TaskbarIslands.LeftSlot(
            taskbarPx,
            islands,
            TaskbarLayout.Scale(TaskbarLayout.PreferredWidth, dpi),
            TaskbarLayout.Scale(160, dpi));
        if (slot.IsEmpty)
            return null;

        var look = Start11Theme.Look(TaskbarIslands.CornerRadius(islands[0].Corner)).At(dpi);
        int margin = look.Margin;
        return (new PxRect(slot.Left - margin, slot.Top - margin, slot.Right + margin, slot.Bottom + margin), look);
    }

    /// <summary>The rectangles of a window region, relative to the window. Empty when it has none.</summary>
    private static List<PxRect> RegionRects(IntPtr window)
    {
        var rects = new List<PxRect>();
        var region = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (region == IntPtr.Zero)
            return rects;
        try
        {
            if (NativeMethods.GetWindowRgn(window, region) < NativeMethods.SimpleRegion)
                return rects;
            uint size = NativeMethods.GetRegionData(region, 0, IntPtr.Zero);
            if (size < 32)
                return rects;

            // RGNDATAHEADER is 32 bytes, with the rectangle count at offset 8; RECTs follow.
            var data = Marshal.AllocHGlobal((int)size);
            try
            {
                if (NativeMethods.GetRegionData(region, size, data) == 0)
                    return rects;
                int count = Marshal.ReadInt32(data, 8);
                for (int i = 0; i < count && 32 + ((i + 1) * 16) <= size; i++)
                {
                    int offset = 32 + (i * 16);
                    rects.Add(new PxRect(
                        Marshal.ReadInt32(data, offset),
                        Marshal.ReadInt32(data, offset + 4),
                        Marshal.ReadInt32(data, offset + 8),
                        Marshal.ReadInt32(data, offset + 12)));
                }
            }
            finally
            {
                Marshal.FreeHGlobal(data);
            }
        }
        finally
        {
            NativeMethods.DeleteObject(region);
        }

        return rects;
    }

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
