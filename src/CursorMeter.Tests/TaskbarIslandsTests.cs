using Xunit;

namespace CursorMeter.Tests;

public sealed class TaskbarIslandsTests
{
    // Start11 v3 at 125% on a 1920x1200 screen: the Start and apps island, then the tray island.
    private static readonly PxRect Taskbar = new(0, 1135, 1920, 1200);

    private static readonly PxRect[] Region =
    [
        new(512, 0, 1416, 1), new(1607, 0, 1904, 1),
        new(510, 1, 1418, 2), new(1605, 1, 1906, 2),
        new(508, 2, 1420, 3), new(1603, 2, 1908, 3),
        new(507, 3, 1421, 5), new(1602, 3, 1909, 5),
        new(506, 5, 1422, 7), new(1601, 5, 1910, 7),
        new(505, 7, 1423, 54), new(1600, 7, 1911, 54),
        new(506, 54, 1422, 56), new(1601, 54, 1910, 56),
        new(507, 56, 1421, 58), new(1602, 56, 1909, 58),
        new(508, 58, 1420, 59), new(1603, 58, 1908, 59),
        new(510, 59, 1418, 60), new(1605, 59, 1906, 60),
        new(512, 60, 1416, 61), new(1607, 60, 1904, 61),
    ];

    [Fact]
    public void FromRegion_finds_each_island_and_its_corner()
    {
        var islands = TaskbarIslands.FromRegion(Region);

        Assert.Equal(2, islands.Count);
        Assert.Equal(new PxRect(505, 0, 1423, 61), islands[0].Bounds);
        Assert.Equal(new PxRect(1600, 0, 1911, 61), islands[1].Bounds);
        Assert.Equal(new[] { 7, 5, 3, 2, 2, 1, 1 }, islands[0].Corner);
    }

    [Fact]
    public void CornerRadius_fits_the_circle_behind_the_cut()
    {
        Assert.Equal(10, TaskbarIslands.CornerRadius([7, 5, 3, 2, 2, 1, 1]), 0.5);
        Assert.Equal(0, TaskbarIslands.CornerRadius([]));
    }

    private static IReadOnlyList<TaskbarIsland> Islands(params PxRect[] bounds) =>
        bounds.Select(rect => new TaskbarIsland(rect, [])).ToList();

    [Fact]
    public void With_everything_centred_the_bars_take_the_corner_before_the_first_island()
    {
        var islands = TaskbarIslands.FromRegion(Region);

        var slot = TaskbarIslands.BarsSlot(Taskbar, islands, 315, 200);

        Assert.Equal(new PxRect(9, 1135, 324, 1196), slot);
    }

    [Fact]
    public void The_slot_shrinks_to_the_room_when_the_stretch_is_narrower_than_the_bars()
    {
        var islands = TaskbarIslands.FromRegion(Region);

        var slot = TaskbarIslands.BarsSlot(Taskbar, islands, 900, 200);

        Assert.Equal(9, slot.Left);
        Assert.Equal(505 - 9, slot.Right);
    }

    [Fact]
    public void With_everything_centred_the_stretch_beside_the_tray_is_too_narrow()
    {
        // The real Start11 layout: 177 px between the centred island and the tray island.
        var gaps = TaskbarIslands.Gaps(Taskbar, TaskbarIslands.FromRegion(Region));

        Assert.Equal(2, gaps.Count);
        Assert.Equal(new TaskbarGap(new PxRect(9, 1135, 496, 1196), GapAnchor.Left), gaps[0]);
        Assert.Equal(159, gaps[1].Bounds.Width);
        Assert.Equal(GapAnchor.Right, gaps[1].Anchor);
    }

    [Fact]
    public void Aligned_left_the_bars_take_the_stretch_beside_the_tray()
    {
        // The real Start11 layout aligned left: apps island 10..1125 and tray island 1600..1911.
        var islands = Islands(new PxRect(10, 0, 1125, 61), new PxRect(1600, 0, 1911, 61));

        var slot = TaskbarIslands.BarsSlot(Taskbar, islands, 315, 200);

        Assert.Equal(new PxRect(1591 - 315, 1135, 1591, 1196), slot);
    }

    [Fact]
    public void With_the_buttons_centred_the_bars_take_the_stretch_beside_the_Start_island()
    {
        // The real layout with three islands: Start 10..145, buttons 560..1361, tray 1600..1911.
        var islands = Islands(new PxRect(10, 0, 145, 61), new PxRect(560, 0, 1361, 61), new PxRect(1600, 0, 1911, 61));

        var gaps = TaskbarIslands.Gaps(Taskbar, islands);
        var slot = TaskbarIslands.BarsSlot(Taskbar, islands, 315, 200);

        Assert.Equal(2, gaps.Count);
        Assert.Equal(GapAnchor.Left, gaps[0].Anchor);
        Assert.Equal(GapAnchor.Right, gaps[1].Anchor);
        Assert.Equal(new PxRect(154, 1135, 154 + 315, 1196), slot);
    }

