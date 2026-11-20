namespace CursorMeter;

/// <summary>
/// Start11 dips its taskbar to almost invisible for a moment, for example while an animation starts or the
/// pointer leaves and returns, and shows it again within a tenth of a second. Moving the bars away for that
/// long makes them flash. A taskbar counts as hidden for the bars only once it has stayed hidden for
/// <see cref="DelayMs"/>. Once the bars are placed as for a hidden taskbar they follow it at once, in both
/// directions, so the delay only holds back going from docked to floating.
/// </summary>
public sealed class HideDebounce
{
    /// <summary>How long the taskbar must stay hidden before the bars leave it.</summary>
    public const int DelayMs = 250;

    private long? _hiddenSince;

    /// <summary>
    /// True when the bars should be placed as for a hidden taskbar.
    /// </summary>
    /// <param name="hidden">Whether the taskbar looks hidden right now.</param>
    /// <param name="placedHidden">Whether the bars are already placed as for a hidden taskbar.</param>
    /// <param name="nowMs">A monotonic clock, in milliseconds.</param>
    public bool Confirm(bool hidden, bool placedHidden, long nowMs)
    {
        if (!hidden)
        {
            _hiddenSince = null;
            return false;
        }

        _hiddenSince ??= nowMs;
        return placedHidden || nowMs - _hiddenSince.Value >= DelayMs;
    }

    /// <summary>Forgets that the taskbar was seen hidden.</summary>
    public void Reset() => _hiddenSince = null;
}
