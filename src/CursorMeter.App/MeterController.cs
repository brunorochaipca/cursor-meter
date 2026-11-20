using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using CursorMeter.App.ExtraSpend;
using CursorMeter.App.Linux;
using CursorMeter.App.Windows;

namespace CursorMeter.App;

/// <summary>
/// Reads the quotas every few minutes and publishes them to the tray icon, the flyout
/// and, on Windows, the bars inside the taskbar.
/// </summary>
internal sealed class MeterController : IDisposable
{
    private const string DashboardUrl = "https://cursor.com/dashboard?tab=spending";

    // Clicking the bars or the tray icon first deactivates an open flyout; that click must not reopen it.
    private static readonly TimeSpan ReopenGuard = TimeSpan.FromMilliseconds(300);

    private readonly IClassicDesktopStyleApplicationLifetime _lifetime;
    private readonly UsageClient _client = new();
    private readonly DispatcherTimer _usageTimer;
    private readonly TrayIcon? _tray;
    private readonly StatusNotifierItem? _statusItem;
    private readonly PanelFeedWriter? _panelFeed;
    private readonly TaskbarHost? _taskbar;
    private MeterState _state = MeterState.Loading;
    private FlyoutWindow? _flyout;
    private AboutWindow? _about;
    private SettingsWindow? _settings;
    private DateTime _flyoutClosedAt;
    private bool _refreshing;
    private bool _disposed;

    private readonly ExtraSpendFeature _extra; // ExtraSpend
    private DateTime _eventsDeniedUntil; // ExtraSpend
    private DateOnly _eventsWholeDay; // ExtraSpend
    private DateTime _eventsWholeAt; // ExtraSpend

