using Avalonia;
using Avalonia.Threading;

namespace CursorMeter.App.ExtraSpend;

/// <summary>
/// ExtraSpend. Polls the extra total, opens the minute graph when it rises, and keeps that graph
/// out of the summary: one of the two is on screen, never both.
/// To remove the feature, delete both ExtraSpend folders and every line marked ExtraSpend.
/// </summary>
internal sealed class ExtraSpendFeature : IDisposable
{
    public const int PollSeconds = 10;

    private readonly ExtraSpendQuiet _quiet = new();

    private readonly Func<Task> _poll;
    private readonly Func<(PixelRect? Anchor, bool Centered)> _anchor;
    private readonly Action _closeSummary;
    private readonly Action _onUserDismissed;
    private readonly DispatcherTimer _pollTimer;
    private readonly DispatcherTimer _slideTimer;
    private readonly ExtraSpendTrace _trace;
    private SpendWindow? _window;
    private bool _openedByJump;
    private bool _polling;
    private bool _disposed;

    public ExtraSpendFeature(Func<Task> poll, Func<(PixelRect? Anchor, bool Centered)> anchor, Action closeSummary, Action onUserDismissed)
    {
        _poll = poll;
        _anchor = anchor;
        _closeSummary = closeSummary;
        _onUserDismissed = onUserDismissed;

        var saved = ExtraSpendDayFile.Load();
        var today = DateOnly.FromDateTime(DateTime.Now);
        _trace = saved is { } mark && mark.Date == today
            ? new ExtraSpendTrace(mark.Date, mark.Cents)
            : new ExtraSpendTrace();

        UsageClient.UsageObserved = Observe;
        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(PollSeconds) };
        _pollTimer.Tick += (_, _) => _ = PollAsync();
        _slideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _slideTimer.Tick += (_, _) => Slide();
    }

    public void Start()
    {
        _pollTimer.Start();
        _slideTimer.Start();
    }

    /// <summary>The user asked for the graph. It stays until they close it.</summary>
    public void Show()
    {
        if (_disposed)
            return;
        if (_window is { IsVisible: true })
        {
            // Closing it from the menu is dismissing it by hand, as the minimize button is.
            _openedByJump = false;
            _quiet.Minimize();
            _window.CloseQuiet();
            return;
        }

        _openedByJump = false;
        _quiet.Reopen();
        _closeSummary();
        Open(activate: true);
    }

    /// <summary>Hides the graph without treating it as a click on the bars.</summary>
    public void Close()
    {
        _openedByJump = false;
        _window?.CloseQuiet();
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (UsageClient.UsageObserved == Observe)
            UsageClient.UsageObserved = null;
        _pollTimer.Stop();
        _slideTimer.Stop();
        Close();
    }

    private void Observe(string json)
    {
        if (_disposed)
            return;
        try
        {
            var cents = ExtraSpendParser.ReadCents(json);
            if (cents is null)
                return;
            var sample = _trace.Add(DateTimeOffset.Now, cents.Value);
            if (sample.DayChanged)
                ExtraSpendDayFile.Save(sample.Day, sample.DayCents);
            if (sample.Jumped)
                Dispatcher.UIThread.Post(OnJump);
            else
                Dispatcher.UIThread.Post(Redraw);
        }
        catch (Exception)
        {
            // A bad reading must not break the quota update.
        }
    }

    /// <summary>ExtraSpend. Today's on-demand charges from the usage list.</summary>
    public void ApplyCharges(IReadOnlyList<ExtraCharge> charges, DateTimeOffset? coveredFrom = null)
    {
        if (_disposed)
            return;
        if (_trace.ApplyCharges(charges, DateTimeOffset.Now, coveredFrom))
            Dispatcher.UIThread.Post(OnJump);
        else
            Dispatcher.UIThread.Post(Redraw);
    }

    private void OnJump()
    {
        if (_disposed)
            return;
        // A minimized graph does not come back inside the same stretch of spending.
        if (!_quiet.Jumped(DateTimeOffset.Now, KeepFor()))
            return;
        if (_window is { IsVisible: true })
        {
            Redraw();
            return;
        }

        _openedByJump = true;
        _closeSummary();
        Open(activate: false);
    }

    private void Open(bool activate)
    {
        var window = Ensure();
        var place = _anchor();
        window.ShowReading(Current(), CeilingCents());
        // On Windows the bars can move (they float up when the apps island takes their room): follow them.
        window.ShowNear(place.Anchor, place.Centered, activate, place.Centered ? null : () => _anchor().Anchor);
    }

    private void Redraw()
    {
        if (_window is { IsVisible: true })
            _window.ShowReading(Current(), CeilingCents());
    }

    private void Slide()
    {
        if (_disposed || _window is not { IsVisible: true })
            return;

        var view = Current();
        // A graph a jump opened stays for the time the user chose after the last jump: closing after one
        // quiet minute and opening again at the next charge would start the picture over each time.
        if (_openedByJump && !view.Busy && _quiet.IdleFor(DateTimeOffset.Now) > KeepFor())
        {
            _openedByJump = false;
            _window.CloseQuiet();
            return;
        }

        _window.ShowReading(view, CeilingCents());
    }

    private ExtraSpendView Current()
    {
        var settings = SettingsStore.Current;
        return _trace.View(DateTimeOffset.Now, settings.ExtraCeiling * 100, settings.WarnAt, settings.HotAt);
    }

    private static int CeilingCents() => SettingsStore.Current.ExtraCeiling * 100;

    /// <summary>The user's "time with the graph open": the quiet period that ends a stretch of spending.</summary>
    private static TimeSpan KeepFor() => TimeSpan.FromMinutes(SettingsStore.Current.ExtraKeepMinutes);

    private SpendWindow Ensure()
    {
        if (_window is not null)
            return _window;

        var window = new SpendWindow();
        window.Closed += (_, _) =>
        {
            if (!ReferenceEquals(_window, window))
                return;
            _window = null;
            _openedByJump = false;
            if (window.WasMinimized)
                _quiet.Minimize();
            if (!window.WasQuiet)
                _onUserDismissed();
        };
        _window = window;
        return window;
    }

    private async Task PollAsync()
    {
        if (_polling || _disposed)
            return;
        _polling = true;
        try
        {
            await _poll().ConfigureAwait(true);
        }
        catch (Exception)
        {
            // The graph keeps the last reading. The quota refresh reports a dead session.
        }
        finally
        {
            _polling = false;
        }
    }
}
