using System.Diagnostics;

namespace CursorMeter.App;

internal sealed class MeterContext : ApplicationContext
{
    private readonly WidgetForm _widget = new();
    private readonly NotifyIcon _icon;
    private readonly UsageClient _client = new();
    private readonly TaskbarEmbedder _embedder;
    private readonly System.Windows.Forms.Timer _usageTimer;
    private readonly System.Windows.Forms.Timer _placeTimer;
    private readonly Icon _iconImage;
    private int _refreshing;
    private string _detail = "A ler a utilização…";
    private string? _secondary = "A sessão vem do Cursor. Não é preciso cookie.";

    public MeterContext()
    {
        _iconImage = CreateIcon();
        _embedder = new TaskbarEmbedder(_widget);
        _widget.Dragged += (_, point) => _embedder.DragBy(point.X, point.Y);
        _widget.RefreshRequested += (_, _) => _widget.ToggleFlyout(_detail, _secondary);
        _widget.QuitRequested += (_, _) => ExitThread();

        var menu = new ContextMenuStrip();
        menu.Items.Add("Atualizar", null, (_, _) => _ = RefreshAsync());
        menu.Items.Add("Painel do Cursor", null, (_, _) => OpenDashboard());
        var startup = new ToolStripMenuItem("Iniciar com o Windows") { Checked = StartupShortcut.IsEnabled, CheckOnClick = true };
        startup.CheckedChanged += (_, _) =>
        {
            try
            {
                StartupShortcut.SetEnabled(startup.Checked);
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                startup.Checked = StartupShortcut.IsEnabled;
            }
        };
        menu.Items.Add(startup);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => _widget.RequestQuit());
        _widget.ContextMenuStrip = menu;

        _icon = new NotifyIcon
        {
            Icon = _iconImage,
            Visible = true,
            Text = "CursorMeter",
            ContextMenuStrip = menu,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                _widget.ToggleFlyout(_detail, _secondary);
        };

        _usageTimer = new System.Windows.Forms.Timer { Interval = 3 * 60 * 1000 };
        _usageTimer.Tick += (_, _) => _ = RefreshAsync();
        _placeTimer = new System.Windows.Forms.Timer { Interval = 2000 };
        _placeTimer.Tick += (_, _) => _embedder.Place();

        _widget.HandleCreated += (_, _) => _embedder.Attach();
        _widget.Show();
        _embedder.Attach();
        _usageTimer.Start();
        _placeTimer.Start();
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (Interlocked.Exchange(ref _refreshing, 1) == 1)
            return;
        try
        {
            var session = CursorSession.TryLoadDefault();
            if (session is null)
            {
                Publish(
                    "Abre o Cursor",
                    "Sem sessão do Cursor",
                    "Abre o Cursor e inicia sessão. Não precisas de colar um cookie.");
                return;
            }

            var quotas = await _client.FetchAsync(session).ConfigureAwait(false);
            var line = QuotaText.Line(quotas);
            var bits = new List<string>();
            if (!string.IsNullOrWhiteSpace(session.Plan))
                bits.Add(session.Plan);
            if (!string.IsNullOrWhiteSpace(session.Email))
                bits.Add(session.Email);
            var reset = QuotaText.ResetLabel(quotas.CycleEnd, DateTimeOffset.Now);
            if (reset.Length > 0)
                bits.Add(reset);
            var secondary = bits.Count == 0 ? "Cada barra é a quota desse pool." : string.Join(" · ", bits);
            Publish(null, line, secondary, quotas);
        }
        catch (CursorAuthException ex)
        {
            Publish("Sessão expirada", ex.Message, "Volta a entrar no Cursor. Não é preciso cookie.");
        }
        catch (CursorUsageException ex)
        {
            Publish("Sem ligação", ex.Message, null);
        }
        catch (HttpRequestException)
        {
            Publish("Sem ligação", "Não consegui falar com o Cursor.", null);
        }
        finally
        {
            Interlocked.Exchange(ref _refreshing, 0);
        }
    }

    private void Publish(string? status, string detail, string? secondary, UsageQuotas? quotas = null)
    {
        void Apply()
        {
            _detail = detail;
            _secondary = secondary;
            if (quotas is null)
                _widget.ShowStatus(status ?? detail);
            else
                _widget.ShowQuotas(quotas);
            var tip = detail.Length <= 63 ? detail : detail[..63];
            _icon.Text = tip;
            if (_widget.ContextMenuStrip is not null && quotas is not null)
                _widget.Text = detail;
        }

        if (_widget.IsHandleCreated && _widget.InvokeRequired)
            _widget.BeginInvoke(Apply);
        else
            Apply();
    }

    private static void OpenDashboard()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = "https://cursor.com/dashboard?tab=spending",
            UseShellExecute = true,
        });
    }

    protected override void ExitThreadCore()
    {
        _usageTimer.Stop();
        _placeTimer.Stop();
        _icon.Visible = false;
        _icon.Dispose();
        _iconImage.Dispose();
        _client.Dispose();
        _usageTimer.Dispose();
        _placeTimer.Dispose();
        base.ExitThreadCore();
    }

    private static Icon CreateIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(28, 28, 28));
            using var cursor = new SolidBrush(Color.FromArgb(125, 206, 160));
            using var other = new SolidBrush(Color.FromArgb(224, 177, 90));
            graphics.FillRectangle(cursor, 4, 9, 24, 5);
            graphics.FillRectangle(other, 4, 18, 15, 5);
        }

        var handle = bitmap.GetHicon();
        try
        {
            using var icon = Icon.FromHandle(handle);
            return (Icon)icon.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }
}
