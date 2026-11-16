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
}
