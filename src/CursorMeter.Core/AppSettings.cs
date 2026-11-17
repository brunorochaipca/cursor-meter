using System.Globalization;
using System.Text;

namespace CursorMeter;

/// <summary>The look of the bars when they are not an island of the Start11 taskbar.</summary>
public enum BarTheme
{
    /// <summary>Dark in the plain box, the Start11 look while floating: what 1.2.7 did.</summary>
    Auto,

    /// <summary>The dark box.</summary>
    Dark,

    /// <summary>No fill at all: only the text and the bars over what is behind.</summary>
    Transparent,

    /// <summary>As part of the Start11 taskbar: no box in the plain bar, the island look while floating.</summary>
    Start11,
}

/// <summary>Which pools the bars show.</summary>
public enum PoolView
{
    Both,
    CursorOnly,
    OtherOnly,
}

/// <summary>
/// What the user chose in the settings panel. Kept as <c>key=value</c> lines in <c>settings.txt</c>;
/// a missing or invalid line falls back to the default, so a hand-edited file never breaks the app.
/// Colours are <c>#RRGGBB</c>, and <c>null</c> means the app's own colour.
/// </summary>
public sealed record AppSettings
{
    public const int DefaultWarnAt = 70;
    public const int DefaultHotAt = 90;

    public BarTheme Theme { get; init; } = BarTheme.Auto;

    /// <summary>Opacity of the fill behind the bars, in percent. Ignored by the transparent theme.</summary>
    public int BackgroundOpacity { get; init; } = 100;

    public string? TextColor { get; init; }
    public string? MutedColor { get; init; }
    public string? CursorColor { get; init; }
    public string? OtherColor { get; init; }
    public string? WarnColor { get; init; }
    public string? HotColor { get; init; }
    public string? OutlineColor { get; init; }

    /// <summary>Size of the text relative to the default, in percent.</summary>
    public int TextScale { get; init; } = 100;

    public bool Outline { get; init; } = true;

    /// <summary>Corner radius in device-independent pixels; -1 follows the theme.</summary>
    public int Radius { get; init; } = -1;

    public int RefreshMinutes { get; init; } = 3;
    public int WarnAt { get; init; } = DefaultWarnAt;
    public int HotAt { get; init; } = DefaultHotAt;

    /// <summary>Opacity of the floating bars until the pointer is over them, in percent.</summary>
    public int FadedOpacity { get; init; } = 40;

    public PoolView Pools { get; init; } = PoolView.Both;

    /// <summary>Shows what is left of each pool instead of what was used.</summary>
    public bool ShowRemaining { get; init; }

    public static AppSettings Default { get; } = new();

    /// <summary>The same settings with every value brought into its range.</summary>
    public AppSettings Normalize()
    {
        int warn = Math.Clamp(WarnAt, 1, 98);
        return this with
        {
            Theme = Enum.IsDefined(Theme) ? Theme : BarTheme.Auto,
            Pools = Enum.IsDefined(Pools) ? Pools : PoolView.Both,
            BackgroundOpacity = Math.Clamp(BackgroundOpacity, 0, 100),
            TextScale = Math.Clamp(TextScale, 80, 110),
            Radius = Math.Clamp(Radius, -1, 16),
            RefreshMinutes = Math.Clamp(RefreshMinutes, 1, 60),
            WarnAt = warn,
            HotAt = Math.Clamp(HotAt, warn + 1, 100),
            FadedOpacity = Math.Clamp(FadedOpacity, 10, 90),
            TextColor = NormalizeColor(TextColor),
            MutedColor = NormalizeColor(MutedColor),
            CursorColor = NormalizeColor(CursorColor),
            OtherColor = NormalizeColor(OtherColor),
            WarnColor = NormalizeColor(WarnColor),
            HotColor = NormalizeColor(HotColor),
            OutlineColor = NormalizeColor(OutlineColor),
        };
    }

    /// <summary>The colour as <c>#RRGGBB</c> in capitals, or <c>null</c> when it is not a six-digit hex colour.</summary>
    public static string? NormalizeColor(string? text)
    {
        var value = text?.Trim();
        if (value is not { Length: 7 } || value[0] != '#')
            return null;
        for (int i = 1; i < value.Length; i++)
        {
            if (!Uri.IsHexDigit(value[i]))
                return null;
        }

        return value.ToUpperInvariant();
    }

