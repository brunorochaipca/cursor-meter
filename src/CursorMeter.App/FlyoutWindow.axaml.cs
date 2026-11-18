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