    [Fact]
    public void A_stretch_that_holds_the_bars_is_preferred_to_a_wider_one_that_does_not()
    {
        var islands = Islands(new PxRect(10, 0, 145, 61), new PxRect(560, 0, 1361, 61), new PxRect(1600, 0, 1911, 61));

        // 400 px do not fit in either stretch (397 and 221): the widest one is taken, shrunk to fit.
        var slot = TaskbarIslands.BarsSlot(Taskbar, islands, 400, 200);

        Assert.Equal(new PxRect(154, 1135, 551, 1196), slot);
    }

    [Fact]
    public void With_the_buttons_centred_the_bars_move_to_the_tray_when_the_buttons_island_grows()
    {
        // The buttons island grew leftwards: 167 px beside Start are too few, 221 beside the tray are enough.
        var islands = Islands(new PxRect(10, 0, 145, 61), new PxRect(330, 0, 1361, 61), new PxRect(1600, 0, 1911, 61));

        var slot = TaskbarIslands.BarsSlot(Taskbar, islands, 315, 200);

        Assert.Equal(new PxRect(1591 - 221, 1135, 1591, 1196), slot);
    }

    [Fact]
    public void Without_a_free_stretch_there_is_no_slot_and_the_room_says_how_little_is_left()
    {
        // The apps island of a crowded taskbar reaches almost the tray: 132 px are free.
        var islands = Islands(new PxRect(10, 0, 1450, 61), new PxRect(1600, 0, 1911, 61));

        Assert.Equal(132, TaskbarIslands.Room(Taskbar, islands));
        Assert.True(TaskbarIslands.BarsSlot(Taskbar, islands, 315, 200).IsEmpty);
    }

    [Fact]
    public void A_wider_minimum_makes_the_bars_wait_before_returning_to_the_island()
    {
        // 210 px free: the bars fit on the island at 200, but floating bars ask for 216 to come back.
        var islands = Islands(new PxRect(10, 0, 1372, 61), new PxRect(1600, 0, 1911, 61));

        Assert.False(TaskbarIslands.BarsSlot(Taskbar, islands, 315, 200).IsEmpty);
        Assert.True(TaskbarIslands.BarsSlot(Taskbar, islands, 315, 216).IsEmpty);
    }

    [Fact]
    public void The_solid_style_is_one_rectangle_and_not_islands()
    {
        // Start11 with the solid style: a simple region covering the whole taskbar.
        var islands = TaskbarIslands.FromRegion([new PxRect(0, 0, 1920, 60)]);

        Assert.False(TaskbarIslands.AreIslands(islands, islandsStyle: false));
        Assert.Empty(TaskbarIslands.Gaps(Taskbar, islands));
        Assert.True(TaskbarIslands.BarsSlot(Taskbar, islands, 315, 200).IsEmpty);
    }

    [Fact]
    public void The_rounded_style_is_one_island_and_not_islands()
    {
        // Start11 with the rounded style: one bar with rounded corners, 10..1911 wide.
        var islands = TaskbarIslands.FromRegion(
            [new(17, 0, 1904, 1), new(14, 1, 1907, 2), new(12, 2, 1909, 4), new(10, 4, 1911, 57), new(12, 57, 1909, 59), new(17, 59, 1904, 61)]);

        Assert.Single(islands);
        Assert.False(TaskbarIslands.AreIslands(islands, islandsStyle: false));
        Assert.True(TaskbarIslands.BarsSlot(Taskbar, islands, 315, 200).IsEmpty);
    }

    [Fact]
    public void Islands_that_merged_into_one_are_still_islands_with_the_islands_style()
    {
        // The apps island grew until it touched the tray island and Start11 merged them: 10..1911.
        // Only the style chosen in Start11 tells that from the rounded style, which looks the same.
        var islands = TaskbarIslands.FromRegion(
            [new(17, 0, 1904, 1), new(14, 1, 1907, 2), new(12, 2, 1909, 4), new(10, 4, 1911, 57), new(12, 57, 1909, 59), new(17, 59, 1904, 61)]);

        Assert.True(TaskbarIslands.AreIslands(islands, islandsStyle: true));
        Assert.Equal(0, TaskbarIslands.Room(Taskbar, islands));
        Assert.True(TaskbarIslands.BarsSlot(Taskbar, islands, 315, 200).IsEmpty);
    }

    [Fact]
    public void Two_islands_are_islands_whatever_the_style_says()
    {
        var islands = TaskbarIslands.FromRegion(Region);

        Assert.True(TaskbarIslands.AreIslands(islands, islandsStyle: false));
        Assert.True(TaskbarIslands.AreIslands(islands, islandsStyle: true));
        Assert.False(TaskbarIslands.AreIslands([], islandsStyle: true));
    }

    [Fact]
    public void Room_is_the_widest_free_stretch()
    {
        Assert.Equal(505 - 18, TaskbarIslands.Room(Taskbar, TaskbarIslands.FromRegion(Region)));
        Assert.Equal(0, TaskbarIslands.Room(Taskbar, []));
    }

    [Fact]
    public void A_taskbar_without_a_region_has_no_islands()
    {
        Assert.Empty(TaskbarIslands.FromRegion([]));
        Assert.True(TaskbarIslands.BarsSlot(Taskbar, [], 315, 200).IsEmpty);
    }
}

