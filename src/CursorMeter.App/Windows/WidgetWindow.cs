using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Threading;

namespace CursorMeter.App.Windows;

/// <summary>
/// The window that holds the bars. On Windows 10 <see cref="TaskbarEmbedder"/> parents it to the taskbar.
/// On Windows 11 it stays a top-level window drawn over the taskbar.
/// </summary>
internal sealed class WidgetWindow : Window
{
    private readonly QuotaBars _bars = new();
    private bool _dragging;
    private bool _closed;
    private NativeMethods.Point _dragCursor;
    private NativeMethods.Rect _dragWindow;
    private IslandLook? _island;
    private bool _floating;
    private BarFrame? _frame;
    private AppSettings _settings = SettingsStore.Current;

    public WidgetWindow()
    {
        _bars.Transitions = new Transitions
        {
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(160) },
        };
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = OperatingSystem.IsWindows() && UiAccess.IsActive;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = TaskbarLayout.PreferredWidth;
        Height = TaskbarLayout.PreferredHeight;
        Background = Palette.Background;
        Cursor = new Cursor(StandardCursorType.Hand);
        Title = "CursorMeter";
        Content = _bars;
        _bars.ApplyStyle(BarStyle.From(_settings));
        UpdateLook();
        SettingsStore.Changed += OnSettingsChanged;

