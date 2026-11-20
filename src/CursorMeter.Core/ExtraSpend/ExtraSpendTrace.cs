namespace CursorMeter;

/// <summary>ExtraSpend. How close today's extra spend is to the ceiling the user set.</summary>
public enum ExtraSpendTone
{
    Normal,
    Warn,
    Hot,
}

/// <summary>ExtraSpend. One corner of the minute chart. Seconds run from 0 (a minute ago) to 60 (now).</summary>
public readonly record struct SpendPoint(double Seconds, double Dollars);

/// <summary>ExtraSpend. What the minute window shows right now.</summary>
public readonly record struct ExtraSpendView(
    bool HasReading,
    int LatestCents,
    int DayStartCents,
    int MinuteCents,
    ExtraSpendTone Tone,
    bool Busy,
    IReadOnlyList<SpendPoint> Points,
    bool IsDay = false,
    int? CycleCents = null)
{
    public static ExtraSpendView Empty { get; } = new(false, 0, 0, 0, ExtraSpendTone.Normal, false, Array.Empty<SpendPoint>());

    /// <summary>Extra spend of the day so far, in cents.</summary>
    public int SpentTodayCents => IsDay ? LatestCents : Math.Max(0, LatestCents - DayStartCents);

    /// <summary>
    /// The extra total that the ceiling is compared with: the billing cycle's, as the usage reading gives it.
    /// Without that reading, only today's charges are known and this is today's.
    /// </summary>
    public int TotalCents => CycleCents ?? LatestCents;

    /// <summary>What is left of the ceiling (ceiling minus the total). Negative when passed, null without a ceiling.</summary>
    public int? RemainingCents(int ceilingCents) => ceilingCents > 0 ? ceilingCents - TotalCents : null;
}

/// <summary>ExtraSpend. Result of accepting one reading of the cumulative extra total.</summary>
public readonly record struct ExtraSpendSample(bool Jumped, bool DayChanged, DateOnly Day, int DayCents);

/// <summary>
/// ExtraSpend. Turns the cumulative extra total into a one-minute step and the day's colour.
/// The line starts at zero: its height is the money spent inside the minute, not the day's total.
/// Samples stay in memory for a few minutes. Only the day mark is worth keeping.
/// </summary>
public sealed class ExtraSpendTrace
{
    public const int WindowSeconds = 60;

    private readonly object _gate = new();
    private readonly List<Sample> _samples = new();
    private List<ShownCharge> _charges = new();
    private DateOnly? _day;
    private int _dayCents;
    private int _latest;
    private int _chargeTotal;
    private bool _has;
    private bool _hasCharges;

    public ExtraSpendTrace()
    {
    }

    /// <param name="day">The local day the saved mark belongs to.</param>
    /// <param name="dayCents">The extra total at the start of that day.</param>
    public ExtraSpendTrace(DateOnly day, int dayCents)
    {
        _day = day;
        _dayCents = Math.Max(0, dayCents);
    }

    /// <summary>Accepts one cumulative reading. A rise is a jump. A new day, or a total that fell, starts the day again.</summary>
    public ExtraSpendSample Add(DateTimeOffset at, int cents)
    {
        lock (_gate)
        {
            var date = DateOnly.FromDateTime(at.ToLocalTime().DateTime);
            bool reset = _day is null || date != _day || (_has && cents < _latest);
            bool jumped = _has && !reset && cents > _latest;
            var mark = reset || _day is null ? date : _day.Value;
            if (reset)
            {
                _samples.Clear();
                _dayCents = cents;
            }

            _day = mark;
            _has = true;
            _latest = cents;
            _samples.Add(new Sample(at, cents));
            ForgetOld(at);
            return new ExtraSpendSample(jumped, reset, mark, _dayCents);
        }
    }

