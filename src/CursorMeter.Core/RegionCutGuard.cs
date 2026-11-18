namespace CursorMeter;

/// <summary>
/// Stops the bars from fighting another program over the taskbar's window region.
/// While the taskbar is raised, the bars cut their slot out of it. Start11 clears the region of the
/// taskbar whenever it is raised, which undoes that cut. Cutting again on every placement then
/// starts a loop: each cut repaints the taskbar, which raises more events, and the bars flicker.
/// After a few losses in a row the cut is given up, and the bars hide until the taskbar is back on the desktop.
/// </summary>
public sealed class RegionCutGuard
{
    /// <summary>Consecutive times the cut may be found undone before it is given up.</summary>
    public const int MaxLosses = 3;

    private int _losses;

    /// <summary>False once the cut has been undone <see cref="MaxLosses"/> times in a row.</summary>
    public bool MayCut => _losses < MaxLosses;

    /// <summary>
    /// Reports what a placement found while this app's cut was open: whether the taskbar still has
    /// the region that was set. An intact cut clears the count, so one passing reset is forgiven.
    /// </summary>
    public void Report(bool intact)
    {
        if (!MayCut)
            return;
        _losses = intact ? 0 : _losses + 1;
    }

    /// <summary>Forgets the losses. Called when the taskbar is back on the desktop.</summary>
    public void Reset() => _losses = 0;
}
