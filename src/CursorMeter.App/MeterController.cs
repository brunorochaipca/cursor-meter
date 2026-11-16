using System.ComponentModel;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
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
    private readonly TaskbarHost? _taskbar;
    private MeterState _state = MeterState.Loading;
    private FlyoutWindow? _flyout;
    private DateTime _flyoutClosedAt;
    private bool _refreshing;
    private bool _disposed;

    public MeterController(IClassicDesktopStyleApplicationLifetime lifetime)
    {
        _lifetime = lifetime;
        var menu = CreateMenu();

        if (OperatingSystem.IsLinux())
        {
            _statusItem = StatusNotifierItem.TryCreate(menu, ToggleFlyout);
            _statusItem?.Show(_state);
        }

        if (_statusItem is null)
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

        _usageTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(3) };
        _usageTimer.Tick += (_, _) => _ = RefreshAsync();
        _usageTimer.Start();
        _ = RefreshAsync();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _usageTimer.Stop();
        _flyout?.Close();
        _taskbar?.Dispose();
        _tray?.Dispose();
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
        if (OperatingSystem.IsWindows())
            menu.Add(StartupShortcut.MenuEntry());
        if (OperatingSystem.IsLinux())
            menu.Add(AutostartEntry.MenuEntry());
        menu.Add(null);
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
        flyout.RefreshRequested += (_, _) => _ = RefreshAsync();
        flyout.DashboardRequested += (_, _) => OpenDashboard();
        flyout.Closed += (_, _) =>
        {
            _flyout = null;
            _flyoutClosedAt = DateTime.UtcNow;
        };
        flyout.ShowState(_state);
        _flyout = flyout;
        flyout.ShowNear(_taskbar?.Anchor);
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
                return session is null
                    ? new MeterState("Abre o Cursor", ex.Message, "Entra no Cursor ou em cursor.com no Firefox.")
                    : new MeterState("Sessão expirada", ex.Message, "Entra em cursor.com no Firefox.");
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
        // NOTIFYICONDATA caps the Windows tooltip at 63 characters; StatusNotifierItem has no limit.
        if (_tray is not null)
            _tray.ToolTipText = !OperatingSystem.IsWindows() || state.Detail.Length <= 63 ? state.Detail : state.Detail[..63];
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
