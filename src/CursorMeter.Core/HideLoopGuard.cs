namespace CursorMeter;

/// <summary>What <see cref="HideLoopGuard.Report"/> found.</summary>
public enum HideLoopChange
{
    None,

    /// <summary>The taskbar and the bars were reacting to each other, so the bars now stay as for a hidden taskbar.</summary>
    Latched,

    /// <summary>The bars may dock again.</summary>
    Released,
}

/// <summary>
/// Start11 hides its taskbar when a window overlaps it and shows it again when none does. The docked bars
/// are such a window in some places, so docking hides the taskbar, floating shows it, and so on, many times a
/// second. A burst of changes of the hidden state is that loop. Once it is seen the guard latches, and the
/// bars keep to the placement for a hidden taskbar, the one that does not move them over the taskbar again,
/// until the bars or the taskbar move, the floating settings change, or <see cref="MaxLatchMs"/> has passed.
/// </summary>
public sealed class HideLoopGuard
{
    /// <summary>Longest gap between two changes of the taskbar for them to count as one burst.</summary>
    public const int RapidMs = 1500;

    /// <summary>Changes in one burst after which the loop is taken to be going on.</summary>
    public const int FlipsToLatch = 5;

    /// <summary>How long the guard stays latched when nothing else releases it.</summary>
    public const long MaxLatchMs = 10 * 60 * 1000;

    private bool? _lastHidden;
    private long _lastFlipAt;
    private int _flips;
    private string _key = string.Empty;
    private long _latchedAt;

    public bool Latched { get; private set; }

    /// <summary>
    /// Notes whether the taskbar is hidden now. <paramref name="key"/> stands for where the bars and the
    /// taskbar are, so the guard lets go when either moves. Nothing is counted while the bars are dragged,
    /// as the pointer then moves them over the taskbar on purpose.
    /// </summary>
    public HideLoopChange Report(bool hidden, string key, long nowMs, bool dragging)
    {
        if (Latched)
        {
            if (key != _key || nowMs - _latchedAt > MaxLatchMs)
            {
                Release();
                _lastHidden = hidden;
                return HideLoopChange.Released;
            }

            return HideLoopChange.None;
        }

        if (dragging)
        {
            _lastHidden = hidden;
            return HideLoopChange.None;
        }

        var change = HideLoopChange.None;
        if (_lastHidden is { } before && before != hidden)
        {
            _flips = nowMs - _lastFlipAt <= RapidMs ? _flips + 1 : 1;
            _lastFlipAt = nowMs;
            if (_flips >= FlipsToLatch)
            {
                Latched = true;
                _key = key;
                _latchedAt = nowMs;
                _flips = 0;
                change = HideLoopChange.Latched;
            }
        }

        _lastHidden = hidden;
        return change;
    }

    /// <summary>Forgets the burst and lets the bars dock again.</summary>
    public void Release()
    {
        Latched = false;
        _flips = 0;
    }
}
