using Xunit;

namespace CursorMeter.Tests;

public sealed class HideLoopGuardTests
{
    private const string Where = "0 0,664 1366x104";

    /// <summary>Feeds alternating hidden and shown states, <paramref name="gap"/> ms apart.</summary>
    private static HideLoopChange Flip(HideLoopGuard guard, int times, long gap, ref long now, bool startHidden = true)
    {
        var last = HideLoopChange.None;
        bool hidden = startHidden;
        for (int i = 0; i < times; i++)
        {
            now += gap;
            var change = guard.Report(hidden, Where, now, dragging: false);
            if (change != HideLoopChange.None)
                last = change;
            hidden = !hidden;
        }

        return last;
    }

    [Fact]
    public void A_burst_of_changes_latches()
    {
        var guard = new HideLoopGuard();
        long now = 1000;

        var change = Flip(guard, 8, 250, ref now);

        Assert.Equal(HideLoopChange.Latched, change);
        Assert.True(guard.Latched);
    }

    [Fact]
    public void Changes_spread_over_time_do_not_latch()
    {
        var guard = new HideLoopGuard();
        long now = 1000;

        var change = Flip(guard, 20, 4000, ref now);

        Assert.Equal(HideLoopChange.None, change);
        Assert.False(guard.Latched);
    }

    [Fact]
    public void One_hide_and_one_show_do_not_latch()
    {
        var guard = new HideLoopGuard();

        Assert.Equal(HideLoopChange.None, guard.Report(false, Where, 0, false));
        Assert.Equal(HideLoopChange.None, guard.Report(true, Where, 100, false));
        Assert.Equal(HideLoopChange.None, guard.Report(false, Where, 600, false));
        Assert.False(guard.Latched);
    }

    [Fact]
    public void A_state_that_does_not_change_is_not_a_change()
    {
        var guard = new HideLoopGuard();

        for (int i = 0; i < 50; i++)
            Assert.Equal(HideLoopChange.None, guard.Report(true, Where, i * 10, false));

        Assert.False(guard.Latched);
    }

    [Fact]
    public void Nothing_is_counted_while_the_bars_are_dragged()
    {
        var guard = new HideLoopGuard();
        bool hidden = true;
        for (int i = 0; i < 40; i++)
        {
            guard.Report(hidden, Where, 1000 + (i * 30), dragging: true);
            hidden = !hidden;
        }

        Assert.False(guard.Latched);
    }

    [Fact]
    public void Latched_stays_put_while_nothing_moves()
    {
        var guard = new HideLoopGuard();
        long now = 1000;
        Flip(guard, 8, 250, ref now);

        for (int i = 0; i < 100; i++)
        {
            now += 500;
            Assert.Equal(HideLoopChange.None, guard.Report(i % 2 == 0, Where, now, false));
        }

        Assert.True(guard.Latched);
    }

    [Fact]
    public void Latched_is_released_when_the_bars_or_taskbar_move()
    {
        var guard = new HideLoopGuard();
        long now = 1000;
        Flip(guard, 8, 250, ref now);

        var change = guard.Report(false, "40 0,664 1366x104", now + 500, false);

        Assert.Equal(HideLoopChange.Released, change);
        Assert.False(guard.Latched);
    }

    [Fact]
    public void Latched_is_released_after_a_long_time()
    {
        var guard = new HideLoopGuard();
        long now = 1000;
        Flip(guard, 8, 250, ref now);

        var change = guard.Report(false, Where, now + HideLoopGuard.MaxLatchMs + 1, false);

        Assert.Equal(HideLoopChange.Released, change);
    }

    [Fact]
    public void Release_starts_counting_again()
    {
        var guard = new HideLoopGuard();
        long now = 1000;
        Flip(guard, 8, 250, ref now);
        guard.Release();

        Assert.False(guard.Latched);
        Assert.Equal(HideLoopChange.None, Flip(guard, 3, 250, ref now));
        Assert.Equal(HideLoopChange.Latched, Flip(guard, 8, 250, ref now));
    }
}