        Handle = TryGetPlatformHandle()?.Handle
            ?? throw new InvalidOperationException("A janela das barras não tem HWND.");
        Win32Properties.AddWindowStylesCallback(this, KeepStyles);
        Win32Properties.AddWndProcHookCallback(this, OnWindowMessage);
        int exStyle = NativeMethods.GetWindowLong(Handle, NativeMethods.GwlExStyle);
        NativeMethods.SetWindowLong(
            Handle,
            NativeMethods.GwlExStyle,
            exStyle | NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate);
    }

    public event EventHandler? Clicked;
    public event EventHandler? MenuRequested;
    public event EventHandler<PixelPoint>? Dragged;

    public IntPtr Handle { get; }
    public bool IsDragging => _dragging;
    public bool IsAlive => !_closed && NativeMethods.IsWindow(Handle);

    /// <summary>Set while the window is a child of the taskbar, so Avalonia never restores its popup style.</summary>
    public bool Embedded { get; set; }

    /// <summary>Set while the window is drawn over the taskbar, so Avalonia cannot drop it behind the shell.</summary>
    public bool PinTopmost { get; set; }

    /// <summary>Set while the taskbar is auto-hidden, so a later move does not show the bars on the desktop.</summary>
    public bool HeldHidden { get; set; }

    /// <summary>The slot in the taskbar; every move or resize that does not come from a drag is pinned to it.</summary>
    public PxRect? LockedSlot { get; set; }

    public PixelRect? ScreenBounds => NativeMethods.GetWindowRect(Handle, out var rect)
        ? new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
        : null;

    public int ToPixels(double value) => (int)Math.Round(value * RenderScaling);

    /// <summary>Set while the taskbar is at the left or right of the screen: the bars are drawn stacked.</summary>
    public bool Vertical
    {
        get => _bars.Vertical;
        set => _bars.Vertical = value;
    }

    /// <summary>
    /// Set while the bars float above the taskbar because the island has no room: they stay faded
    /// until the pointer is over them, so they hide as little as possible of what is behind.
    /// </summary>
    public bool Floating
    {
        get => _floating;
        set
        {
            if (_floating == value)
                return;
            _floating = value;
            UpdateLook();
            UpdateFade();
        }
    }

    /// <summary>
    /// Draws the bars as a rounded island, or as the plain box with <c>null</c>. While the bars
    /// float, the theme of the settings decides what the box looks like.
    /// </summary>
    public void ShowIsland(IslandLook? island)
    {
        // Also redrawn when only the scale changed, as the bars move to a monitor with another zoom.
        _island = island;
        UpdateLook();
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => Dispatcher.UIThread.Post(() =>
    {
        if (_closed)
            return;
        _settings = SettingsStore.Current;
        _bars.ApplyStyle(BarStyle.From(_settings));
        UpdateLook();
        UpdateFade();
    });

    private void UpdateLook()
    {
        var frame = BarFrame.Resolve(_settings, _island, _floating, RenderScaling);
        if (frame == _frame)
            return;

        // An island window is transparent and Margin pixels larger than the island on every side, so
        // the antialiased corners and the outer half of the outline blend with whatever is behind,
        // as Start11 draws them. Its outline is centred on the island's outermost row of pixels.
        _frame = frame;
        TransparencyLevelHint = frame.Transparent ? [WindowTransparencyLevel.Transparent] : [];
        Background = frame.Transparent ? Brushes.Transparent : Palette.Background;
        _bars.ShowFrame(frame.FillBrush, frame.OutlinePen, frame.Radius, frame.Margin, frame.Inset);
    }
    public void ShowState(MeterState state)
    {
        if (state.Quotas is { } quotas)
        {
            _bars.ShowQuotas(quotas);
            Title = QuotaText.Line(quotas);
        }
        else
        {
            _bars.ShowStatus(state.Status ?? state.Detail);
            Title = "CursorMeter";
        }
    }

    protected override void OnPointerEntered(PointerEventArgs e)
    {
        base.OnPointerEntered(e);
        UpdateFade();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        UpdateFade();
    }

    private void UpdateFade() => _bars.Opacity = !_floating || _dragging || IsPointerOver ? 1 : _settings.FadedOpacity / 100d;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && NativeMethods.GetCursorPos(out _dragCursor)
            && NativeMethods.GetWindowRect(Handle, out _dragWindow))
        {
            _dragging = true;
            NativeMethods.SetCapture(Handle);
            UpdateFade();
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (e.InitialPressMouseButton == MouseButton.Right)
        {
            MenuRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (e.InitialPressMouseButton != MouseButton.Left || !_dragging)
            return;
        _dragging = false;
        NativeMethods.ReleaseCapture();
        UpdateFade();
        if (!NativeMethods.GetCursorPos(out var cursor))
            return;
        var moved = Math.Abs(cursor.X - _dragCursor.X) + Math.Abs(cursor.Y - _dragCursor.Y);
        if (moved <= 4)
            Clicked?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_dragging || !NativeMethods.GetCursorPos(out var cursor))
            return;
        if (Math.Abs(cursor.X - _dragCursor.X) + Math.Abs(cursor.Y - _dragCursor.Y) < 3)
            return;
        Dragged?.Invoke(this, new PixelPoint(_dragWindow.Left + cursor.X - _dragCursor.X, _dragWindow.Top + cursor.Y - _dragCursor.Y));
    }

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        SettingsStore.Changed -= OnSettingsChanged;
        base.OnClosed(e);
    }

    private (uint Style, uint ExStyle) KeepStyles(uint style, uint exStyle)
    {
        exStyle |= (uint)(NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate);
        if (PinTopmost)
            exStyle |= (uint)NativeMethods.WsExTopMost;
        else
            exStyle &= unchecked((uint)~NativeMethods.WsExTopMost);
        if (Embedded)
        {
            style &= unchecked(~(uint)(NativeMethods.WsPopup | NativeMethods.WsClipSiblings));
            style |= NativeMethods.WsChild;
        }

        return (style, exStyle);
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmContextMenu)
        {
            // The right button already opened the app's own menu. Left to DefWindowProc, a child window
            // hands WM_CONTEXTMENU to its parent, so on Windows 10 the taskbar opened its menu as well.
            handled = true;
            return IntPtr.Zero;
        }

        if (message != NativeMethods.WmWindowPosChanging || _dragging)
            return IntPtr.Zero;

        var pos = Marshal.PtrToStructure<NativeMethods.WindowPos>(lParam);
        if (HeldHidden)
        {
            pos.Flags |= NativeMethods.SwpHideWindow;
            pos.Flags &= ~NativeMethods.SwpShowWindow;
            Marshal.StructureToPtr(pos, lParam, false);
            return IntPtr.Zero;
        }

        if (LockedSlot is { IsEmpty: false } slot)
        {
            pos.X = slot.Left;
            pos.Y = slot.Top;
            pos.Cx = slot.Width;
            pos.Cy = slot.Height;
            pos.Flags &= ~(NativeMethods.SwpNoSize | NativeMethods.SwpNoMove);
            if (PinTopmost)
            {
                pos.HwndInsertAfter = NativeMethods.HwndTopMost;
                pos.Flags &= ~NativeMethods.SwpNoZOrder;
            }

            Marshal.StructureToPtr(pos, lParam, false);
        }

        return IntPtr.Zero;
    }
}