    /// <summary>
    /// Replaces today's on-demand charges from the usage list. True when a charge showed up that the
    /// previous list did not have. The first list is drawn at the charges' own times. A charge that
    /// shows up later is drawn at <paramref name="now"/>, the moment it was learnt: the usage list can
    /// trail the real charge by minutes, which would put the step outside the minute that is shown.
    /// </summary>
    /// <param name="coveredFrom">
    /// Set when the list is only the newest part of the day: charges before this time are kept from the
    /// previous list. Null means the list is the whole day.
    /// </param>
    public bool ApplyCharges(IReadOnlyList<ExtraCharge> charges, DateTimeOffset now, DateTimeOffset? coveredFrom = null)
    {
        lock (_gate)
        {
            // A charge is the same one when its time is the same. If its value grew in the meantime
            // (a request still being billed), only the growth is new: nothing already drawn is redrawn.
            var previous = new Dictionary<long, List<ShownCharge>>();
            var next = new List<ShownCharge>(charges.Count);
            foreach (var shown in _charges)
            {
                if (coveredFrom is { } from && shown.At < from)
                {
                    next.Add(shown);
                    continue;
                }

                long ms = shown.At.ToUnixTimeMilliseconds();
                if (!previous.TryGetValue(ms, out var parts))
                    previous[ms] = parts = new List<ShownCharge>();
                parts.Add(shown);
            }

            var current = new Dictionary<long, int>();
            foreach (var charge in charges)
            {
                long ms = charge.At.ToUnixTimeMilliseconds();
                current[ms] = current.GetValueOrDefault(ms) + charge.Cents;
            }

            bool jumped = false;
            foreach (var (ms, cents) in current)
            {
                var at = DateTimeOffset.FromUnixTimeMilliseconds(ms);
                previous.TryGetValue(ms, out var parts);
                int had = parts?.Sum(static p => p.Cents) ?? 0;
                if (parts is null)
                {
                    DateTimeOffset drawn;
                    if (_hasCharges)
                    {
                        drawn = now;
                        jumped = true;
                    }
                    else
                    {
                        drawn = at > now ? now : at;
                    }

                    next.Add(new ShownCharge(at, cents, drawn));
                    continue;
                }

                if (cents < had)
                {
                    // Lower than before: trim from the newest part, nothing is drawn as new.
                    int cut = had - cents;
                    for (int i = parts.Count - 1; i >= 0 && cut > 0; i--)
                    {
                        int take = Math.Min(cut, parts[i].Cents);
                        parts[i] = parts[i] with { Cents = parts[i].Cents - take };
                        cut -= take;
                    }
                }

                next.AddRange(parts.Where(static p => p.Cents > 0));
                if (cents > had)
                {
                    next.Add(new ShownCharge(at, cents - had, now));
                    jumped = true;
                }
            }

            next.Sort(static (a, b) => a.DrawnAt.CompareTo(b.DrawnAt));
            _charges = next;
            _chargeTotal = next.Sum(static c => c.Cents);
            _hasCharges = true;
            return jumped;
        }
    }

    public ExtraSpendView View(DateTimeOffset now, int ceilingCents, int warnAt, int hotAt)
    {
        lock (_gate)
        {
            if (_hasCharges)
                return ChargesView(now, ceilingCents, warnAt, hotAt);
            if (!_has || _day is null)
                return ExtraSpendView.Empty;

            var start = now - TimeSpan.FromSeconds(WindowSeconds);
            int baseline = _latest;
            bool hadBaseline = false;
            foreach (var sample in _samples)
            {
                if (sample.At <= start)
                {
                    baseline = sample.Cents;
                    hadBaseline = true;
                }
            }

            if (!hadBaseline && _samples.Count > 0)
                baseline = _samples[0].Cents;

            var points = new List<SpendPoint> { new(0, 0) };
            double level = 0;
            foreach (var sample in _samples)
            {
                if (sample.At <= start || sample.At > now)
                    continue;
                double x = (sample.At - start).TotalSeconds;
                double next = Math.Max(0, (sample.Cents - baseline) / 100.0);
                if (next > level + 0.0000001)
                {
                    points.Add(new SpendPoint(x, level));
                    points.Add(new SpendPoint(x, next));
                    level = next;
                }
            }

            double end = Math.Clamp((now - start).TotalSeconds, 0, WindowSeconds);
            var last = points[^1];
            if (Math.Abs(last.Seconds - end) > 0.001 || Math.Abs(last.Dollars - level) > 0.0001)
                points.Add(new SpendPoint(end, level));

            return new ExtraSpendView(
                true,
                _latest,
                _dayCents,
                (int)Math.Round(level * 100),
                ToneOf(_latest, _dayCents, ceilingCents, warnAt, hotAt),
                level > 0.0001,
                points);
        }
    }

