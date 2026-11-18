using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CursorMeter.App;

/// <summary>
/// The two pool bars, or a status line while there are no quotas.
/// </summary>
internal sealed class QuotaBars : Control
{
    public static readonly IPen DefaultOutline = new Pen(Palette.Border);

    private UsageQuotas? _quotas;
    private string _status = MeterState.Loading.Status!;
    private BarStyle _style = BarStyle.Default;
    private IBrush _fill = Palette.Background;
    private IPen? _outline = DefaultOutline;
    private double _radius;
    private double _margin;
    private double _inset = 0.5;
    private bool _vertical;

    /// <summary>
    /// Sets the box behind the bars: <paramref name="margin"/> in from the control's edge, with
    /// the outline centred a further <paramref name="inset"/> inside the box.
    /// </summary>
    public void ShowFrame(IBrush fill, IPen? outline, double radius, double margin, double inset)
    {
        _fill = fill;
        _outline = outline;
        _radius = radius;
        _margin = margin;
        _inset = inset;
        InvalidateVisual();
    }

    /// <summary>
    /// True on a taskbar at the left or right of the screen. Each pool is then a ring with its
    /// name above and its percentage inside, so the meter fits a narrow taskbar.
    /// </summary>
    public bool Vertical
    {
        get => _vertical;
        set
        {
            if (_vertical == value)
                return;
            _vertical = value;
            InvalidateVisual();
        }
    }

    public void ApplyStyle(BarStyle style)
    {
        _style = style;
        InvalidateVisual();
    }

    public void ShowQuotas(UsageQuotas quotas)
    {
        _quotas = quotas;
        InvalidateVisual();
    }

    public void ShowStatus(string status)
    {
        _quotas = null;
        _status = status;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        var size = Bounds.Size;
        var box = new Rect(size).Deflate(_margin);
        context.DrawRectangle(_fill, null, box, _radius, _radius);
        if (_outline is not null)
        {
            double radius = Math.Max(0, _radius - _inset);
            context.DrawRectangle(null, _outline, box.Deflate(_inset), radius, radius);
        }

        if (_quotas is null)
        {
            var status = Text(_status, _style.Muted, size.Width - 8, TextAlignment.Center, _vertical ? 4 : 1);
            context.DrawText(status, new Point(4, (size.Height - status.Height) / 2));
            return;
        }

        var rows = new List<(string Label, string Short, double Percent, IBrush Pool)>(2);
        if (_style.Pools != PoolView.OtherOnly)
            rows.Add(("Cursor Models", "Cursor", _quotas.CursorModelsPercent, _style.CursorPool));
        if (_style.Pools != PoolView.CursorOnly)
            rows.Add(("Other Models", "Other", _quotas.OtherModelsPercent, _style.OtherPool));
        for (int i = 0; i < rows.Count; i++)
        {
            if (_vertical)
                DrawRing(context, i, rows.Count, rows[i].Short, rows[i].Percent, rows[i].Pool);
            else
                DrawRow(context, i, rows.Count, rows[i].Label, rows[i].Percent, rows[i].Pool);
        }
    }

