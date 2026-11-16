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
    /// A slot in screen pixels for the bars as one more island, left of the first one, as tall as
    /// it and as far from the left edge as the last island is from the right. Empty without room.
    /// </summary>
    public static PxRect LeftSlot(PxRect taskbar, IReadOnlyList<TaskbarIsland> islands, int width, int minWidth)
    {
        if (islands.Count == 0)
            return default;

        var first = islands[0].Bounds;
        var last = islands[^1].Bounds;
        int margin = Math.Max(0, taskbar.Width - last.Right);
        int left = taskbar.Left + margin;
        int right = Math.Min(left + width, taskbar.Left + first.Left - margin);
        if (right - left < minWidth)
            return default;

        return new PxRect(left, taskbar.Top + first.Top, right, taskbar.Top + first.Bottom);
    }
}
