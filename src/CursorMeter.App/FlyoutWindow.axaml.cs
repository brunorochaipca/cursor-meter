using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
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
        OpenCursorButton.Click += (_, _) => OpenCursorRequested?.Invoke(this, EventArgs.Empty);
        DownloadCursorButton.Click += (_, _) => DownloadCursorRequested?.Invoke(this, EventArgs.Empty);
        OpenFirefoxButton.Click += (_, _) => OpenFirefoxRequested?.Invoke(this, EventArgs.Empty);
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
        if (change.Property != ActualTransparencyLevelProperty)
            return;
        AppLog.Info($"Flyout backdrop: {ActualTransparencyLevel}");
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
    public event EventHandler? OpenCursorRequested;
    public event EventHandler? DownloadCursorRequested;
    public event EventHandler? OpenFirefoxRequested;

    public void ShowState(MeterState state)
    {
        if (state.Setup is { } setup)
        {
            ShowSetup(setup);
            return;
        }

        SetupPanel.IsVisible = false;
        DashboardButton.IsVisible = true;
        RefreshButton.Content = "Atualizar";
        DetailText.Text = state.Detail;
        DetailText.MinHeight = 40;
        SecondaryText.IsVisible = true;
        SecondaryText.MinHeight = 18;
        SecondaryText.Margin = new Thickness(0, 0, 0, 16);
        SecondaryText.Text = state.Secondary ?? "";
    }

    /// <summary>
    /// No usable session: instead of the status line, the two places the account can be read from,
    /// what each one is missing and the button that fixes it. Nothing here polls: after the user
    /// has done it, "Verificar já" reads again.
    /// </summary>
    private void ShowSetup(SessionSetup setup)
    {
        DetailText.Text = "Ligar a conta do Cursor";
        DetailText.MinHeight = 0;
        SecondaryText.Text = "O CursorMeter lê os dados de uma destas fontes. Basta uma.";
        SecondaryText.MinHeight = 0;
        SecondaryText.Margin = new Thickness(0, 2, 0, 12);
        SecondaryText.IsVisible = true;
        SetupPanel.IsVisible = true;
        DashboardButton.IsVisible = false;
        RefreshButton.Content = "Verificar já";

        CursorStatusText.Text = !setup.CursorInstalled ? "✗ não instalado"
            : setup.CursorHasSession ? "✗ sessão expirada"
            : "✗ sem sessão";
        CursorHintText.Text = !setup.CursorInstalled ? "Instala o Cursor e inicia sessão."
            : setup.CursorHasSession ? "Abre o Cursor e volta a iniciar sessão."
            : "Abre o Cursor e inicia sessão.";
        OpenCursorButton.IsVisible = setup.CursorPath is not null;
        DownloadCursorButton.IsVisible = !setup.CursorInstalled;

        var firefox = setup.Firefox;
        FirefoxStatusText.Text = setup.FirefoxPath is null && !firefox.ProfileFound ? "✗ não encontrado"
            : firefox.CookieFound ? "✗ sessão expirada"
            : "✗ sem sessão";
        FirefoxHintText.Text = setup.FirefoxPath is null && !firefox.ProfileFound
            ? "O Firefox não está instalado neste computador."
            : firefox.CookieFound
                ? "A sessão em cursor.com expirou. Entra outra vez no Firefox."
                : "Inicia sessão em cursor.com no Firefox.";
        string? warning = firefox.ClearsCookiesOnClose
            ? "⚠ O Firefox apaga os cookies ao fechar, por isso não guarda a sessão. "
                + "Em Definições › Privacidade e segurança › Cookies, abre \"Gerir exceções\" e permite https://cursor.com. "
                + "Se for uma política da organização, usa o Cursor."
            : firefox.AlwaysPrivate
                ? "⚠ O Firefox está sempre em modo privado, que não guarda a sessão. "
                    + "Em Definições › Privacidade e segurança › Histórico, escolhe \"Lembrar o histórico\"."
                : firefox.ProfileFound && !firefox.CookieFound
                    ? "Se já iniciaste sessão e continua sem funcionar, o Firefox pode não estar a guardar cookies "
                        + "(limpeza ao fechar, modo privado ou política da organização). Nesse caso, usa o Cursor."
                    : null;
        FirefoxWarningText.IsVisible = warning is not null;
        FirefoxWarningText.Text = warning ?? "";
        OpenFirefoxButton.IsVisible = setup.FirefoxPath is not null;
    }

    /// <param name="centered">Opens the card centred on the anchor instead of lined up with its left edge.</param>
    public void ShowNear(PixelRect? anchor, bool centered = false)
    {
        _anchor = anchor;
        _centered = centered;
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
        var computed = PixelSize.FromSize(Bounds.Size, scaling);

        // The size the window really has: Bounds is in device-independent units and the monitor's
        // scaling is not always the one this window renders with, so a computed height can be taller
        // than the window and leave a gap between it and the taskbar.
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
            // Taskbar at the left or right of the screen: open beside it, with the bottom edge of the
            // card level with the bars (they sit above the notification area), not above them.
            x = left ? area.X + gap : area.Right - size.Width - gap;
            y = (_anchor?.Bottom ?? area.Bottom) - size.Height;
        }

        x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - size.Width));
        y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - size.Height));
        Position = new PixelPoint(x, y);

        var line = $"Flyout placement: anchor {(_anchor is { } p ? $"{p.X},{p.Y} {p.Width}x{p.Height}" : "none")}, "
            + $"work area {area.X},{area.Y} {area.Width}x{area.Height}, screen scaling {scaling:0.##}, render scaling {RenderScaling:0.##}, "
            + $"bounds {Bounds.Width:0.#}x{Bounds.Height:0.#}, computed {computed.Width}x{computed.Height}, window {size.Width}x{size.Height}, "
            + $"taskbar {(side is { } l ? (l ? "left" : "right") : "horizontal")}, at {x},{y}";
        if (line != _lastPlacement)
        {
            _lastPlacement = line;
            AppLog.Info(line);
        }

        // Setting the position raises no layout pass. If the window had not reached its final size yet,
        // place it again once it has.
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

    /// <summary>
    /// True when the taskbar is at the left of the screen, false at the right, null when it is at the
    /// top or bottom. The working area gives it away: the side that lost the most room is the taskbar's.
    /// With an auto-hidden taskbar nothing is reserved, so the shape of the bars decides (stacked
    /// circles are taller than wide) and the nearer screen edge says which side.
    /// </summary>
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

    private string? _lastPlacement;
    private bool _centered;
    private PixelSize? _placedSize;
    private bool _settling;

    /// <summary>The window's size in pixels as Windows has it, or null off Windows or before it exists.</summary>
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
