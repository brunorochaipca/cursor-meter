namespace CursorMeter.App;

internal sealed class WidgetForm : Form
{
    private readonly Font _font = new("Segoe UI", 8f, FontStyle.Regular, GraphicsUnit.Point);
    private double? _cursor;
    private double? _other;
    private string _status = "A ler…";
    private bool _dragging;
    private Point _dragStart;
    private FlyoutForm? _flyout;

    public WidgetForm()
    {
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        ControlBox = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(TaskbarLayout.PreferredWidth, TaskbarLayout.PreferredHeight);
        BackColor = Color.FromArgb(28, 28, 28);
        ForeColor = Color.FromArgb(236, 234, 226);
        DoubleBuffered = true;
        Text = "CursorMeter";
        Cursor = Cursors.Hand;
    }

    public event EventHandler? RefreshRequested;
    public event EventHandler? QuitRequested;

    public void ShowQuotas(UsageQuotas quotas)
    {
        _cursor = quotas.CursorModelsPercent;
        _other = quotas.OtherModelsPercent;
        _status = "";
        var line = QuotaText.Line(quotas);
        Text = line;
        Invalidate();
    }

    public void ShowStatus(string status)
    {
        _cursor = null;
        _other = null;
        _status = status;
        Text = "CursorMeter";
        Invalidate();
    }

    public void ToggleFlyout(string detail, string? secondary)
    {
        if (_flyout is { IsDisposed: false })
        {
            _flyout.Close();
            _flyout = null;
            return;
        }

        var origin = PointToScreen(Point.Empty);
        _flyout = new FlyoutForm(detail, secondary, () => RefreshRequested?.Invoke(this, EventArgs.Empty));
        var x = origin.X;
        var y = origin.Y - _flyout.Height - 8;
        if (y < 0)
            y = origin.Y + Height + 8;
        _flyout.Location = new Point(x, y);
        _flyout.FormClosed += (_, _) => _flyout = null;
        _flyout.Show(this);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int wsExToolWindow = 0x00000080;
            var parameters = base.CreateParams;
            parameters.ExStyle |= wsExToolWindow;
            return parameters;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        using var border = new Pen(Color.FromArgb(58, 58, 58));
        g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

        if (_cursor is null || _other is null)
        {
            TextRenderer.DrawText(
                g,
                _status,
                _font,
                ClientRectangle,
                Color.FromArgb(196, 192, 180),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }

        DrawRow(g, 0, "Cursor Models", _cursor.Value, Color.FromArgb(125, 206, 160));
        DrawRow(g, 1, "Other Models", _other.Value, Color.FromArgb(224, 177, 90));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button == MouseButtons.Left)
        {
            _dragging = true;
            _dragStart = e.Location;
            Capture = true;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left || !_dragging)
            return;
        _dragging = false;
        Capture = false;
        var moved = Math.Abs(e.X - _dragStart.X) + Math.Abs(e.Y - _dragStart.Y);
        if (moved <= 4)
            RefreshRequested?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging)
            return;
        if (Math.Abs(e.X - _dragStart.X) + Math.Abs(e.Y - _dragStart.Y) < 3)
            return;
        var parentPoint = new Point(Left + e.X - _dragStart.X, Top + e.Y - _dragStart.Y);
        Dragged?.Invoke(this, parentPoint);
    }

    public event EventHandler<Point>? Dragged;

    public void RequestQuit() => QuitRequested?.Invoke(this, EventArgs.Empty);

    private void DrawRow(Graphics graphics, int index, string label, double percent, Color poolColor)
    {
        int rowHeight = Math.Max(12, (Height - 6) / 2);
        int y = 3 + (index * rowHeight);
        var tone = UsageQuotas.ToneOf(percent);
        var fill = tone switch
        {
            QuotaTone.Hot => Color.FromArgb(224, 106, 92),
            QuotaTone.Warn => Color.FromArgb(224, 177, 90),
            _ => poolColor,
        };
        var text = tone == QuotaTone.Hot ? fill : Color.FromArgb(236, 234, 226);

        var labelRect = new Rectangle(8, y, 92, rowHeight);
        TextRenderer.DrawText(graphics, label, _font, labelRect, text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        int barX = 102;
        int barWidth = Math.Max(24, Width - barX - 46);
        int barY = y + Math.Max(2, (rowHeight - 6) / 2);
        var track = new Rectangle(barX, barY, barWidth, 6);
        using var trackBrush = new SolidBrush(Color.FromArgb(48, 48, 48));
        graphics.FillRectangle(trackBrush, track);
        int filled = (int)Math.Round(track.Width * QuotaText.BarFill(percent));
        if (filled > 0)
        {
            using var fillBrush = new SolidBrush(fill);
            graphics.FillRectangle(fillBrush, track.X, track.Y, Math.Min(track.Width, filled), track.Height);
        }

        var percentRect = new Rectangle(Width - 44, y, 38, rowHeight);
        TextRenderer.DrawText(
            graphics,
            QuotaText.Format(percent),
            _font,
            percentRect,
            text,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _font.Dispose();
        base.Dispose(disposing);
    }
}
