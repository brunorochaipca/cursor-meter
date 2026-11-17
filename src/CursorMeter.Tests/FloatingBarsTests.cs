using Xunit;

namespace CursorMeter.Tests;

public sealed class FloatingBarsTests
{
    private static readonly PxRect Screen = new(0, 0, 1920, 1200);
    private static readonly PxRect Taskbar = new(0, 1135, 1920, 1200);
    private static readonly PxRect Right = new(1920, 0, 3840, 1200);

    [Fact]
    public void The_default_slot_sits_just_above_a_bottom_taskbar_at_the_left_margin()
    {
        var slot = FloatingBars.DefaultSlot(Taskbar, Screen, BarEdge.Bottom, 8, 317, 63, 5);

        Assert.Equal(new PxRect(8, 1135 - 5 - 63, 8 + 317, 1135 - 5), slot);
    }

    [Fact]
    public void The_default_slot_sits_just_below_a_top_taskbar()
    {
        var slot = FloatingBars.DefaultSlot(new PxRect(0, 0, 1920, 65), Screen, BarEdge.Top, 8, 317, 63, 5);

        Assert.Equal(new PxRect(8, 70, 325, 133), slot);
    }

    [Fact]
    public void ClampInto_keeps_the_slot_inside_the_area_without_resizing_it()
    {
        var slot = FloatingBars.ClampInto(new PxRect(1800, -40, 2117, 23), Screen);

        Assert.Equal(new PxRect(1603, 0, 1920, 63), slot);
    }

    [Fact]
    public void Without_a_saved_position_the_bars_stay_at_home()
    {
        var home = new PxRect(8, 1067, 325, 1130);

        Assert.Equal(home, FloatingBars.Resolve(new FloatingState(null, null, false), home, [Screen]));
    }

    [Fact]
    public void A_saved_position_is_used_on_the_screen_it_belongs_to()
    {
        var home = new PxRect(8, 1067, 325, 1130);

        var slot = FloatingBars.Resolve(new FloatingState(2000, 300, false), home, [Screen, Right]);

        Assert.Equal(new PxRect(2000, 300, 2317, 363), slot);
    }

    [Fact]
    public void A_saved_position_partly_off_screen_is_pulled_back_in()
    {
        var home = new PxRect(8, 1067, 325, 1130);

        var slot = FloatingBars.Resolve(new FloatingState(1700, 1150, false), home, [Screen]);

        Assert.Equal(new PxRect(1603, 1137, 1920, 1200), slot);
    }

    [Fact]
    public void A_saved_position_on_a_screen_that_is_gone_goes_back_home()
    {
        var home = new PxRect(8, 1067, 325, 1130);

        Assert.Equal(home, FloatingBars.Resolve(new FloatingState(2400, 300, false), home, [Screen]));
    }

    [Fact]
    public void The_state_survives_a_round_trip()
    {
        foreach (var state in new[]
                 {
                     new FloatingState(null, null, false),
                     new FloatingState(null, null, true),
                     new FloatingState(120, 640, false),
                     new FloatingState(-1500, 20, true),
                 })
        {
            Assert.Equal(state, FloatingState.Parse(state.Format()));
        }
    }

    [Fact]
    public void A_state_that_cannot_be_read_is_the_default()
    {
        Assert.Equal(new FloatingState(null, null, false), FloatingState.Parse(null));
        Assert.Equal(new FloatingState(null, null, false), FloatingState.Parse(""));
        Assert.Equal(new FloatingState(null, null, false), FloatingState.Parse("not a state"));
        Assert.Equal(new FloatingState(null, null, true), FloatingState.Parse("- - 1"));
    }
}
