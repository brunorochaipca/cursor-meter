namespace CursorMeter.App;

/// <summary>
/// What every surface shows: the bars, the flyout and the tray tooltip.
/// Without quotas, the bars show <see cref="Status"/> (or the detail when there is no status).
/// </summary>
internal sealed record MeterState(string? Status, string Detail, string? Secondary, UsageQuotas? Quotas = null)
{
    public static MeterState Loading { get; } = new(
        "A ler…",
        "A ler a utilização…",
        "A sessão vem do Cursor. Não é preciso cookie.");
}

/// <summary>
/// A row shared by the tray menu and the widget's right-click menu. Null rows are separators.
/// </summary>
internal sealed record MenuEntry(string Label, Action Run, Func<bool>? IsChecked = null);
