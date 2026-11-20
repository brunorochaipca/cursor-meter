using Xunit;

namespace CursorMeter.Tests;

public sealed class ExtraSpendTests
{
    [Fact]
    public void Missing_spend_is_not_a_reading()
    {
        Assert.Null(ExtraSpendParser.ReadCents("{\"planUsage\":{\"totalSpend\":999,\"autoPercentUsed\":1,\"apiPercentUsed\":2}}"));
        Assert.Null(ExtraSpendParser.ReadCents("not json"));
        Assert.Null(ExtraSpendParser.ReadCents(""));
    }

    [Fact]
    public void A_limit_without_an_amount_is_zero_and_the_personal_total_wins()
    {
        Assert.Equal(0, ExtraSpendParser.ReadCents("{\"spendLimitUsage\":{\"limitType\":\"user\"}}"));
        Assert.Equal(1200, ExtraSpendParser.ReadCents("{\"spendLimitUsage\":{\"individualUsed\":1200,\"pooledUsed\":999}}"));
        Assert.Equal(800, ExtraSpendParser.ReadCents("{\"spendLimitUsage\":{\"pooledUsed\":800}}"));
        Assert.Equal(0, ExtraSpendParser.ReadCents("{\"spendLimitUsage\":{\"individualUsed\":0,\"pooledUsed\":400}}"));
        Assert.Equal(1500, ExtraSpendParser.ReadCents("{\"spendLimitUsage\":{\"individualUsed\":\"1500\"}}"));
    }

    [Fact]
    public void A_jump_inside_the_minute_is_a_step_up_from_zero()
    {
        var now = new DateTimeOffset(2026, 11, 20, 15, 57, 0, TimeSpan.Zero);
        var trace = new ExtraSpendTrace();
        trace.Add(now.AddSeconds(-90), 1100);
        Assert.True(trace.Add(now.AddSeconds(-20), 1200).Jumped);

        var view = trace.View(now, ceilingCents: 1500, warnAt: 70, hotAt: 90);

        Assert.Equal(100, view.MinuteCents);
        Assert.True(view.Busy);
        Assert.Equal(ExtraSpendTone.Normal, view.Tone);
        Assert.Equal(new[] { 0d, 40, 40, 60 }, view.Points.Select(p => p.Seconds).ToArray(), new DoubleTolerance(0.01));
        Assert.Equal(new[] { 0d, 0, 1, 1 }, view.Points.Select(p => p.Dollars).ToArray(), new DoubleTolerance(0.001));
    }

    [Fact]
    public void A_jump_after_a_long_gap_still_rises_at_now()
    {
        var now = DateTimeOffset.Now;
        var trace = new ExtraSpendTrace();
        trace.Add(now.AddMinutes(-10), 1000);
        Assert.True(trace.Add(now, 1250).Jumped);

        var view = trace.View(now, 0, 70, 90);

        Assert.Equal(250, view.MinuteCents);
        Assert.True(view.Busy);
        Assert.Equal(2.5, view.Points[^1].Dollars, 3);
        Assert.Equal(0, view.Points[0].Dollars, 3);
    }

    [Fact]
    public void The_colour_follows_the_room_between_the_morning_and_the_ceiling()
    {
        var now = DateTimeOffset.Now;
        var trace = new ExtraSpendTrace();
        trace.Add(now, 1000);

        Assert.Equal(ExtraSpendTone.Normal, trace.View(now, 1500, 70, 90).Tone);
        trace.Add(now.AddSeconds(1), 1200);
        Assert.Equal(ExtraSpendTone.Normal, trace.View(now.AddSeconds(1), 1500, 70, 90).Tone);
        trace.Add(now.AddSeconds(2), 1350);
        Assert.Equal(ExtraSpendTone.Warn, trace.View(now.AddSeconds(2), 1500, 70, 90).Tone);
        trace.Add(now.AddSeconds(3), 1450);
        Assert.Equal(ExtraSpendTone.Hot, trace.View(now.AddSeconds(3), 1500, 70, 90).Tone);
        Assert.Equal(ExtraSpendTone.Normal, trace.View(now.AddSeconds(3), 0, 70, 90).Tone);
    }