    /// <summary>
    /// A pool on a narrow taskbar: its name above a ring that fills with the usage, the percentage
    /// in the middle of the ring.
    /// </summary>
    private void DrawRing(DrawingContext context, int index, int rows, string label, double used, IBrush poolColor)
    {
        const double pad = 4;
        const double gap = 3;
        const double stroke = 4;
        var width = Bounds.Width;
        var rowHeight = Math.Max(24, (Bounds.Height - 6) / rows);
        var y = 3 + (index * rowHeight);
        var (fill, textBrush) = Tone(used, poolColor);
        var percent = _style.ShowRemaining && !double.IsNaN(used) ? Math.Max(0, 100 - used) : used;

        var labelText = Text(label, textBrush, Math.Max(1, width - (2 * pad)), TextAlignment.Center);
        var diameter = Math.Clamp(Math.Min(width - (2 * pad), rowHeight - labelText.Height - gap - pad), 20, 56);
        var top = y + Math.Max(0, (rowHeight - (labelText.Height + gap + diameter)) / 2);
        context.DrawText(labelText, new Point(pad, top));

        var center = new Point(width / 2, top + labelText.Height + gap + (diameter / 2));
        var radius = (diameter - stroke) / 2;
        context.DrawEllipse(null, new Pen(Palette.Track, stroke), center, radius, radius);

        var share = QuotaText.BarFill(percent);
        if (share >= 0.999)
        {
            context.DrawEllipse(null, new Pen(fill, stroke), center, radius, radius);
        }
        else if (share > 0)
        {
            double end = (-90 + (360 * share)) * Math.PI / 180;
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(center.X, center.Y - radius), false);
                ctx.ArcTo(
                    new Point(center.X + (radius * Math.Cos(end)), center.Y + (radius * Math.Sin(end))),
                    new Size(radius, radius),
                    0,
                    share > 0.5,
                    SweepDirection.Clockwise);
                ctx.EndFigure(false);
            }

            context.DrawGeometry(null, new Pen(fill, stroke, lineCap: PenLineCap.Round), geometry);
        }

        // The percentage must fit inside the ring, so it shrinks with it.
        var inside = Math.Max(1, diameter - (2 * stroke));
        var number = Text(QuotaText.Format(percent), textBrush, inside + 6, TextAlignment.Center, 1, Math.Min(_style.TextSize, inside / 3.2));
        context.DrawText(number, new Point(center.X - ((inside + 6) / 2), center.Y - (number.Height / 2)));
    }

    private (IBrush Fill, IBrush Text) Tone(double used, IBrush poolColor)
    {
        var tone = UsageQuotas.ToneOf(used, _style.WarnAt, _style.HotAt);
        var fill = tone switch
        {
            QuotaTone.Hot => _style.Hot,
            QuotaTone.Warn => _style.Warn,
            _ => poolColor,
        };
        return (fill, tone == QuotaTone.Hot ? fill : _style.Text);
    }

    private void DrawRow(DrawingContext context, int index, int rows, string label, double used, IBrush poolColor)
    {
        var width = Bounds.Width;
        var rowHeight = Math.Max(12, (Bounds.Height - 6) / rows);
        var y = 3 + (index * rowHeight);
        var tone = UsageQuotas.ToneOf(used, _style.WarnAt, _style.HotAt);
        var fill = tone switch
        {
            QuotaTone.Hot => _style.Hot,
            QuotaTone.Warn => _style.Warn,
            _ => poolColor,
        };
        var textBrush = tone == QuotaTone.Hot ? fill : _style.Text;
        var percent = _style.ShowRemaining && !double.IsNaN(used) ? Math.Max(0, 100 - used) : used;

        var labelText = Text(label, textBrush, 92, TextAlignment.Left);
        context.DrawText(labelText, new Point(8, y + ((rowHeight - labelText.Height) / 2)));

        const double barX = 102;
        var barWidth = Math.Max(24, width - barX - 46);
        var barY = y + Math.Max(2, (rowHeight - 6) / 2);
        context.FillRectangle(Palette.Track, new Rect(barX, barY, barWidth, 6));
        var filled = barWidth * QuotaText.BarFill(percent);
        if (filled > 0)
            context.FillRectangle(fill, new Rect(barX, barY, Math.Min(barWidth, filled), 6));

        var percentText = Text(QuotaText.Format(percent), textBrush, 38, TextAlignment.Right);
        context.DrawText(percentText, new Point(width - 44, y + ((rowHeight - percentText.Height) / 2)));
    }
    private FormattedText Text(string text, IBrush brush, double maxWidth, TextAlignment alignment, int maxLines = 1, double? size = null)
        => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, size ?? _style.TextSize, brush)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = maxLines,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = alignment,
        };
}
