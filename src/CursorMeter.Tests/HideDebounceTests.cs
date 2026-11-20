using Xunit;

namespace CursorMeter.Tests;

public sealed class HideDebounceTests
{
    [Fact]
    public void A_taskbar_in_view_is_never_hidden()
    {
        var debounce = new HideDebounce();

        Assert.False(debounce.Confirm(false, false, 1000));
        Assert.False(debounce.Confirm(false, true, 5000));
    }

    [Fact]
    public void A_short_dip_is_ignored()
    {
        var debounce = new HideDebounce();

        Assert.False(debounce.Confirm(true, false, 1000));
        Assert.False(debounce.Confirm(true, false, 1000 + HideDebounce.DelayMs - 1));
        Assert.False(debounce.Confirm(false, false, 1000 + HideDebounce.DelayMs + 10));
    }

    [Fact]
    public void A_dip_that_ends_starts_the_count_again()
    {
        var debounce = new HideDebounce();
        debounce.Confirm(true, false, 1000);
        debounce.Confirm(false, false, 1100);

        Assert.False(debounce.Confirm(true, false, 1200));
        Assert.False(debounce.Confirm(true, false, 1200 + HideDebounce.DelayMs - 1));
        Assert.True(debounce.Confirm(true, false, 1200 + HideDebounce.DelayMs));
    }

    [Fact]
    public void A_taskbar_that_stays_hidden_is_confirmed()
    {
        var debounce = new HideDebounce();

        Assert.False(debounce.Confirm(true, false, 1000));
        Assert.True(debounce.Confirm(true, false, 1000 + HideDebounce.DelayMs));
        Assert.True(debounce.Confirm(true, false, 1000 + HideDebounce.DelayMs + 600));
    }

    [Fact]
    public void Bars_already_placed_as_hidden_follow_at_once()
    {
        var debounce = new HideDebounce();

        Assert.True(debounce.Confirm(true, true, 1000));
        Assert.False(debounce.Confirm(false, true, 1010));
    }

    [Fact]
    public void Reset_forgets_the_dip()
    {
        var debounce = new HideDebounce();
        debounce.Confirm(true, false, 1000);
        debounce.Reset();

        Assert.False(debounce.Confirm(true, false, 1000 + HideDebounce.DelayMs));
    }
}
