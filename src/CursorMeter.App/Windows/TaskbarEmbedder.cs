using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Threading;

namespace CursorMeter.App.Windows;

/// <summary>
/// Keeps the bars on the primary taskbar, just left of the tray.
/// Windows 10 accepts them as a child of the taskbar. Windows 11 does not present that child:
/// at 100% the XAML surface covers it and nothing is drawn, while at 125% DPI hosting
/// happens to scale the same window into view. On Windows 11 the bars are a topmost
/// window in screen pixels, sized from the taskbar's own DPI. Opening Start moves the
/// taskbar into a higher band that covers that window. The bars stay in their slot: that
/// rectangle is cut out of the taskbar until the menu closes. A taskbar that already has
/// a region is left unchanged, and the bars step just outside instead. A taskbar button
/// click, or a taskbar replacement such as Start11, can also raise the taskbar above the
/// bars without moving it, so the order is checked on every placement and on a short timer.
/// When Start11 cuts the taskbar into islands, the bars become a fixed island of their own
/// on the left, where nothing covers them. When the apps island has grown over that room,
/// the bars float above the taskbar instead, in the same look but faded, and can be dragged
/// anywhere on screen or hidden.
/// </summary>
internal sealed class TaskbarEmbedder : IDisposable
{
    private const uint EventObjectCreate = 0x8000;
    private const uint EventObjectLocationChange = 0x800B;
    private const string XamlBridgeClass = "Windows.UI.Composition.DesktopWindowContentBridge";

    /// <summary>Extra room, at 96 DPI, that floating bars ask for before going back on the island.</summary>
    private const int FloatHysteresis = 16;

    /// <summary>True when this process has UIAccess, so its topmost windows sit above the raised taskbar.</summary>
    private static readonly bool UiAccessActive = OperatingSystem.IsWindows() && UiAccess.IsActive;