    [Fact]
    public void The_remaining_is_the_ceiling_minus_the_cycle_total_even_when_the_day_list_is_used()
    {
        var now = DateTimeOffset.Now;
        var trace = new ExtraSpendTrace();
        trace.Add(now.AddMinutes(-5), 6624); // the cycle total when the day began
        trace.Add(now.AddMinutes(-1), 6900); // and now
        trace.ApplyCharges([new ExtraCharge(now.AddMinutes(-30), 276)], now);

        var view = trace.View(now, 7500, 70, 90);

        Assert.Equal(6900, view.TotalCents);
        Assert.Equal(600, view.RemainingCents(7500)); // 75.00 - 69.00
        Assert.Equal(276, view.SpentTodayCents);
        // 276 of the 876 cents between the morning and the ceiling: not yet a warning.
        Assert.Equal(ExtraSpendTone.Normal, view.Tone);
        // A ceiling of 69.50 $ leaves 3.26 $ of room and 2.76 $ of it is used: 85 %.
        Assert.Equal(ExtraSpendTone.Warn, trace.View(now, 6950, 70, 90).Tone);
        Assert.Equal(ExtraSpendTone.Hot, trace.View(now, 6900, 70, 90).Tone);
    }

    [Fact]
    public void Without_the_cycle_reading_the_remaining_uses_today_s_charges_and_falls_with_each_charge()
    {
        var now = DateTimeOffset.Now;
        var trace = new ExtraSpendTrace();
        var morning = new ExtraCharge(now.AddHours(-3), 200);
        trace.ApplyCharges([morning], now);
        Assert.Equal(1300, trace.View(now, 1500, 70, 90).RemainingCents(1500));

        trace.ApplyCharges([morning, new ExtraCharge(now.AddSeconds(-1), 15)], now);
        var view = trace.View(now, 1500, 70, 90);
        Assert.Equal(215, view.SpentTodayCents);
        Assert.Equal(1285, view.RemainingCents(1500));

        trace.ApplyCharges([morning, new ExtraCharge(now.AddSeconds(-1), 15), new ExtraCharge(now, 1400)], now);
        Assert.Equal(-115, trace.View(now, 1500, 70, 90).RemainingCents(1500));
        Assert.Equal(ExtraSpendTone.Hot, trace.View(now, 1500, 70, 90).Tone);
        Assert.Null(trace.View(now, 0, 70, 90).RemainingCents(0));
    }

    [Fact]
    public void The_remaining_of_a_cumulative_reading_is_the_ceiling_minus_the_total()
    {
        var now = DateTimeOffset.Now;
        var trace = new ExtraSpendTrace();
        trace.Add(now, 6624);
        trace.Add(now.AddSeconds(1), 6724);

        Assert.Equal(776, trace.View(now.AddSeconds(1), 7500, 70, 90).RemainingCents(7500));
    }

    [Fact]
    public void A_saved_morning_mark_stays_when_the_app_opens_later()
    {
        var now = DateTimeOffset.Now;
        var trace = new ExtraSpendTrace(DateOnly.FromDateTime(now.LocalDateTime), 1000);
        var sample = trace.Add(now, 1200);

        Assert.False(sample.DayChanged);
        Assert.Equal(1000, sample.DayCents);
        Assert.False(sample.Jumped);
        var view = trace.View(now, 1500, 70, 90);
        Assert.Equal(1000, view.DayStartCents);
        Assert.Equal(1200, view.LatestCents);
        Assert.Equal(0, view.MinuteCents);
        Assert.Equal(ExtraSpendTone.Normal, view.Tone);
    }

    [Fact]
    public void A_new_day_or_a_lower_total_starts_the_day_again()
    {
        var now = DateTimeOffset.Now;
        var yesterday = new ExtraSpendTrace(DateOnly.FromDateTime(now.AddDays(-1).LocalDateTime), 9000);
        var restarted = yesterday.Add(now, 300);
        Assert.True(restarted.DayChanged);
        Assert.Equal(300, restarted.DayCents);
        Assert.False(restarted.Jumped);

        var trace = new ExtraSpendTrace();
        trace.Add(now, 2000);
        var dropped = trace.Add(now.AddSeconds(1), 100);
        Assert.True(dropped.DayChanged);
        Assert.Equal(100, dropped.DayCents);
        Assert.False(dropped.Jumped);
        Assert.Equal(0, trace.View(now.AddSeconds(1), 1500, 70, 90).MinuteCents);
    }

