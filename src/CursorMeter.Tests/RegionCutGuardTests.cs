using Xunit;

namespace CursorMeter.Tests;

public sealed class RegionCutGuardTests
{
    [Fact]
    public void A_new_guard_allows_the_cut()
    {
        Assert.True(new RegionCutGuard().MayCut);
    }

    [Fact]
    public void An_intact_cut_never_gives_up()
    {
        var guard = new RegionCutGuard();
        for (int i = 0; i < 100; i++)
            guard.Report(intact: true);

        Assert.True(guard.MayCut);
    }

    [Fact]
    public void Gives_up_after_the_cut_is_undone_several_times_in_a_row()
    {
        var guard = new RegionCutGuard();
        for (int i = 0; i < RegionCutGuard.MaxLosses - 1; i++)
        {
            guard.Report(intact: false);
            Assert.True(guard.MayCut);
        }

        guard.Report(intact: false);

        Assert.False(guard.MayCut);
    }

    [Fact]
    public void One_passing_reset_is_forgiven()
    {
        var guard = new RegionCutGuard();
        guard.Report(intact: false);
        guard.Report(intact: false);
        guard.Report(intact: true);
        guard.Report(intact: false);
        guard.Report(intact: false);

        Assert.True(guard.MayCut);
    }

    [Fact]
    public void Stays_given_up_until_reset()
    {
        var guard = new RegionCutGuard();
        for (int i = 0; i < RegionCutGuard.MaxLosses; i++)
            guard.Report(intact: false);

        guard.Report(intact: true);
        Assert.False(guard.MayCut);

        guard.Reset();
        Assert.True(guard.MayCut);
    }
}
