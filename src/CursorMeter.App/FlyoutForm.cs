namespace CursorMeter.App;

internal sealed class FlyoutForm : Form
{
    public FlyoutForm(string detail, string? secondary, Action refresh)
    {
        AutoScaleMode = AutoScaleMode.Font;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(320, secondary is null ? 118 : 142);
        BackColor = Color.FromArgb(24, 24, 24);
        ForeColor = Color.FromArgb(236, 234, 226);
        Font = new Font("Segoe UI", 9f, FontStyle.Regular, GraphicsUnit.Point);

        var line = new Label
        {
            AutoSize = false,
            Bounds = new Rectangle(16, 14, 288, 40),
            Text = detail,
            Font = new Font(Font, FontStyle.Bold),
            ForeColor = ForeColor,
        };
        var extra = new Label
        {
            AutoSize = false,
            Bounds = new Rectangle(16, 54, 288, 36),
            Text = secondary ?? "",
            ForeColor = Color.FromArgb(176, 172, 160),
        };
        var button = new Button
        {
            Text = "Atualizar",
            Bounds = new Rectangle(16, ClientSize.Height - 40, 96, 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(44, 44, 44),
            ForeColor = ForeColor,
        };
        button.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
        button.Click += (_, _) => refresh();

        var dashboard = new Button
        {
            Text = "Painel do Cursor",
            Bounds = new Rectangle(120, ClientSize.Height - 40, 130, 26),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(44, 44, 44),
            ForeColor = ForeColor,
        };
        dashboard.FlatAppearance.BorderColor = Color.FromArgb(70, 70, 70);
        dashboard.Click += (_, _) =>
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://cursor.com/dashboard?tab=spending",
                UseShellExecute = true,
            });
        };

        Controls.Add(line);
        Controls.Add(extra);
        Controls.Add(button);
        Controls.Add(dashboard);
        KeyPreview = true;
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
                Close();
        };
        Shown += (_, _) => BeginInvoke(() => Deactivate += (_, _) => Close());
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var border = new Pen(Color.FromArgb(70, 70, 70));
        e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
    }
}
