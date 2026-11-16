using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using CursorMeter.App.Windows;

namespace CursorMeter.App;

/// <summary>
/// The summary popup. It opens above <c>anchor</c> (below it if there is no room), or,
/// without an anchor, in the corner of the screen next to the panel.
/// </summary>
internal sealed partial class FlyoutWindow : Window
{
    private const int Gap = 8;
    private PixelRect? _anchor;
    private bool _closing;

    public FlyoutWindow()
    {
        InitializeComponent();
        TransparencyLevelHint = [WindowTransparencyLevel.Mica];
        RefreshButton.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        DashboardButton.Click += (_, _) => DashboardRequested?.Invoke(this, EventArgs.Empty);
        LayoutUpdated += (_, _) => Reposition();
        Opened += (_, _) =>
        {
            RoundCorners();
            Dispatcher.UIThread.Post(() => Deactivated += (_, _) => CloseOnce());
        };
    }

    /// <summary>
    /// Windows 11 22H2 and later: Mica behind the card, as the Widgets board does, and round corners.
    /// Elsewhere (or when Mica is not active) the card keeps its solid fill.
    /// </summary>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ActualTransparencyLevelProperty)
            Card.Background = ActualTransparencyLevel == WindowTransparencyLevel.Mica
                ? Brushes.Transparent
                : new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18));
    }

    private void RoundCorners()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621) || TryGetPlatformHandle()?.Handle is not { } hwnd)
            return;
        int round = NativeMethods.DwmwcpRound;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaWindowCornerPreference, ref round, sizeof(int));
    }
    public event EventHandler? RefreshRequested;
    public event EventHandler? DashboardRequested;

    public void ShowState(MeterState state)
    {
        DetailText.Text = state.Detail;
        SecondaryText.Text = state.Secondary ?? "";
    }

    public void ShowNear(PixelRect? anchor)
    {
        _anchor = anchor;
        Show();
        Activate();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
            CloseOnce();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _closing = true;
        base.OnClosing(e);
    }

    private void CloseOnce()
    {
        if (!_closing)
            Close();
    }

    private void Reposition()
    {
        // X11 has no primary screen unless RandR was told which output it is.
        var screen = (_anchor is { } a ? Screens.ScreenFromPoint(a.TopLeft) : null)
            ?? Screens.Primary
            ?? Screens.All.FirstOrDefault();
        if (screen is null || Bounds.Height <= 0)
            return;

        var scaling = screen.Scaling;
        var size = PixelSize.FromSize(Bounds.Size, scaling);
        var gap = (int)Math.Round(Gap * scaling);
        var area = screen.WorkingArea;
        bool panelOnTop = area.Y > screen.Bounds.Y;

        int x = _anchor?.X ?? area.Right - size.Width - (gap * 2);
        int top = _anchor?.Y ?? (panelOnTop ? area.Y : area.Bottom);
        int bottom = _anchor?.Bottom ?? top;

        int y = top - size.Height - gap;
        if (y < area.Y)
            y = bottom + gap;

        x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - size.Width));
        y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - size.Height));
        Position = new PixelPoint(x, y);
    }
}