    private ExtraSpendView ChargesView(DateTimeOffset now, int ceilingCents, int warnAt, int hotAt)
    {
        var start = now - TimeSpan.FromSeconds(WindowSeconds);
        var points = new List<SpendPoint> { new(0, 0) };
        double level = 0;
        foreach (var charge in _charges)
        {
            if (charge.DrawnAt <= start || charge.DrawnAt > now)
                continue;
            double x = (charge.DrawnAt - start).TotalSeconds;
            double next = level + charge.Cents / 100.0;
            points.Add(new SpendPoint(x, level));
            points.Add(new SpendPoint(x, next));
            level = next;
        }

        double end = Math.Clamp((now - start).TotalSeconds, 0, WindowSeconds);
        var last = points[^1];
        if (Math.Abs(last.Seconds - end) > 0.001 || Math.Abs(last.Dollars - level) > 0.0001)
            points.Add(new SpendPoint(end, level));

        return new ExtraSpendView(
            true,
            _chargeTotal,
            0,
            (int)Math.Round(level * 100),
            // With the cycle's reading the ceiling is judged on the cycle total. Without it, on today's charges alone.
            _has && _day is not null
                ? ToneOf(_latest, _dayCents, ceilingCents, warnAt, hotAt)
                : ToneOf(_chargeTotal, 0, ceilingCents, warnAt, hotAt),
            level > 0.0001,
            points,
            IsDay: true,
            CycleCents: _has && _day is not null ? _latest : null);
    }

    /// <summary>
    /// Colour of the day. The ceiling is the absolute extra total, in cents.
    /// Zero ceiling stays normal. The percent is today's spend against the room between the day start and the ceiling.
    /// </summary>
    public static ExtraSpendTone ToneOf(int latestCents, int dayStartCents, int ceilingCents, int warnAt, int hotAt)
    {
        if (ceilingCents <= 0)
            return ExtraSpendTone.Normal;
        int spent = latestCents - dayStartCents;
        int room = ceilingCents - dayStartCents;
        if (room <= 0)
            return latestCents >= ceilingCents ? ExtraSpendTone.Hot : ExtraSpendTone.Normal;
        if (spent <= 0)
            return ExtraSpendTone.Normal;
        double percent = spent * 100.0 / room;
        if (percent >= hotAt)
            return ExtraSpendTone.Hot;
        if (percent >= warnAt)
            return ExtraSpendTone.Warn;
        return ExtraSpendTone.Normal;
    }

    /// <summary>Drops old samples but keeps the newest one from before the retention window, so a late jump still has a floor.</summary>
    private void ForgetOld(DateTimeOffset at)
    {
        var keep = at.AddMinutes(-3);
        int anchor = -1;
        for (int i = 0; i < _samples.Count; i++)
        {
            if (_samples[i].At < keep)
                anchor = i;
        }

        if (anchor > 0)
            _samples.RemoveRange(0, anchor);
    }

    private readonly record struct Sample(DateTimeOffset At, int Cents);

    private readonly record struct ShownCharge(DateTimeOffset At, int Cents, DateTimeOffset DrawnAt);
}
