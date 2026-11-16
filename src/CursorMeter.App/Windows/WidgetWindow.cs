using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Immutable;

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

    public WidgetWindow()
    {
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = TaskbarLayout.PreferredWidth;
        Height = TaskbarLayout.PreferredHeight;
        Background = Palette.Background;
        Cursor = new Cursor(StandardCursorType.Hand);
        Title = "CursorMeter";
        Content = _bars;

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

    /// <summary>Draws the bars as a rounded island, or as the plain box with <c>null</c>.</summary>
    public void ShowIsland(IslandLook? island)
    {
        if (island == _island)
            return;

        _island = island;
        if (island is null)
        {
            TransparencyLevelHint = [];
            Background = Palette.Background;
            _bars.ShowFrame(Palette.Background, QuotaBars.DefaultOutline, 0, 0, 0.5);
            return;
        }

        // The window is transparent and Margin pixels larger than the island on every side, so the
        // antialiased corners and the outer half of the outline blend with whatever is behind,
        // as Start11 draws them. Its outline is centred on the island's outermost row of pixels.
        double pixel = 1 / RenderScaling;
        var outline = island.Outline is { } color
            ? new Pen(new ImmutableSolidColorBrush(color), island.OutlineWidth)
            : null;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        _bars.ShowFrame(
            new ImmutableSolidColorBrush(island.Fill),
            outline,
            island.Radius * pixel,
            island.Margin * pixel,
            pixel / 2);
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

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed
            && NativeMethods.GetCursorPos(out _dragCursor)
            && NativeMethods.GetWindowRect(Handle, out _dragWindow))
        {
            _dragging = true;
            NativeMethods.SetCapture(Handle);
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