    public MeterController(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
        var menu = CreateMenu();
        // ExtraSpend: the minute graph, shown instead of the summary.
        _extra = new ExtraSpendFeature(PollExtraAsync, ExtraAnchor, CloseSummary, NoteFlyoutClosed);

        if (OperatingSystem.IsLinux())
        {
            _statusItem = StatusNotifierItem.TryCreate(menu, ToggleFlyout);
            _panelFeed = PanelFeedWriter.TryCreate();
            if (_panelFeed is not null)
                _panelFeed.ExtensionChanged += (_, _) => Dispatcher.UIThread.Post(ApplyPanelExtension);
            ApplyPanelExtension();
            _statusItem?.Show(_state);
            WritePanelFeed();
        }

        // Windows has its own native notification area icon and menu, owned by the taskbar host.
        if (_statusItem is null && !OperatingSystem.IsWindows())
        {
            _tray = new TrayIcon
            {
                Icon = AppIcon.Create(),
                ToolTipText = "CursorMeter",
                Menu = ToNativeMenu(menu),
            };
            _tray.Clicked += (_, _) => ToggleFlyout();
            TrayIcon.SetIcons(Application.Current!, [_tray]);
        }

        if (OperatingSystem.IsWindows())
        {
            _taskbar = new TaskbarHost(menu);
            _taskbar.Clicked += (_, _) => ToggleFlyout();
            _taskbar.Show(_state);
        }

        _usageTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(SettingsStore.Current.RefreshMinutes) };
        _usageTimer.Tick += (_, _) => _ = RefreshAsync();
        _usageTimer.Start();
        SettingsStore.Changed += OnSettingsChanged;
        _ = RefreshAsync();
        _extra.Start(); // ExtraSpend
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        SettingsStore.Changed -= OnSettingsChanged;
        _extra.Dispose(); // ExtraSpend
        _usageTimer.Stop();
        _flyout?.Close();
        _about?.Close();
        _settings?.Close();
        _taskbar?.Dispose();
        _tray?.Dispose();
        _panelFeed?.Dispose();
        _statusItem?.Dispose();
        _client.Dispose();
    }

    private List<MenuEntry?> CreateMenu()
    {
        var menu = new List<MenuEntry?>();
        // GNOME's AppIndicator extension opens the menu on a single left click and only activates on a double click.
        if (OperatingSystem.IsLinux())
            menu.Add(new("Mostrar resumo", ToggleFlyout));
        menu.Add(new("Atualizar", () => _ = RefreshAsync()));
        menu.Add(new("Painel do Cursor", OpenDashboard));
        menu.Add(new("Consumo extra", () => _extra.Show())); // ExtraSpend
        menu.Add(new("Definições…", ShowSettings));
        if (OperatingSystem.IsWindows())
        {
            menu.Add(StartupShortcut.MenuEntry());
            menu.Add(FloatingSettings.MenuEntry());
        }

        if (OperatingSystem.IsLinux())
            menu.Add(AutostartEntry.MenuEntry());
        menu.Add(null);
        menu.Add(new("Acerca de", ShowAbout));
        menu.Add(new("Sair", Quit));
        return menu;
    }

    private static NativeMenu ToNativeMenu(IReadOnlyList<MenuEntry?> entries)
    {
        var menu = new NativeMenu();
        var toggles = new List<(NativeMenuItem Item, Func<bool> IsChecked)>();
        void SyncToggles()
        {
            foreach (var (item, isChecked) in toggles)
                item.IsChecked = isChecked();
        }

        foreach (var entry in entries)
        {
            if (entry is null)
            {
                menu.Add(new NativeMenuItemSeparator());
                continue;
            }

            var item = new NativeMenuItem(entry.Label);
            if (entry.IsChecked is { } isChecked)
            {
                item.ToggleType = NativeMenuItemToggleType.CheckBox;
                toggles.Add((item, isChecked));
            }

            item.Click += (_, _) =>
            {
                entry.Run();
                SyncToggles();
            };
            menu.Add(item);
        }

        SyncToggles();
        menu.Opening += (_, _) => SyncToggles();
        return menu;
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        var interval = TimeSpan.FromMinutes(SettingsStore.Current.RefreshMinutes);
        if (_usageTimer.Interval != interval)
            _usageTimer.Interval = interval;
        WritePanelFeed();
    }

    /// <summary>The GNOME Shell extension draws the bars while it is enabled, so the tray text is dropped.</summary>
    private void ApplyPanelExtension()
    {
        if (_disposed)
            return;
        _statusItem?.SetLabelHidden(_panelFeed?.ExtensionActive == true);
    }

    private void WritePanelFeed()
    {
        _panelFeed?.Write(PanelFeed.Build(
            _state.Status,
            _state.Detail,
            _state.Quotas,
            SettingsStore.Current,
            Environment.ProcessId,
            DateTimeOffset.UtcNow));
    }

    private void ShowSettings()
    {
        if (_disposed)
            return;
        if (_settings is not null)
        {
            _settings.Activate();
            return;
        }

        var hooks = OperatingSystem.IsWindows()
            ? new SettingsHooks(StartupShortcut.MenuEntry(), FloatingSettings.MenuEntry(), FloatingSettings.ResetPosition)
            : new SettingsHooks(OperatingSystem.IsLinux() ? AutostartEntry.MenuEntry() : null, null, null);
        var window = new SettingsWindow(hooks);
        window.Closed += (_, _) => _settings = null;
        _settings = window;
        window.Show();
        window.Activate();
    }

    private void ShowAbout()
    {
        if (_disposed)
            return;
        if (_about is not null)
        {
            _about.Activate();
            return;
        }

        var about = new AboutWindow();
        about.Closed += (_, _) => _about = null;
        _about = about;
        about.Show();
        about.Activate();
    }

    private void ToggleFlyout()
    {
        if (_disposed)
            return;
        if (_flyout is not null)
        {
            _flyout.Close();
            return;
        }

        if (DateTime.UtcNow - _flyoutClosedAt < ReopenGuard)
            return;

        var flyout = new FlyoutWindow();
        _extra.Close(); // ExtraSpend: the summary takes the place of the graph
        flyout.RefreshRequested += (_, _) => _ = RefreshAsync();
        flyout.DashboardRequested += (_, _) => OpenDashboard();
        flyout.OpenCursorRequested += (_, _) => _state.Setup?.OpenCursor();
        flyout.RealtimeRequested += (_, _) => _extra.Show(); // ExtraSpend
        flyout.DownloadCursorRequested += (_, _) => SessionSetup.OpenDownload();
        flyout.OpenFirefoxRequested += (_, _) => _state.Setup?.OpenFirefox();
        flyout.Closed += (_, _) =>
        {
            _flyout = null;
            _flyoutClosedAt = DateTime.UtcNow;
        };
        flyout.ShowState(_state);
        _flyout = flyout;

        // On Windows the bars are the anchor and the card lines up with them. On Linux the anchor is the
        // extension's bars in the taskbar, and the card opens centred above them.
        var panelAnchor = _taskbar is null && _panelFeed?.ExtensionActive == true ? _panelFeed.ReadAnchor() : null;
        flyout.ShowNear(_taskbar?.Anchor ?? panelAnchor, centered: panelAnchor is not null,
            follow: _taskbar is null ? null : () => _taskbar?.Anchor);
    }

    // ExtraSpend. Delete with the ExtraSpend folders.
    private (PixelRect? Anchor, bool Centered) ExtraAnchor()
    {
        if (_taskbar?.Anchor is { } bars)
            return (bars, false);
        var panel = _panelFeed?.ExtensionActive == true ? _panelFeed.ReadAnchor() : null;
        return (panel, panel is not null);
    }

    private void CloseSummary() => _flyout?.Close();

    private void NoteFlyoutClosed() => _flyoutClosedAt = DateTime.UtcNow;

    private async Task PollExtraAsync()
    {
        if (_refreshing || _disposed)
            return;

        var session = CursorSession.TryLoadDefault();
        try
        {
            if (session is not null)
            {
                try
                {
                    await _client.FetchAsync(session).ConfigureAwait(false);
                }
                catch (CursorAuthException)
                {
                    await FetchWebPeriodAsync().ConfigureAwait(false);
                }
            }
            else
            {
                await FetchWebPeriodAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is CursorAuthException or CursorUsageException)
        {
        }

        await FetchTodayChargesAsync(session).ConfigureAwait(false);
    }

    private async Task FetchWebPeriodAsync()
    {
        var cookie = FirefoxSession.TryLoadDefault();
        if (cookie is null)
            return;
        await _client.FetchWebAsync(cookie).ConfigureAwait(false);
    }

    // ExtraSpend. The usage list for today, same request as cursor.com/dashboard/usage.
    private async Task FetchTodayChargesAsync(CursorSession? session)
    {
        if (DateTime.UtcNow < _eventsDeniedUntil || _disposed)
            return;

        var cookie = FirefoxSession.TryLoadDefault();
        if (string.IsNullOrWhiteSpace(cookie))
            cookie = session?.AccessToken;
        if (string.IsNullOrWhiteSpace(cookie) || cookie.Contains(';') || cookie.Contains('\r') || cookie.Contains('\n'))
            return;

        var now = DateTimeOffset.Now;
        var start = new DateTimeOffset(now.Date, now.Offset);
        var charges = new List<ExtraCharge>();
        DateTimeOffset? coveredFrom = null;
        try
        {
            // The newest page is read every time. The rest of the day (a busy day has hundreds of rows)
            // is read once and then every few minutes, and in between it is kept from the last full read.
            bool wholeDay = _eventsWholeDay != DateOnly.FromDateTime(now.Date)
                || DateTime.UtcNow - _eventsWholeAt > TimeSpan.FromMinutes(5);
            for (int page = 1; page <= 20; page++)
            {
                var json = await _client.FetchUsageEventsAsync(
                    cookie, start.ToUnixTimeMilliseconds(), now.ToUnixTimeMilliseconds(), page, 100).ConfigureAwait(false);
                var read = ExtraSpendEvents.ReadPage(json);
                if (read is not { } batch)
                    return;
                charges.AddRange(batch.Charges);
                // A short page is the last one. Count the rows, not the charges: most rows are included in the plan.
                if (batch.Rows < 100)
                    break;
                if (!wholeDay)
                {
                    coveredFrom = batch.Oldest;
                    break;
                }
            }

            if (wholeDay && coveredFrom is null)
            {
                _eventsWholeDay = DateOnly.FromDateTime(now.Date);
                _eventsWholeAt = DateTime.UtcNow;
            }
        }
        catch (CursorAuthException)
        {
            _eventsDeniedUntil = DateTime.UtcNow.AddMinutes(10);
            return;
        }

        _extra.ApplyCharges(charges, coveredFrom);
    }

    private async Task RefreshAsync()
    {
        if (_refreshing || _disposed)
            return;
        _refreshing = true;
        try
        {
            var state = await Task.Run(LoadStateAsync);
            if (!_disposed)
                Publish(state);
        }
        finally
        {
            _refreshing = false;
        }
    }

    private async Task<MeterState> LoadStateAsync()
    {
        try
        {
            var session = CursorSession.TryLoadDefault();
            UsageQuotas quotas;
            try
            {
                quotas = await LoadQuotasAsync(session).ConfigureAwait(false);
            }
            catch (CursorAuthException ex)
            {
                var setup = SessionSetup.Inspect(session);
                return session is null
                    ? new MeterState("Abre o Cursor", ex.Message, "Entra no Cursor ou em cursor.com no Firefox.", Setup: setup)
                    : new MeterState("Sessão expirada", ex.Message, "Entra em cursor.com no Firefox.", Setup: setup);
            }

            var line = QuotaText.Line(quotas);
            var secondary = QuotaText.Summary(session?.Plan, session?.Email, quotas.CycleEnd, DateTimeOffset.Now);
            return new MeterState(null, line, secondary, quotas);
        }
        catch (CursorUsageException ex)
        {
            return new MeterState("Sem ligação", ex.Message, null);
        }
        catch (HttpRequestException)
        {
            return new MeterState("Sem ligação", "Não consegui falar com o Cursor.", null);
        }
    }

    private string? _lastNoSession;

    /// <summary>Writes why no session was found, once per distinct finding, so the log explains it without repeating on every refresh.</summary>
    private void LogNoSession(bool noCursorSession)
    {
        string why;
        try
        {
            why = (noCursorSession
                ? $"no Cursor session (database {CursorSession.DatabasePath()}: {(File.Exists(CursorSession.DatabasePath()) ? "exists" : "missing")}, CLI auth {(File.Exists(CursorSession.CliAuthPath()) ? "exists" : "missing")})"
                : "Cursor session rejected")
                + "; Firefox program: " + (SessionSetup.FindFirefox() ?? "not found")
                + "; Firefox: " + FirefoxSession.Diagnose();
        }
        catch (Exception ex)
        {
            why = "diagnosis failed: " + ex.Message;
        }

        if (why == _lastNoSession)
            return;
        _lastNoSession = why;
        AppLog.Info("No usable session: " + why);
    }

    private async Task<UsageQuotas> LoadQuotasAsync(CursorSession? session)
    {
        if (session is not null)
        {
            try
            {
                return await _client.FetchAsync(session).ConfigureAwait(false);
            }
            catch (CursorAuthException)
            {
            }
        }

        var cookie = FirefoxSession.TryLoadDefault();
        if (cookie is null)
        {
            LogNoSession(session is null);
            throw new CursorAuthException(session is null
                ? "Sem sessão do Cursor. Entra no Cursor ou em cursor.com no Firefox."
                : "A sessão do Cursor expirou. Entra em cursor.com no Firefox.");
        }

        return await _client.FetchWebAsync(cookie).ConfigureAwait(false);
    }

    private void Publish(MeterState state)
    {
        if (state.Status != _state.Status)
            AppLog.Info(state.Status is null ? "Usage loaded" : $"Usage unavailable: {state.Status}. {state.Detail}");
        _state = state;
        _taskbar?.Show(state);
        _flyout?.ShowState(state);
        _statusItem?.Show(state);
        WritePanelFeed();
        _taskbar?.SetTooltip(state.Detail);
        if (_tray is not null)
            _tray.ToolTipText = state.Detail;
    }

    private void Quit()
    {
        Dispose();
        _lifetime.Shutdown();
    }

    private void OpenDashboard()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = DashboardUrl,
                UseShellExecute = true,
            });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // The click runs on the UI thread. On Linux, UseShellExecute throws when xdg-open,
            // gnome-open and kfmclient are all missing, and that exception would end the process.
            Publish(_state with
            {
                Detail = "Não consegui abrir o painel.",
                Secondary = OperatingSystem.IsLinux()
                    ? "Instala o pacote xdg-utils."
                    : "Não há um programa associado a este endereço.",
            });
            if (_flyout is null)
                ToggleFlyout();
            return;
        }

        // Mutter does not focus a browser started without an activation token, so nothing
        // deactivates the flyout and it would stay on top of the dashboard.
        if (OperatingSystem.IsLinux())
            _flyout?.Close();
    }
}
