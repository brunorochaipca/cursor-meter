using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using CursorMeter.App.Windows;

namespace CursorMeter.App.ExtraSpend;

/// <summary>
/// ExtraSpend. The minute graph above the bars. It takes the same place as the summary and is never shown with it.
/// </summary>
internal sealed class SpendWindow : Window
{
    private const int Gap = 8;

    private readonly Border _card;
    private readonly SpendGraph _graph;
    private readonly TextBlock _total;
    private readonly TextBlock _caption;
    private PixelRect? _anchor;
    private bool _centered;
    private bool _closing;
    private PixelSize? _placedSize;
    private bool _settling;

    public SpendWindow()
    {
        Title = "CursorMeter";
        Width = FlyoutWindow.CardWidth;
        SizeToContent = SizeToContent.Height;
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Background = Brushes.Transparent;
        FontSize = 12;

        var title = new TextBlock
        {
            Text = "Consumo extra",
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.FromRgb(0xEC, 0xEA, 0xE2)),
        };
        _total = new TextBlock
        {
            FontSize = 22,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = new SolidColorBrush(Palette.CursorPool.Color),
        };
        _graph = new SpendGraph { Height = 132, Margin = new Thickness(0, 10, 0, 0) };
        var ago = new TextBlock { Text = "há 60 s", FontSize = 11, Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xAC, 0xA0)) };
        var now = new TextBlock
        {
            Text = "agora",
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xAC, 0xA0)),
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var axis = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), Margin = new Thickness(0, 2, 0, 0) };
        axis.Children.Add(ago);
        Grid.SetColumn(now, 1);
        axis.Children.Add(now);
        _caption = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xB0, 0xAC, 0xA0)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };

        // The only way to dismiss it by hand: it does not close when it loses focus.
        var minimize = new Button
        {
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(4),
            Foreground = new SolidColorBrush(Color.FromRgb(0xC5, 0xC5, 0xC5)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            Content = new PathIcon { Width = 12, Height = 12, Data = Geometry.Parse("M2 11h20v2H2z") },
        };
        ToolTip.SetTip(minimize, "Minimizar");
        minimize.Click += (_, _) => CloseQuiet();
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, -4, -8, 0) };
        title.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(title);
        Grid.SetColumn(minimize, 1);
        header.Children.Add(minimize);

        var stack = new StackPanel();
        stack.Children.Add(header);
        stack.Children.Add(_total);
        stack.Children.Add(_graph);
        stack.Children.Add(axis);
        stack.Children.Add(_caption);
        _card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 14),
            Child = stack,
        };
        Content = _card;
        // After the card exists: this raises OnPropertyChanged, which paints the card.
        TransparencyLevelHint = [WindowTransparencyLevel.Mica];

        LayoutUpdated += (_, _) => Reposition();
        // No close on Deactivated: the graph stays while the user works elsewhere.
        Opened += (_, _) => RoundCorners();
    }

    /// <summary>True when this close was asked by the app, so it must not count as the user dismissing it.</summary>
    public bool WasQuiet { get; private set; }

    public void ShowReading(ExtraSpendView view, int ceilingCents)
    {
        _graph.SetReading(view.Points, view.Tone);
        if (!view.HasReading)
        {
            _total.Text = "";
            _caption.Text = "Ainda sem leitura do consumo extra.";
            return;
        }

        _total.Text = view.IsDay ? Money(view.LatestCents) + " hoje" : Money(view.LatestCents);
        _total.Foreground = new SolidColorBrush(ToneColor(view.Tone));
        _caption.Text = Caption(view, ceilingCents);
    }

    /// <param name="activate">
    /// False when a consumption jump opens it: it must not take the keyboard from what the user is typing,
    /// and a window that was never active cannot be closed by losing focus right away.
    /// </param>
    public void ShowNear(PixelRect? anchor, bool centered = false, bool activate = true)
    {
        _anchor = anchor;
        _centered = centered;
        ShowActivated = activate;
        Show();
        if (activate)
            Activate();
    }

    public void CloseQuiet()
    {
        WasQuiet = true;
        CloseOnce();
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != ActualTransparencyLevelProperty || _card is null)
            return;
        _card.Background = ActualTransparencyLevel == WindowTransparencyLevel.Mica
            ? Brushes.Transparent
            : new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18));
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

    private static string Caption(ExtraSpendView view, int ceilingCents)
    {
        var line = Money(view.MinuteCents) + " neste minuto";
        int today = view.LatestCents - view.DayStartCents;
        if (today > 0 && !view.IsDay)
            line += " · " + Money(today) + " hoje";
        if (view.RemainingCents(ceilingCents) is { } left)
        {
            // Ceiling minus the extra total, so the three numbers can be checked against each other.
            line += left >= 0
                ? "\nRestam " + Money(left) + " (teto " + Money(ceilingCents) + " − gasto " + Money(view.TotalCents) + ")"
                : "\nTeto de " + Money(ceilingCents) + " excedido em " + Money(-left) + " (gasto " + Money(view.TotalCents) + ")";
        }

        return line;
    }

    private static Color ToneColor(ExtraSpendTone tone) => tone switch
    {
        ExtraSpendTone.Hot => Palette.Hot.Color,
        ExtraSpendTone.Warn => Palette.Warn.Color,
        _ => Palette.CursorPool.Color,
    };

    private static string Money(int cents)
        => (cents / 100.0).ToString("0.00", CultureInfo.CurrentCulture) + " $";

    private void RoundCorners()
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621) || TryGetPlatformHandle()?.Handle is not { } hwnd)
            return;
        int round = NativeMethods.DwmwcpRound;
        NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DwmwaWindowCornerPreference, ref round, sizeof(int));
    }

    private void Reposition()
    {
        var screen = (_anchor is { } a ? Screens.ScreenFromPoint(a.TopLeft) : null)
            ?? Screens.Primary
            ?? Screens.All.FirstOrDefault();
        if (screen is null || Bounds.Height <= 0)
            return;

        var scaling = screen.Scaling;
        var computed = PixelSize.FromSize(Bounds.Size, scaling);
        var size = ActualSize() ?? computed;
        _placedSize = size;

        var gap = (int)Math.Round(Gap * scaling);
        var area = screen.WorkingArea;
        bool panelOnTop = area.Y > screen.Bounds.Y;

        int x = _anchor is { } placed
            ? (_centered ? placed.X + (placed.Width / 2) - (size.Width / 2) : placed.X)
            : area.Right - size.Width - (gap * 2);
        int top = _anchor?.Y ?? (panelOnTop ? area.Y : area.Bottom);
        int bottom = _anchor?.Bottom ?? top;
        int y = top - size.Height - gap;
        if (y < area.Y)
            y = bottom + gap;

        var side = _anchor is { } anchor ? VerticalTaskbarSide(screen, anchor) : null;
        if (side is { } left)
        {
            x = left ? area.X + gap : area.Right - size.Width - gap;
            y = (_anchor?.Bottom ?? area.Bottom) - size.Height;
        }

        x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - size.Width));
        y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - size.Height));
        Position = new PixelPoint(x, y);

        if (!_settling)
        {
            _settling = true;
            Dispatcher.UIThread.Post(() =>
            {
                _settling = false;
                if (!_closing && ActualSize() is { } now && now != _placedSize)
                    Reposition();
            }, DispatcherPriority.Background);
        }
    }

    private static bool? VerticalTaskbarSide(Screen screen, PixelRect anchor)
    {
        var bounds = screen.Bounds;
        var area = screen.WorkingArea;
        int left = area.X - bounds.X;
        int right = bounds.Right - area.Right;
        int vertical = Math.Max(area.Y - bounds.Y, bounds.Bottom - area.Bottom);
        int side = Math.Max(left, right);

        if (side > 0 || vertical > 0)
            return side > vertical ? left >= right : null;
        if (anchor.Height <= anchor.Width)
            return null;
        return anchor.X + (anchor.Width / 2) < bounds.X + (bounds.Width / 2);
    }

    private PixelSize? ActualSize()
    {
        if (!OperatingSystem.IsWindows()
            || TryGetPlatformHandle()?.Handle is not { } hwnd
            || !NativeMethods.GetWindowRect(hwnd, out var rect)
            || rect.Right <= rect.Left
            || rect.Bottom <= rect.Top)
            return null;
        return new PixelSize(rect.Right - rect.Left, rect.Bottom - rect.Top);
    }
}