    [Fact]
    public void A_minute_with_no_new_spend_is_idle()
    {
        var now = DateTimeOffset.Now;
        var trace = new ExtraSpendTrace();
        trace.Add(now.AddMinutes(-3), 1000);
        trace.Add(now.AddMinutes(-2), 1400);

        var view = trace.View(now, 1500, 70, 90);

        Assert.False(view.Busy);
        Assert.Equal(0, view.MinuteCents);
        Assert.Equal(ExtraSpendTone.Warn, view.Tone);
    }

    [Fact]
    public void The_day_mark_is_one_line()
    {
        var day = new DateOnly(2026, 11, 20);
        Assert.Equal("2026-11-20 1000", ExtraSpendDay.Format(day, 1000));
        Assert.Equal((day, 1000), ExtraSpendDay.TryRead("2026-11-20 1000"));
        Assert.Null(ExtraSpendDay.TryRead("tomorrow 10"));
        Assert.Null(ExtraSpendDay.TryRead("2026-11-20 -1"));
        Assert.Null(ExtraSpendDay.TryRead(null));
    }

    [Fact]
    public void Included_rows_are_not_extra_and_on_demand_cost_is()
    {
        const string json = """
            {
              "usageEventsDisplay": [
                {"timestamp":"1780402687672","kind":"USAGE_EVENT_KIND_INCLUDED_IN_ULTRA","usageBasedCosts":"-","chargedCents":8},
                {"timestamp":"1780402700000","kind":"USAGE_EVENT_KIND_USAGE_BASED","usageBasedCosts":"$1.25"},
                {"timestamp":"1780402800000","kind":"USAGE_EVENT_KIND_ON_DEMAND","usageBasedCosts":"-","chargedCents":40},
                {"timestamp":"1780402900000","kind":"USAGE_EVENT_KIND_FREE","chargedCents":15}
              ]
            }
            """;

        var charges = ExtraSpendEvents.ReadCharges(json);

        Assert.NotNull(charges);
        Assert.Equal(2, charges.Count);
        Assert.Equal(125, charges[0].Cents);
        Assert.Equal(40, charges[1].Cents);
        Assert.Null(ExtraSpendEvents.ReadCharges("{\"planUsage\":{}}"));
    }

    [Fact]
    public void Today_charges_are_the_total_and_only_the_last_minute_steps()
    {
        var now = DateTimeOffset.Now;
        var trace = new ExtraSpendTrace();
        Assert.False(trace.ApplyCharges([
            new ExtraCharge(now.AddMinutes(-10), 200),
            new ExtraCharge(now.AddSeconds(-20), 125),
        ], now));
        Assert.True(trace.ApplyCharges([
            new ExtraCharge(now.AddMinutes(-10), 200),
            new ExtraCharge(now.AddSeconds(-20), 125),
            new ExtraCharge(now.AddSeconds(-5), 40),
        ], now));

        var view = trace.View(now, 1500, 70, 90);

        Assert.True(view.IsDay);
        Assert.Equal(365, view.LatestCents);
        Assert.Equal(165, view.MinuteCents);
        Assert.True(view.Busy);
        Assert.Equal(ExtraSpendTone.Normal, view.Tone);
    }

    [Fact]
    public void A_charge_the_list_reports_late_is_drawn_when_it_is_learnt()
    {
        var now = DateTimeOffset.Now;
        var trace = new ExtraSpendTrace();
        var old = new ExtraCharge(now.AddMinutes(-30), 200);
        trace.ApplyCharges([old], now.AddSeconds(-30));

        // The usage list only shows this one now, though it happened five minutes ago.
        Assert.True(trace.ApplyCharges([old, new ExtraCharge(now.AddMinutes(-5), 15)], now));

        var view = trace.View(now, 1500, 70, 90);

        Assert.Equal(15, view.MinuteCents);
        Assert.True(view.Busy);
        Assert.Equal(0.15, view.Points[^1].Dollars, 3);
        // It stays on screen until its own minute is over, and then it is gone.
        Assert.False(trace.View(now.AddSeconds(61), 1500, 70, 90).Busy);
        // The same list again is not a new jump.
        Assert.False(trace.ApplyCharges([old, new ExtraCharge(now.AddMinutes(-5), 15)], now.AddSeconds(10)));
    }

