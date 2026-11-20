using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CursorMeter.App.ExtraSpend;

/// <summary>
/// ExtraSpend. The step of the last minute. The height is the money spent in that minute, on a scale
/// that only moves in fixed steps (so 0.03 $ and 0.15 $ are different heights), and each step says how
/// much it was. The colour is the day's, from <see cref="Palette"/>.
/// </summary>
internal sealed class SpendGraph : Control
{
    /// <summary>Top of the scale, in dollars. The smallest one that holds the minute's total.</summary>
    private static readonly double[] Scales = [0.2, 0.5, 1, 2, 5, 10, 20, 50, 100, 200, 500];

    private const double LabelSize = 10;

    private IReadOnlyList<SpendPoint> _points = Array.Empty<SpendPoint>();
    private ExtraSpendTone _tone = ExtraSpendTone.Normal;

    public SpendGraph()
    {
        ClipToBounds = true;
    }

    public void SetReading(IReadOnlyList<SpendPoint> points, ExtraSpendTone tone)
    {
        _points = points;
        _tone = tone;
        InvalidateVisual();
    }

    /// <summary>The top of the scale for a minute that spent <paramref name="peak"/> dollars.</summary>
    internal static double ScaleFor(double peak)
    {
        foreach (var scale in Scales)
        {
            if (peak <= scale)
                return scale;
        }

        return Math.Ceiling(peak / 100) * 100;
    }

    public override void Render(DrawingContext context)
    {
        var plot = new Rect(0, 4, Bounds.Width, Math.Max(0, Bounds.Height - 8));
        if (plot.Width <= 1 || plot.Height <= 1)
            return;

        double peak = 0;
        foreach (var point in _points)
            peak = Math.Max(peak, point.Dollars);

        double scale = ScaleFor(peak);
        // Room above the top gridline for its label.
        double usable = plot.Height - 14;

        double X(double seconds) => plot.X + Math.Clamp(seconds, 0, ExtraSpendTrace.WindowSeconds) / ExtraSpendTrace.WindowSeconds * plot.Width;
        double Y(double dollars) => plot.Bottom - Math.Min(dollars, scale) / scale * usable;

        var tone = ToneBrush(_tone);
        var faint = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255));
        var muted = new SolidColorBrush(Color.FromArgb(150, 0xB0, 0xAC, 0xA0));

        foreach (var level in new[] { scale, scale / 2 })
        {
            double y = Y(level);
            context.DrawLine(new Pen(faint, 1, DashStyle.Dash), new Point(plot.X, y), new Point(plot.Right, y));
            var text = Label(Money(level), muted);
            context.DrawText(text, new Point(plot.X, y - text.Height - 1));
        }

        context.DrawLine(new Pen(faint, 1), new Point(plot.X, plot.Bottom), new Point(plot.Right, plot.Bottom));
        if (_points.Count < 2)
            return;

        var fill = new StreamGeometry();
        using (var figure = fill.Open())
        {
            figure.BeginFigure(new Point(X(_points[0].Seconds), Y(_points[0].Dollars)), true);
            for (int i = 1; i < _points.Count; i++)
                figure.LineTo(new Point(X(_points[i].Seconds), Y(_points[i].Dollars)));
            figure.LineTo(new Point(X(_points[^1].Seconds), plot.Bottom));
            figure.LineTo(new Point(X(_points[0].Seconds), plot.Bottom));
            figure.EndFigure(true);
        }

        var stroke = new StreamGeometry();
        using (var figure = stroke.Open())
        {
            figure.BeginFigure(new Point(X(_points[0].Seconds), Y(_points[0].Dollars)), false);
            for (int i = 1; i < _points.Count; i++)
                figure.LineTo(new Point(X(_points[i].Seconds), Y(_points[i].Dollars)));
            figure.EndFigure(false);
        }

        context.DrawGeometry(new SolidColorBrush(Color.FromArgb(36, tone.Color.R, tone.Color.G, tone.Color.B)), null, fill);
        context.DrawGeometry(null, new Pen(tone, 1.5), stroke);

        // What each step cost, from the oldest to the newest. A label that would sit on the previous one is left out.
        double lastRight = double.NegativeInfinity;
        for (int i = 1; i < _points.Count; i++)
        {
            var from = _points[i - 1];
            var to = _points[i];
            double cost = to.Dollars - from.Dollars;
            if (Math.Abs(to.Seconds - from.Seconds) > 0.001 || cost <= 0.0001)
                continue;

            var text = Label("+" + Money(cost), tone);
            double x = X(to.Seconds) + 4;
            if (x + text.Width > plot.Right)
                x = X(to.Seconds) - 4 - text.Width;
            if (x < lastRight + 4)
                continue;
            double y = Math.Max(plot.Y, Y(to.Dollars) - text.Height - 1);
            context.DrawText(text, new Point(x, y));
            lastRight = x + text.Width;
        }
    }

    private static string Money(double dollars)
        => dollars.ToString("0.00", CultureInfo.CurrentCulture) + " $";

    private static FormattedText Label(string text, IBrush brush)
        => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, LabelSize, brush);

    private static ISolidColorBrush ToneBrush(ExtraSpendTone tone) => tone switch
    {
        ExtraSpendTone.Hot => Palette.Hot,
        ExtraSpendTone.Warn => Palette.Warn,
        _ => Palette.CursorPool,
    };
}