    public static AppSettings Parse(string? text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in (text ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            int split = line.IndexOf('=');
            if (split > 0)
                values[line[..split].Trim()] = line[(split + 1)..].Trim();
        }

        var defaults = Default;
        return new AppSettings
        {
            Theme = Enumeration(values, nameof(Theme), defaults.Theme),
            Pools = Enumeration(values, nameof(Pools), defaults.Pools),
            BackgroundOpacity = Number(values, nameof(BackgroundOpacity), defaults.BackgroundOpacity),
            TextScale = Number(values, nameof(TextScale), defaults.TextScale),
            Radius = Number(values, nameof(Radius), defaults.Radius),
            RefreshMinutes = Number(values, nameof(RefreshMinutes), defaults.RefreshMinutes),
            WarnAt = Number(values, nameof(WarnAt), defaults.WarnAt),
            HotAt = Number(values, nameof(HotAt), defaults.HotAt),
            FadedOpacity = Number(values, nameof(FadedOpacity), defaults.FadedOpacity),
            Outline = Flag(values, nameof(Outline), defaults.Outline),
            ShowRemaining = Flag(values, nameof(ShowRemaining), defaults.ShowRemaining),
            TextColor = Text(values, nameof(TextColor)),
            MutedColor = Text(values, nameof(MutedColor)),
            CursorColor = Text(values, nameof(CursorColor)),
            OtherColor = Text(values, nameof(OtherColor)),
            WarnColor = Text(values, nameof(WarnColor)),
            HotColor = Text(values, nameof(HotColor)),
            OutlineColor = Text(values, nameof(OutlineColor)),
        }.Normalize();
    }

    public string Format()
    {
        var s = Normalize();
        var text = new StringBuilder();
        void Line(string key, object? value)
        {
            if (value is not null)
                text.Append(key).Append('=').Append(Convert.ToString(value, CultureInfo.InvariantCulture)).Append('\n');
        }

        Line(nameof(Theme), s.Theme);
        Line(nameof(BackgroundOpacity), s.BackgroundOpacity);
        Line(nameof(Outline), s.Outline ? 1 : 0);
        Line(nameof(Radius), s.Radius);
        Line(nameof(TextScale), s.TextScale);
        Line(nameof(Pools), s.Pools);
        Line(nameof(ShowRemaining), s.ShowRemaining ? 1 : 0);
        Line(nameof(RefreshMinutes), s.RefreshMinutes);
        Line(nameof(WarnAt), s.WarnAt);
        Line(nameof(HotAt), s.HotAt);
        Line(nameof(FadedOpacity), s.FadedOpacity);
        Line(nameof(TextColor), s.TextColor);
        Line(nameof(MutedColor), s.MutedColor);
        Line(nameof(CursorColor), s.CursorColor);
        Line(nameof(OtherColor), s.OtherColor);
        Line(nameof(WarnColor), s.WarnColor);
        Line(nameof(HotColor), s.HotColor);
        Line(nameof(OutlineColor), s.OutlineColor);
        return text.ToString();
    }

    private static int Number(Dictionary<string, string> values, string key, int fallback)
        => values.TryGetValue(key, out var text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)
            ? number
            : fallback;

    private static bool Flag(Dictionary<string, string> values, string key, bool fallback)
    {
        if (!values.TryGetValue(key, out var text))
            return fallback;
        if (text is "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase))
            return true;
        if (text is "0" || text.Equals("false", StringComparison.OrdinalIgnoreCase))
            return false;
        return fallback;
    }

    private static T Enumeration<T>(Dictionary<string, string> values, string key, T fallback)
        where T : struct, Enum
        => values.TryGetValue(key, out var text) && Enum.TryParse<T>(text, ignoreCase: true, out var value) && Enum.IsDefined(value)
            ? value
            : fallback;

    private static string? Text(Dictionary<string, string> values, string key)
        => values.TryGetValue(key, out var text) ? text : null;
}
