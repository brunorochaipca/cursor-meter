using Xunit;

namespace CursorMeter.Tests;

public sealed class TaskbarLayoutTests
{
    [Fact]
    public void Scale_matches_the_monitor()
    {
        Assert.Equal(252, TaskbarLayout.Scale(252, 96));
        Assert.Equal(315, TaskbarLayout.Scale(252, 120));
        Assert.Equal(378, TaskbarLayout.Scale(252, 144));
    }

    [Theory]
    [InlineData(48, 96, 44)]
    [InlineData(60, 120, 55)]
    [InlineData(72, 144, 66)]
    [InlineData(1080, 96, 48)]
    [InlineData(0, 96, 40)]
    public void FitThickness_stays_inside_the_bar(int thickness, int dpi, int expected)
    {
        Assert.Equal(expected, TaskbarLayout.FitThickness(thickness, dpi));
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(0, true)]
    [InlineData(15, true)]
    [InlineData(48, false)]
    [InlineData(60, false)]
    public void IsCollapsed_is_the_auto_hide_sliver(int thickness, bool collapsed)
    {
        var bar = new PxRect(0, 1080 - thickness, 1920, 1080);
        Assert.Equal(collapsed, TaskbarLayout.IsCollapsed(bar));
    }

    [Theory]
    [InlineData(96, 48, 1032)]
    [InlineData(120, 60, 1020)]
    [InlineData(144, 72, 1008)]
    public void Place_keeps_the_bars_inside_the_taskbar_at_every_scale(int dpi, int thickness, int top)
    {
        var taskbar = new PxRect(0, top, 1920, top + thickness);
        int trayWidth = TaskbarLayout.Scale(180, dpi);
        var tray = new PxRect(taskbar.Right - trayWidth, taskbar.Top, taskbar.Right, taskbar.Bottom);
        int width = TaskbarLayout.Scale(TaskbarLayout.PreferredWidth, dpi);
        int height = TaskbarLayout.FitThickness(thickness, dpi);

        var slot = TaskbarLayout.Place(taskbar, tray, BarEdge.Bottom, 0, width, height);

        Assert.True(slot.Left >= taskbar.Left + 2, $"left {slot.Left}");
        Assert.True(slot.Right <= tray.Left, $"right {slot.Right} tray {tray.Left}");
        Assert.True(slot.Top >= taskbar.Top, $"top {slot.Top}");
        Assert.True(slot.Bottom <= taskbar.Bottom, $"bottom {slot.Bottom}");
        Assert.True(slot.Width >= 80);
        Assert.InRange(slot.Height, 28, thickness);
    }

    [Theory]
    [InlineData(BarEdge.Left, 0, 96)]
    [InlineData(BarEdge.Right, 1920 - 72, 96)]
    [InlineData(BarEdge.Left, 0, 144)]
    public void Place_stacks_the_bars_inside_a_vertical_taskbar(BarEdge edge, int left, int dpi)
    {
        int thickness = TaskbarLayout.Scale(72, dpi);
        var taskbar = new PxRect(left, 0, left + thickness, 1080);
        var tray = new PxRect(taskbar.Left, 1080 - TaskbarLayout.Scale(120, dpi), taskbar.Right, 1080);
        var (width, height) = TaskbarLayout.FitSize(taskbar, edge, dpi);

        var slot = TaskbarLayout.Place(taskbar, tray, edge, 0, width, height);

        Assert.True(slot.Left >= taskbar.Left, $"left {slot.Left}");
        Assert.True(slot.Right <= taskbar.Right, $"right {slot.Right}");
        Assert.True(slot.Bottom <= tray.Top, $"bottom {slot.Bottom} tray {tray.Top}");
        Assert.True(slot.Top >= taskbar.Top);
        Assert.Equal(TaskbarLayout.Scale(TaskbarLayout.VerticalHeight, dpi), slot.Height);
        Assert.True(slot.Width < slot.Height * 2);
    }

    [Theory]
    [InlineData(BarEdge.Bottom, 1920, 48, 252, 44)]
    [InlineData(BarEdge.Left, 72, 1080, 64, 128)]
    [InlineData(BarEdge.Right, 300, 1080, 252, 128)]
    [InlineData(BarEdge.Left, 30, 1080, 40, 128)]
    public void FitSize_follows_the_taskbar_orientation(BarEdge edge, int taskbarWidth, int taskbarHeight, int width, int height)
    {
        var taskbar = new PxRect(0, 0, taskbarWidth, taskbarHeight);

        Assert.Equal((width, height), TaskbarLayout.FitSize(taskbar, edge, 96));
    }

    [Fact]
    public void Beside_steps_aside_a_vertical_taskbar()
    {
        var taskbar = new PxRect(0, 0, 72, 1080);
        var slot = new PxRect(4, 900, 68, 964);
        var monitor = new PxRect(0, 0, 1920, 1080);

        var lifted = TaskbarLayout.Beside(taskbar, slot, BarEdge.Left, monitor);

        Assert.Equal(taskbar.Right, lifted.Left);
        Assert.Equal(slot.Top, lifted.Top);
    }

