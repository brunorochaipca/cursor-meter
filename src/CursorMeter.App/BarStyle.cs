using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace CursorMeter.App;

/// <summary>The colours, text size and thresholds the bars draw with, resolved from the settings.</summary>
internal sealed class BarStyle
{
    public const double BaseTextSize = 32d / 3;

    public static BarStyle Default { get; } = From(AppSettings.Default);

    public required IBrush Text { get; init; }
    public required IBrush Muted { get; init; }
    public required IBrush CursorPool { get; init; }
    public required IBrush OtherPool { get; init; }
    public required IBrush Warn { get; init; }
    public required IBrush Hot { get; init; }
    public double TextSize { get; init; } = BaseTextSize;
    public double WarnAt { get; init; } = AppSettings.DefaultWarnAt;
    public double HotAt { get; init; } = AppSettings.DefaultHotAt;
    public PoolView Pools { get; init; }
    public bool ShowRemaining { get; init; }

    public static BarStyle From(AppSettings settings) => new()
    {
        Text = Pick(settings.TextColor, Palette.Text),
        Muted = Pick(settings.MutedColor, Palette.Muted),
        CursorPool = Pick(settings.CursorColor, Palette.CursorPool),
        OtherPool = Pick(settings.OtherColor, Palette.OtherPool),
        Warn = Pick(settings.WarnColor, Palette.Warn),
        Hot = Pick(settings.HotColor, Palette.Hot),
        TextSize = BaseTextSize * settings.TextScale / 100d,
        WarnAt = settings.WarnAt,
        HotAt = settings.HotAt,
        Pools = settings.Pools,
        ShowRemaining = settings.ShowRemaining,
    };

    /// <summary>The colour written as <c>#RRGGBB</c>, or <paramref name="fallback"/> when there is none.</summary>
    public static IBrush Pick(string? hex, IBrush fallback)
        => hex is not null && Color.TryParse(hex, out var color) ? new ImmutableSolidColorBrush(color) : fallback;

    public static Color? ColorOf(string? hex) => hex is not null && Color.TryParse(hex, out var color) ? color : null;
}
