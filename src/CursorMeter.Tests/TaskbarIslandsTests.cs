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

    [Fact]
    public void LeftSlot_mirrors_the_tray_island_margin()
    {
        var islands = TaskbarIslands.FromRegion(Region);

        var slot = TaskbarIslands.LeftSlot(Taskbar, islands, 315, 200);

        Assert.Equal(new PxRect(9, 1135, 324, 1196), slot);
    }

    [Fact]
    public void LeftSlot_shrinks_to_the_room_before_the_first_island()
    {
        var islands = TaskbarIslands.FromRegion(Region);

        var slot = TaskbarIslands.LeftSlot(Taskbar, islands, 900, 200);

        Assert.Equal(9, slot.Left);
        Assert.Equal(505 - 9, slot.Right);
    }

    [Fact]
    public void LeftSlot_is_empty_when_the_islands_start_on_the_left()
    {
        var islands = TaskbarIslands.FromRegion([new(9, 0, 900, 61), new(1600, 0, 1911, 61)]);

        Assert.True(TaskbarIslands.LeftSlot(Taskbar, islands, 315, 200).IsEmpty);
    }

    [Fact]
    public void A_taskbar_without_a_region_has_no_islands()
    {
        Assert.Empty(TaskbarIslands.FromRegion([]));
        Assert.True(TaskbarIslands.LeftSlot(Taskbar, [], 315, 200).IsEmpty);
    }
}
