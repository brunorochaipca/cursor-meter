using Xunit;

namespace CursorMeter.Tests;

public class SlotGlideTests
{
    private static readonly PxRect From = new(1154, 920, 1469, 980);
    private static readonly PxRect To = new(956, 601, 1271, 661);

    [Fact]
    public void Starts_where_the_bars_are()
    {
        Assert.Equal(From, SlotGlide.At(From, To, 0));
    }

    [Fact]
    public void Ends_exactly_on_the_target()
    {
        Assert.Equal(To, SlotGlide.At(From, To, SlotGlide.DurationMs));
        Assert.Equal(To, SlotGlide.At(From, To, SlotGlide.DurationMs * 5));
    }

    [Fact]
    public void Is_ahead_of_a_straight_line_at_half_time()
    {
        var half = SlotGlide.At(From, To, SlotGlide.DurationMs / 2);
        // A straight line would be at 760; the ease-out is already past it.
        Assert.True(half.Top < 760);
        Assert.True(half.Top > To.Top);
    }

    [Fact]
    public void Keeps_the_size_while_moving()
    {
        var mid = SlotGlide.At(From, To, 70);
        Assert.Equal(From.Width, mid.Width);
        Assert.Equal(From.Height, mid.Height);
    }

    [Fact]
    public void Before_the_start_is_the_start()
    {
        Assert.Equal(From, SlotGlide.At(From, To, -30));
    }
}
