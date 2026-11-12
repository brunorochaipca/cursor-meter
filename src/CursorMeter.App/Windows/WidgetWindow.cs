using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace CursorMeter.App.Windows;

/// <summary>
/// The window that holds the bars. <see cref="TaskbarEmbedder"/> turns it into a child of the taskbar.
/// </summary>
internal sealed class WidgetWindow : Window
{
    private readonly QuotaBars _bars = new();
    private bool _dragging;
    private bool _closed;
    private NativeMethods.Point _dragCursor;
    private NativeMethods.Rect _dragWindow;

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
        NativeMethods.SetWindowLong(Handle, NativeMethods.GwlExStyle, exStyle | NativeMethods.WsExToolWindow);
    }

    public event EventHandler? Clicked;
    public event EventHandler? MenuRequested;
    public event EventHandler<PixelPoint>? Dragged;

    public IntPtr Handle { get; }
    public bool IsDragging => _dragging;
    public bool IsAlive => !_closed && NativeMethods.IsWindow(Handle);

    /// <summary>Set while the window is a child of the taskbar, so Avalonia never restores its popup style.</summary>
    public bool Embedded { get; set; }

    /// <summary>The slot in the taskbar; every move or resize that does not come from a drag is pinned to it.</summary>
    public PxRect? LockedSlot { get; set; }

    public PixelRect? ScreenBounds => NativeMethods.GetWindowRect(Handle, out var rect)
        ? new PixelRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top)
        : null;

    public int ToPixels(double value) => (int)Math.Round(value * RenderScaling);

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
        exStyle |= NativeMethods.WsExToolWindow;
        if (Embedded)
        {
            style &= unchecked(~(uint)(NativeMethods.WsPopup | NativeMethods.WsClipSiblings));
            style |= NativeMethods.WsChild;
        }

        return (style, exStyle);
    }

    private IntPtr OnWindowMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == NativeMethods.WmWindowPosChanging && !_dragging && LockedSlot is { IsEmpty: false } slot)
        {
            var pos = Marshal.PtrToStructure<NativeMethods.WindowPos>(lParam);
            pos.X = slot.Left;
            pos.Y = slot.Top;
            pos.Cx = slot.Width;
            pos.Cy = slot.Height;
            pos.Flags &= ~(NativeMethods.SwpNoSize | NativeMethods.SwpNoMove);
            Marshal.StructureToPtr(pos, lParam, false);
        }

        return IntPtr.Zero;
    }
}
