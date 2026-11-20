namespace CursorMeter;

/// <summary>
/// The path the bars take when they move between the taskbar and the floating place (and back):
/// a short glide that starts fast and settles, instead of a jump.
/// </summary>
public static class SlotGlide
{
    /// <summary>How long the glide lasts.</summary>
    public const int DurationMs = 200;

    /// <summary>Ease-out cubic: 0 at the start, 1 at the end, quick at first and slow at the end.</summary>
    public static double Ease(double progress)
    {
        double t = Math.Clamp(progress, 0, 1);
        double inverse = 1 - t;
        return 1 - inverse * inverse * inverse;
    }

    /// <summary>The rectangle after <paramref name="elapsedMs"/>; exactly <paramref name="to"/> once the time is up.</summary>
    public static PxRect At(PxRect from, PxRect to, long elapsedMs)
    {
        if (elapsedMs >= DurationMs)
            return to;
        double e = Ease((double)Math.Max(elapsedMs, 0) / DurationMs);
        return new PxRect(
            Mix(from.Left, to.Left, e),
            Mix(from.Top, to.Top, e),
            Mix(from.Right, to.Right, e),
            Mix(from.Bottom, to.Bottom, e));
    }

    private static int Mix(int a, int b, double e) => (int)Math.Round(a + (b - a) * e);
}