    [Fact]
    public void Beside_steps_above_a_bottom_taskbar_while_start_is_open()
    {
        var taskbar = new PxRect(0, 1032, 1920, 1080);
        var slot = new PxRect(1442, 1034, 1694, 1078);
        var monitor = new PxRect(0, 0, 1920, 1080);

        var lifted = TaskbarLayout.Beside(taskbar, slot, BarEdge.Bottom, monitor);

        Assert.Equal(slot.Left, lifted.Left);
        Assert.Equal(taskbar.Top - slot.Height, lifted.Top);
        Assert.Equal(taskbar.Top, lifted.Bottom);
        Assert.Equal(slot.Width, lifted.Width);
        Assert.True(lifted.Bottom <= taskbar.Top);
    }

    [Fact]
    public void Screen_slot_matches_the_windows_10_client_slot()
    {
        var screenBar = new PxRect(0, 1032, 1920, 1080);
        var screenTray = new PxRect(1680, 1032, 1920, 1080);
        var clientBar = new PxRect(0, 0, 1920, 48);
        var clientTray = new PxRect(1680, 0, 1920, 48);

        var screen = TaskbarLayout.Place(screenBar, screenTray, BarEdge.Bottom, 12, 252, 44);
        var client = TaskbarLayout.Place(clientBar, clientTray, BarEdge.Bottom, 12, 252, 44);

        Assert.Equal(client.Left, screen.Left - screenBar.Left);
        Assert.Equal(client.Top, screen.Top - screenBar.Top);
        Assert.Equal(client.Width, screen.Width);
        Assert.Equal(client.Height, screen.Height);
    }

    // A Windows 10 taskbar, 1920 wide: Start, the search box and Task View, then the apps, then the tray.
    private static readonly PxRect Windows10Bar = new(0, 1032, 1920, 1080);
    private static readonly PxRect Windows10Tray = new(1680, 1032, 1920, 1080);
    private static readonly PxRect[] Windows10Start =
    [
        new(0, 1032, 48, 1080),
        new(48, 1032, 348, 1080),
        new(348, 1032, 396, 1080),
    ];

    [Fact]
    public void KeepClearAlong_ends_at_the_last_button_before_the_apps()
    {
        int limit = TaskbarLayout.KeepClearAlong(Windows10Bar, Windows10Tray, BarEdge.Bottom, Windows10Start);

        Assert.Equal(396, limit);
    }

    [Fact]
    public void KeepClearAlong_ignores_what_spans_the_taskbar_the_tray_and_the_far_half()
    {
        PxRect[] others =
        [
            new(396, 1032, 1680, 1080),
            new(0, 1032, 1920, 1080),
            Windows10Tray,
            new(1000, 1032, 1100, 1080),
            default,
        ];

        Assert.Equal(TaskbarLayout.NoLimit, TaskbarLayout.KeepClearAlong(Windows10Bar, Windows10Tray, BarEdge.Bottom, others));
    }

    [Fact]
    public void KeepClearAlong_follows_a_vertical_taskbar()
    {
        var bar = new PxRect(0, 0, 72, 1080);
        var tray = new PxRect(0, 960, 72, 1080);
        PxRect[] buttons = [new(0, 0, 72, 48), new(0, 48, 72, 96)];

        Assert.Equal(96, TaskbarLayout.KeepClearAlong(bar, tray, BarEdge.Left, buttons));
    }

    [Fact]
    public void Place_keeps_dragged_bars_off_the_search_box()
    {
        int width = 252;
        int height = 44;
        int limit = TaskbarLayout.KeepClearAlong(Windows10Bar, Windows10Tray, BarEdge.Bottom, Windows10Start);

        // Dragged all the way to the left of the screen.
        var free = TaskbarLayout.Place(Windows10Bar, Windows10Tray, BarEdge.Bottom, -5000, width, height);
        var kept = TaskbarLayout.Place(Windows10Bar, Windows10Tray, BarEdge.Bottom, -5000, width, height, limit);

        Assert.True(free.Left < limit, $"free {free.Left}");
        Assert.Equal(limit, kept.Left);
        Assert.Equal(free.Top, kept.Top);
        Assert.Equal(width, kept.Width);
    }

    [Fact]
    public void Place_does_not_move_bars_that_are_already_clear()
    {
        var free = TaskbarLayout.Place(Windows10Bar, Windows10Tray, BarEdge.Bottom, -40, 252, 44);
        var kept = TaskbarLayout.Place(Windows10Bar, Windows10Tray, BarEdge.Bottom, -40, 252, 44, 396);

        Assert.Equal(free, kept);
    }

    [Fact]
    public void Place_keeps_vertical_bars_below_the_buttons_at_the_top()
    {
        var bar = new PxRect(0, 0, 72, 1080);
        var tray = new PxRect(0, 960, 72, 1080);

        var kept = TaskbarLayout.Place(bar, tray, BarEdge.Left, -5000, 64, 128, 96);

        Assert.Equal(96, kept.Top);
    }

    [Fact]
    public void Place_with_a_limit_beyond_the_room_stays_inside_the_taskbar()
    {
        var kept = TaskbarLayout.Place(Windows10Bar, Windows10Tray, BarEdge.Bottom, 0, 252, 44, 5000);

        Assert.True(kept.Right <= Windows10Bar.Right);
        Assert.True(kept.Left >= Windows10Bar.Left);
    }
}