    [Fact]
    public void A_charge_whose_value_grows_adds_only_the_growth_and_nothing_is_redrawn()
    {
        var now = DateTimeOffset.Now;
        var trace = new ExtraSpendTrace();
        var at = now.AddSeconds(-30);
        trace.ApplyCharges([new ExtraCharge(at, 3)], now.AddSeconds(-25));

        // The same request, now billed higher: 3 -> 15 cents.
        Assert.True(trace.ApplyCharges([new ExtraCharge(at, 15)], now));

        var view = trace.View(now, 1500, 70, 90);

        Assert.Equal(15, view.LatestCents);
        // The first 3 cents stay where they were drawn (30 s ago), the 12 new ones are at the right edge.
        Assert.Equal(new[] { 0d, 0, 0.03, 0.03, 0.15 }, view.Points.Select(p => p.Dollars).ToArray(), new DoubleTolerance(0.001));
        Assert.Equal(30, view.Points[1].Seconds, 0.5);
        Assert.Equal(60, view.Points[^1].Seconds, 0.5);
        // The same list again changes nothing.
        Assert.False(trace.ApplyCharges([new ExtraCharge(at, 15)], now.AddSeconds(10)));
    }

    [Fact]
    public void A_list_of_only_the_newest_rows_keeps_the_older_charges()
    {
        var now = DateTimeOffset.Now;
        var trace = new ExtraSpendTrace();
        var morning = new ExtraCharge(now.AddHours(-3), 200);
        var noon = new ExtraCharge(now.AddHours(-1), 100);
        trace.ApplyCharges([morning, noon], now);

        var recent = new ExtraCharge(now.AddSeconds(-4), 7);
        Assert.True(trace.ApplyCharges([recent], now, coveredFrom: now.AddMinutes(-10)));

        Assert.Equal(307, trace.View(now, 1500, 70, 90).LatestCents);
        Assert.False(trace.ApplyCharges([recent], now.AddSeconds(10), coveredFrom: now.AddMinutes(-9)));
        Assert.Equal(307, trace.View(now, 1500, 70, 90).LatestCents);
    }

    [Fact]
    public void A_page_is_full_by_its_rows_not_by_its_charges()
    {
        var rows = string.Join(",", Enumerable.Range(0, 100).Select(i =>
            $"{{\"timestamp\":\"{1780402000000L + i * 1000}\",\"kind\":\"USAGE_EVENT_KIND_INCLUDED_IN_ULTRA\",\"usageBasedCosts\":\"-\"}}"));
        var json = $"{{\"usageEventsDisplay\":[{rows},{{\"timestamp\":\"1780401000000\",\"usageBasedCosts\":\"$0.03\"}}]}}";

        var page = ExtraSpendEvents.ReadPage(json);

        Assert.NotNull(page);
        Assert.Equal(101, page.Value.Rows);
        Assert.Single(page.Value.Charges);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1780401000000L), page.Value.Oldest);
        Assert.Null(ExtraSpendEvents.ReadPage("{\"planUsage\":{}}"));
    }

    [Fact]
    public void A_charge_stamped_in_the_future_never_hides_the_graph()
    {
        var now = DateTimeOffset.Now;
        var trace = new ExtraSpendTrace();
        trace.ApplyCharges([new ExtraCharge(now.AddSeconds(-40), 100)], now);
        trace.ApplyCharges([new ExtraCharge(now.AddSeconds(-40), 100), new ExtraCharge(now.AddSeconds(8), 3)], now);

        var view = trace.View(now, 0, 70, 90);

        Assert.Equal(103, view.MinuteCents);
        Assert.True(view.Busy);
    }

    private sealed class DoubleTolerance : IEqualityComparer<double>
    {
        private readonly double _within;

        public DoubleTolerance(double within) => _within = within;

        public bool Equals(double x, double y) => Math.Abs(x - y) <= _within;

        public int GetHashCode(double obj) => 0;
    }
}