    private readonly WidgetWindow _widget;
    private readonly NativeMethods.WinEventDelegate _onTaskbarEvent;
    private readonly DispatcherTimer _settleTimer;
    private readonly DispatcherTimer _orderTimer;
    private readonly DispatcherTimer _fullscreenTimer;
    private IntPtr _taskbarHook;
    private uint _hookedThread;
    private int _delta;
    private int _settleRemaining;
    private bool _placeQueued;
    private bool _lifted;
    private bool _onIsland;
    private bool _floating;
    private bool _holeOpen;
    private bool _regionOwnedElsewhere;
    private bool _fullscreen;
    private PxRect _holeSlot;
    private readonly RegionCutGuard _cutGuard = new();

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
        // A full-screen window covers the taskbar and, being topmost, the bars as well. No window
        // event reports it, so Windows is asked on a short timer.
        _fullscreenTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _fullscreenTimer.Tick += (_, _) => OnFullscreenTick();
        _fullscreenTimer.Start();
        FloatingSettings.Changed += OnFloatingSettingsChanged;
        SettingsStore.Changed += OnSettingsChanged;
        WatchTaskbar();
    }

    private void OnFloatingSettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(() => Place());

    private void OnSettingsChanged(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(OnFullscreenTick);

    /// <summary>
    /// True while a full-screen application is in front and the user wants the bars out of its way.
    /// Windows holds back notifications in the same states, so it is the call that tells a game or a
    /// full-screen video from a window that is only maximised.
    /// </summary>
    private static bool FullscreenAppActive() =>
        SettingsStore.Current.HideInFullscreen
        && NativeMethods.SHQueryUserNotificationState(out int state) == 0
        && state is 2 or 3;

    private void OnFullscreenTick()
    {
        if (!_widget.IsAlive)
            return;

        bool now = FullscreenAppActive();
        if (now == _fullscreen)
            return;

        _fullscreen = now;
        Place();
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

        if (_fullscreen)
        {
            Mode("full-screen application in front, bars hidden");
            HoldHidden();
            return;
        }

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
        _widget.Vertical = TaskbarLayout.IsVertical(edge);
        var (width, height) = TaskbarLayout.FitSize(taskbarPx, edge, dpi);
        // The cut this app makes in the taskbar for Start looks like islands too, so it is not read.
        NoRoom? noRoom = null;
        var island = overlay && !_holeOpen
            ? FindIsland(taskbar, taskbarPx, edge, dpi, _floating ? TaskbarLayout.Scale(FloatHysteresis, dpi) : 0, out noRoom)
            : null;
        _onIsland = island is not null;
        if (noRoom is not null)
        {
            PlaceFloating(taskbar, taskbarPx, monitor, edge, dpi, noRoom);
            return;
        }

        _floating = false;
        _widget.Floating = false;
        _widget.ShowIsland(island?.Look);
        if (island is { } onIsland)
        {
            ReleaseHole(taskbar);
            var look = onIsland.Look;
            Mode($"island, slot {Describe(onIsland.Slot)}, radius {look.Radius:0.#}, outline {look.OutlineWidth:0.##}px, dpi {dpi}");
            DockOver(onIsland.Slot, lifted: false);
            return;
        }

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
            if (TaskbarRaised(taskbar))
            {
                if (UiAccessActive)
                {
                    // With UIAccess the bars sit in ZBID_UIACCESS, above the raised taskbar in every
                    // state, so there is nothing to cut and no need to step aside.
                    ReleaseHole(taskbar);
                    Mode($"overlay above the raised taskbar (UIAccess), slot {Describe(screenSlot)}, dpi {dpi}");
                    DockOver(screenSlot, lifted: false);
                }
                else if (!_regionOwnedElsewhere && HoldInsideTaskbar(taskbar, taskbarPx, screenSlot))
                {
                    Mode($"overlay through the taskbar, no island, slot {Describe(screenSlot)}, dpi {dpi}");
                    DockOver(screenSlot, lifted: false);
                    ArmLiftWatch(true);
                }
                else if (_regionOwnedElsewhere)
                {
                    // Start11 puts its own region back on the raised taskbar at once, so a cut only
                    // flickers, and behind the Start menu it shows the menu instead of the bars. The region
                    // is left alone and the bars stay where they are, covered, until the taskbar is back on
                    // the desktop. Experimental administrator mode (UIAccess) avoids this entirely.
                    _holeOpen = false;
                    _holeSlot = default;
                    Mode($"overlay on the raised taskbar, its region belongs to another program, slot {Describe(screenSlot)}, dpi {dpi}");
                    DockOver(screenSlot, lifted: false);
                }
                else
                {
                    ReleaseHole(taskbar);
                    var slot = TaskbarLayout.Beside(taskbarPx, screenSlot, edge, monitor);
                    Mode($"overlay beside the taskbar, no island, slot {Describe(slot)}, dpi {dpi}");
                    DockOver(slot, lifted: true);
                }
            }
            else
            {
                ReleaseHole(taskbar);
                _cutGuard.Reset();
                Mode($"overlay on the taskbar, no island, slot {Describe(screenSlot)}, dpi {dpi}");
                DockOver(screenSlot, lifted: false);
            }
        }
        else
        {
            Mode($"embedded in the taskbar, slot {Describe(screenSlot)}, dpi {dpi}");
            Embed(taskbar, taskbarPx, screenSlot, allowReparent);
        }
    }

    /// <summary>
    /// The taskbar is islands and the apps island leaves no room: the bars float above the taskbar
    /// in the same island look, faded until the pointer is over them, wherever the user drops them.
    /// </summary>
    private void PlaceFloating(IntPtr taskbar, PxRect taskbarPx, PxRect monitor, BarEdge edge, int dpi, NoRoom noRoom)
    {
        ReleaseHole(taskbar);
        _floating = true;
        _onIsland = false;
        _widget.Floating = true;
        _widget.ShowIsland(noRoom.Look);
        string reason = $"no room for the island ({noRoom.Room}px), radius {noRoom.Look.Radius:0.#}, dpi {dpi}";
        if (FloatingSettings.State.Hidden)
        {
            Mode($"floating above the taskbar, hidden by the user, {reason}");
            HoldHidden();
            return;
        }

        int margin = noRoom.Look.Margin;
        int width = TaskbarLayout.Scale(TaskbarLayout.PreferredWidth, dpi) + (2 * margin);
        int height = noRoom.Height + (2 * margin);
        var home = FloatingBars.DefaultSlot(
            taskbarPx, monitor, edge, noRoom.EdgeMargin - margin, width, height, TaskbarLayout.Scale(FloatingBars.Gap, dpi));
        var slot = FloatingBars.Resolve(FloatingSettings.State, home, Monitors());
        Mode($"floating above the taskbar, {reason}");
        DockOver(slot, lifted: false);
    }

    public void DragTo(int screenLeft, int screenTop)
    {
        if (_floating)
        {
            FloatingSettings.SetPosition(screenLeft, screenTop);
            Place();
            return;
        }

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
        var (width, height) = TaskbarLayout.FitSize(taskbarPx, edge, dpi);
        var natural = TaskbarLayout.Place(taskbarPx, trayPx, edge, 0, width, height);
        _delta = edge is BarEdge.Left or BarEdge.Right
            ? screenTop - natural.Top
            : screenLeft - natural.Left;
        WriteDelta(_delta);
        Place();
    }

    public void Dispose()
    {
        FloatingSettings.Changed -= OnFloatingSettingsChanged;
        SettingsStore.Changed -= OnSettingsChanged;
        ReleaseHole(NativeMethods.FindWindow("Shell_TrayWnd", null));
        _settleTimer.Stop();
        _orderTimer.Stop();
        _fullscreenTimer.Stop();
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
        if (!_widget.IsAlive || _widget.IsDragging)
            return;

        // While lifted the bars sit beside the taskbar, so the order does not matter. The lift
        // watch keeps placing them until the taskbar is back in the bars' band.
        if (!_widget.PinTopmost || _widget.HeldHidden || _lifted)
            return;

        var taskbar = NativeMethods.FindWindow("Shell_TrayWnd", null);
        if (taskbar == IntPtr.Zero)
            return;

        // Task View moves the taskbar into a band above the bars and, with Start11, leaves it
        // there after it closes. No topmost window can sit over a taskbar in a higher band.
        // An island sits outside the taskbar's region, so the band does not hide it.
        // Floating bars are clear of the taskbar as well.
        if (!_onIsland && !_floating && !UiAccessActive && TaskbarInOtherBand(taskbar))
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
                && widgetBand != NativeMethods.UiAccessBand
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

    /// <summary>
    /// True when the taskbar has left the desktop band. A child inherits that band, so this
    /// looks at the taskbar only: comparing it with the bars would undo the cut on the next tick.
    /// </summary>
    private static bool TaskbarRaised(IntPtr taskbar)
    {
        try
        {
            return NativeMethods.GetWindowBand(taskbar, out var band) && band != NativeMethods.DesktopBand;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    /// <summary>
    /// Cuts the bars' slot out of a rectangular taskbar. The higher band then no longer covers
    /// those pixels, and the bars stay where they are. A complex region belongs to Start11.
    /// </summary>
    private bool HoldInsideTaskbar(IntPtr taskbar, PxRect taskbarPx, PxRect slot)
    {
        // Start11 clears the region of the raised taskbar, which undoes this cut. Cutting again each
        // time would flicker the bars, so after a few losses in a row the cut is dropped.
        if (_holeOpen)
        {
            bool intact = HoleIntact(taskbar, taskbarPx);
            bool couldCut = _cutGuard.MayCut;
            _cutGuard.Report(intact);
            if (couldCut && !_cutGuard.MayCut)
            {
                _regionOwnedElsewhere = true;
                AppLog.Info("Taskbar region keeps being reset by another program, it is no longer cut");
            }
        }

        if (!_cutGuard.MayCut)
            return false;
        if (!CanCutTaskbar(taskbar))
            return false;
        if (_holeOpen
            && slot.Left == _holeSlot.Left
            && slot.Top == _holeSlot.Top
            && slot.Width == _holeSlot.Width
            && slot.Height == _holeSlot.Height
            && HoleIntact(taskbar, taskbarPx))
            return true;

        int left = slot.Left - taskbarPx.Left;
        int top = slot.Top - taskbarPx.Top;
        int right = left + slot.Width;
        int bottom = top + slot.Height;
        if (left < 0 || top < 0 || right > taskbarPx.Width || bottom > taskbarPx.Height)
            return false;

        IntPtr window = NativeMethods.CreateRectRgn(0, 0, taskbarPx.Width, taskbarPx.Height);
        IntPtr gap = NativeMethods.CreateRectRgn(left, top, right, bottom);
        if (window == IntPtr.Zero || gap == IntPtr.Zero)
        {
            if (window != IntPtr.Zero)
                NativeMethods.DeleteObject(window);
            if (gap != IntPtr.Zero)
                NativeMethods.DeleteObject(gap);
            return false;
        }

        int combined = NativeMethods.CombineRgn(window, window, gap, NativeMethods.RgnDiff);
        NativeMethods.DeleteObject(gap);
        if (combined == 0)
        {
            NativeMethods.DeleteObject(window);
            return false;
        }

        // SetWindowRgn takes ownership of the region when it succeeds.
        if (NativeMethods.SetWindowRgn(taskbar, window, true) == 0)
        {
            NativeMethods.DeleteObject(window);
            return false;
        }

        _holeOpen = true;
        _holeSlot = slot;
        return true;
    }

    /// <summary>
    /// A taskbar in a higher band can only be cut. Start11 shapes its taskbar with a complex region
    /// of its own, and the cut goes over it: the guard gives up if it keeps being put back.
    /// An empty region has nothing to cut.
    /// </summary>
    private static bool CanCutTaskbar(IntPtr taskbar) => RegionKind(taskbar) != NullRegion;

    /// <summary>
    /// True while the hole for the bars is still in the taskbar region. A complex region alone
    /// does not say so: Start11 puts a complex region of its own back, and that one has no hole.
    /// </summary>
    private bool HoleIntact(IntPtr taskbar, PxRect taskbarPx)
    {
        var probe = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (probe == IntPtr.Zero)
            return true;
        try
        {
            int kind = NativeMethods.GetWindowRgn(taskbar, probe);
            if (kind == 0 || kind == NullRegion)
                return false;
            int x = _holeSlot.Left - taskbarPx.Left + (_holeSlot.Width / 2);
            int y = _holeSlot.Top - taskbarPx.Top + (_holeSlot.Height / 2);
            return !NativeMethods.PtInRegion(probe, x, y);
        }
        finally
        {
            NativeMethods.DeleteObject(probe);
        }
    }

    private const int NullRegion = 1;

    private static int RegionKind(IntPtr window)
    {
        var probe = NativeMethods.CreateRectRgn(0, 0, 0, 0);
        if (probe == IntPtr.Zero)
            return NativeMethods.ComplexRegion;
        try
        {
            return NativeMethods.GetWindowRgn(window, probe);
        }
        finally
        {
            NativeMethods.DeleteObject(probe);
        }
    }

    private void ReleaseHole(IntPtr taskbar)
    {
        if (!_holeOpen)
            return;
        if (taskbar != IntPtr.Zero)
            NativeMethods.SetWindowRgn(taskbar, IntPtr.Zero, true);
        _holeOpen = false;
        _holeSlot = default;
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
        ReleaseHole(NativeMethods.FindWindow("Shell_TrayWnd", null));
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
        ReleaseHole(NativeMethods.FindWindow("Shell_TrayWnd", null));
        Mode("no taskbar found, floating in the screen corner");
        _onIsland = false;
        _floating = false;
        _widget.Vertical = false;
        _widget.Floating = false;
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

    /// <summary>The bounds of every screen, in screen pixels.</summary>
    private static List<PxRect> Monitors()
    {
        var screens = new List<PxRect>();
        NativeMethods.MonitorEnumProc collect = (IntPtr monitor, IntPtr dc, ref NativeMethods.Rect rect, IntPtr data) =>
        {
            screens.Add(ToPx(rect));
            return true;
        };
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, collect, IntPtr.Zero);
        GC.KeepAlive(collect);
        return screens;
    }

    /// <summary>An islands taskbar with no room for the bars: how they should look while floating.</summary>
    private sealed record NoRoom(IslandLook Look, int Height, int EdgeMargin, int Room);

    /// <summary>
    /// The slot for the bars as an island of their own: the corner before the first island, or
    /// the free stretch beside the Start or tray island, wherever Start11 aligns its buttons.
    /// When the taskbar is made of islands but no stretch has room, null is returned and <paramref name="noRoom"/> says how
    /// the bars should look when floating instead. <paramref name="extra"/> widens the room the
    /// bars ask for, so floating bars do not flip back and forth as the apps island grows.
    /// </summary>
    private static (PxRect Slot, IslandLook Look)? FindIsland(IntPtr taskbar, PxRect taskbarPx, BarEdge edge, int dpi, int extra, out NoRoom? noRoom)
    {
        noRoom = null;
        if (edge is not (BarEdge.Bottom or BarEdge.Top))
            return null;

        // The solid and rounded styles of Start11 leave one rectangle or one rounded bar, not islands.
        // Islands merge into one when the apps reach the tray, which only the chosen style tells apart.
        var islands = TaskbarIslands.FromRegion(RegionRects(taskbar));
        if (!TaskbarIslands.AreIslands(islands, Start11Theme.IslandsStyle()))
            return null;

        var look = Start11Theme.Look(TaskbarIslands.CornerRadius(islands[0].Corner)).At(dpi);
        var slot = TaskbarIslands.BarsSlot(
            taskbarPx,
            islands,
            TaskbarLayout.Scale(TaskbarLayout.PreferredWidth, dpi),
            TaskbarLayout.Scale(160, dpi) + extra);
        if (slot.IsEmpty)
        {
            noRoom = new NoRoom(
                look,
                islands[0].Bounds.Height,
                Math.Max(0, taskbarPx.Width - islands[^1].Bounds.Right),
                TaskbarIslands.Room(taskbarPx, islands));
            return null;
        }

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
