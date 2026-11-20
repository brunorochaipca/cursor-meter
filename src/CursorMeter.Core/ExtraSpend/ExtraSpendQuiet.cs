namespace CursorMeter;

/// <summary>
/// ExtraSpend. Decides when a consumption may open the graph by itself. A stretch of spending ends
/// when nothing was spent for the time the user chose. If the user minimized the graph, the same
/// stretch does not open it again: only spending that starts after a quiet period of that length does.
/// </summary>
public sealed class ExtraSpendQuiet
{
    private DateTimeOffset? _lastJump;
    private bool _minimized;

    public bool Minimized => _minimized;

    /// <summary>The user dismissed the graph by hand.</summary>
    public void Minimize() => _minimized = true;

    /// <summary>The user asked to see the graph: nothing is held back any more.</summary>
    public void Reopen() => _minimized = false;

    /// <summary>
    /// A consumption showed up. True when it may open the graph: the graph was not minimized, or it was
    /// and the spending had been quiet for <paramref name="keep"/> before this one.
    /// </summary>
    public bool Jumped(DateTimeOffset now, TimeSpan keep)
    {
        bool quiet = IdleFor(now) > keep;
        _lastJump = now;
        if (!_minimized)
            return true;
        if (!quiet)
            return false;
        _minimized = false;
        return true;
    }

    /// <summary>How long since the last consumption. Very long when there was none.</summary>
    public TimeSpan IdleFor(DateTimeOffset now) => _lastJump is { } last ? now - last : TimeSpan.MaxValue;
}
