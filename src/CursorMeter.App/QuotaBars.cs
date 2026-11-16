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
    private const double TextSize = 32d / 3;
    public static readonly IPen DefaultOutline = new Pen(Palette.Border);

    private UsageQuotas? _quotas;
    private string _status = MeterState.Loading.Status!;
    private IBrush _fill = Palette.Background;
    private IPen? _outline = DefaultOutline;
    private double _radius;
    private double _margin;
    private double _inset = 0.5;

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
            var status = Text(_status, Palette.Muted, size.Width - 8, TextAlignment.Center);
            context.DrawText(status, new Point(4, (size.Height - status.Height) / 2));
            return;
        }

        DrawRow(context, 0, "Cursor Models", _quotas.CursorModelsPercent, Palette.CursorPool);
        DrawRow(context, 1, "Other Models", _quotas.OtherModelsPercent, Palette.OtherPool);
    }

    private void DrawRow(DrawingContext context, int index, string label, double percent, IBrush poolColor)
    {
        var width = Bounds.Width;
        var rowHeight = Math.Max(12, (Bounds.Height - 6) / 2);
        var y = 3 + (index * rowHeight);
        var tone = UsageQuotas.ToneOf(percent);
        var fill = tone switch
        {
            QuotaTone.Hot => Palette.Hot,
            QuotaTone.Warn => Palette.OtherPool,
            _ => poolColor,
        };
        var textBrush = tone == QuotaTone.Hot ? fill : Palette.Text;

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

    private static FormattedText Text(string text, IBrush brush, double maxWidth, TextAlignment alignment)
        => new(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, TextSize, brush)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis,
            TextAlignment = alignment,
        };
}
