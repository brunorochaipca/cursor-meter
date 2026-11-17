namespace CursorMeter;

/// <summary>An island cut out of the taskbar, in the taskbar's own coordinates.</summary>
/// <param name="Corner">How far the rounded corner cuts into each row, from the top edge down.</param>
public sealed record TaskbarIsland(PxRect Bounds, IReadOnlyList<int> Corner);

/// <summary>
/// Start11's taskbar islands. The taskbar window keeps its full width, but a window region
/// cuts it into rounded islands, so the space beside them shows the desktop.
/// </summary>
public static class TaskbarIslands
{
    /// <summary>The islands in the rectangles of the taskbar's window region, left to right.</summary>
    public static IReadOnlyList<TaskbarIsland> FromRegion(IReadOnlyList<PxRect> rects)
    {
        var rows = rects.Where(rect => !rect.IsEmpty).ToList();
        if (rows.Count == 0)
            return [];

        // The tallest band runs through every island at its full width, below the rounded corners.
        var tallest = rows.MaxBy(rect => rect.Height);
        var spans = rows
            .Where(rect => rect.Top == tallest.Top && rect.Bottom == tallest.Bottom)
            .OrderBy(rect => rect.Left);

        var islands = new List<TaskbarIsland>();
        foreach (var span in spans)
        {
            var own = rows.Where(rect => rect.Left >= span.Left && rect.Right <= span.Right).ToList();
            int top = own.Min(rect => rect.Top);
            int bottom = own.Max(rect => rect.Bottom);
            var corner = new List<int>();
            for (int y = top; y < span.Top; y++)
            {
                var row = own.FirstOrDefault(rect => rect.Top <= y && y < rect.Bottom);
                corner.Add(row.IsEmpty ? 0 : row.Left - span.Left);
            }

            islands.Add(new TaskbarIsland(new PxRect(span.Left, top, span.Right, bottom), corner));
        }

        return islands;
    }

    /// <summary>
    /// The radius in pixels of the circle whose aliased corner cuts <paramref name="corner"/> into the rows.
    /// Start11 strokes its antialiased outline on that circle.
    /// </summary>
    public static double CornerRadius(IReadOnlyList<int> corner)
    {
        if (corner.Count == 0)
            return 0;

        double best = corner.Count;
        double bestError = double.MaxValue;
        for (double radius = corner.Count; radius <= (corner.Count * 4) + 8; radius += 0.25)
        {
            double error = 0;
            for (int row = 0; row <= corner.Count; row++)
            {
                double up = radius - row - 0.5;
                double cut = up > 0 ? radius - Math.Sqrt((radius * radius) - (up * up)) : 0;
                double actual = row < corner.Count ? corner[row] : 0;
                error += (cut - actual) * (cut - actual);
            }

            if (error < bestError)
            {
                bestError = error;
                best = radius;
            }
        }

        return best;
    }

    /// <summary>
    /// True when the region is Start11's islands: at least two, the apps and the tray. The solid
    /// style is one rectangle and the rounded style is one rounded bar, so neither has islands to
    /// take a place beside. With the islands style chosen in Start11, one island is also right: when
    /// the apps island grows until it touches the tray island, Start11 merges them into one.
    /// </summary>
    public static bool AreIslands(IReadOnlyList<TaskbarIsland> islands, bool islandsStyle) =>
        islands.Count >= 2 || (islandsStyle && islands.Count == 1);

    /// <summary>
    /// The stretches of free taskbar the bars could take as one more island, left to right, in
    /// screen pixels: before the first island and between islands. Each is kept clear of its
    /// neighbours by the margin the last island has from the right edge. Where the alignment is
    /// left, the only one is between the apps and the tray island; with the buttons centred there
    /// is one beside the Start island and one beside the tray; with everything centred, the
    /// corner before the first island and a narrow one beside the tray.
    /// </summary>
    public static IReadOnlyList<TaskbarGap> Gaps(PxRect taskbar, IReadOnlyList<TaskbarIsland> islands)
    {
        if (islands.Count == 0)
            return [];

        int margin = Math.Max(0, taskbar.Width - islands[^1].Bounds.Right);
        int top = taskbar.Top + islands[0].Bounds.Top;
        int bottom = taskbar.Top + islands[0].Bounds.Bottom;
        var gaps = new List<TaskbarGap>();
        int left = taskbar.Left + margin;
        for (int i = 0; i < islands.Count; i++)
        {
            int right = taskbar.Left + islands[i].Bounds.Left - margin;
            if (right > left)
            {
                // The gap before the tray island stays put as apps come and go, so the bars hug the
                // tray there. Any other gap hugs the island before it, which is the one that stays.
                var anchor = i == islands.Count - 1 ? GapAnchor.Right : GapAnchor.Left;
                gaps.Add(new TaskbarGap(new PxRect(left, top, right, bottom), anchor));
            }

            left = taskbar.Left + islands[i].Bounds.Right + margin;
        }

        return gaps;
    }

    /// <summary>The widest free stretch, in pixels. Opening many windows narrows every stretch.</summary>
    public static int Room(PxRect taskbar, IReadOnlyList<TaskbarIsland> islands) =>
        Gaps(taskbar, islands).Select(gap => gap.Bounds.Width).DefaultIfEmpty(0).Max();

    /// <summary>
    /// A slot in screen pixels for the bars as one more island, as tall as the others. It takes the
    /// first free stretch that holds <paramref name="width"/>; failing that the widest one that is
    /// at least <paramref name="minWidth"/> wide, shrunk to fit. Empty when no stretch has room.
    /// </summary>
    public static PxRect BarsSlot(PxRect taskbar, IReadOnlyList<TaskbarIsland> islands, int width, int minWidth)
    {
        TaskbarGap? widest = null;
        foreach (var gap in Gaps(taskbar, islands))
        {
            if (gap.Bounds.Width < minWidth)
                continue;
            if (gap.Bounds.Width >= width)
                return Fit(gap, width);
            if (widest is null || gap.Bounds.Width > widest.Bounds.Width)
                widest = gap;
        }

        return widest is null ? default : Fit(widest, widest.Bounds.Width);
    }

    private static PxRect Fit(TaskbarGap gap, int width)
    {
        var bounds = gap.Bounds;
        return gap.Anchor == GapAnchor.Right
            ? new PxRect(bounds.Right - width, bounds.Top, bounds.Right, bounds.Bottom)
            : new PxRect(bounds.Left, bounds.Top, bounds.Left + width, bounds.Bottom);
    }
}

/// <summary>Which neighbour of a free stretch the bars stay next to.</summary>
public enum GapAnchor
{
    Left,
    Right,
}

/// <summary>A free stretch of the taskbar, in screen pixels.</summary>
public sealed record TaskbarGap(PxRect Bounds, GapAnchor Anchor);
