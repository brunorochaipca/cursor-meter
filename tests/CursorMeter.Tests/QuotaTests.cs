using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CursorMeter.Tests;

public class QuotaTests
{
    [Fact]
    public void Line_uses_the_two_pools()
    {
        var quotas = new UsageQuotas(37.2, 64.4, null);
        Assert.Equal("37% Cursor Models | 64% Other Models", QuotaText.Line(quotas));
    }

    [Fact]
    public void Format_keeps_a_sliver_visible()
    {
        Assert.Equal("<1%", QuotaText.Format(0.2));
        Assert.Equal("1%", QuotaText.Format(0.71));
        Assert.Equal("3%", QuotaText.Format(3.3));
        Assert.Equal("112%", QuotaText.Format(112.2));
    }

    [Fact]
    public void Parse_maps_auto_to_cursor_models_and_api_to_other_models()
    {
        const string json = """
            {
              "billingCycleEnd": "1789718953000",
              "planUsage": {
                "autoPercentUsed": 3.297777777777778,
                "apiPercentUsed": 0.7111111111111111,
                "totalPercentUsed": 3.062626262626263
              }
            }
            """;

        var quotas = UsageParser.Parse(json);

        Assert.Equal(3.297777777777778, quotas.CursorModelsPercent);
        Assert.Equal(0.7111111111111111, quotas.OtherModelsPercent);
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1789718953000), quotas.CycleEnd);
        Assert.Equal("3% Cursor Models | 1% Other Models", QuotaText.Line(quotas));
    }

    [Fact]
    public void Reset_label_counts_days()
    {
        var end = DateTimeOffset.Parse("2026-11-20T00:00:00Z");
        var now = DateTimeOffset.Parse("2026-11-11T21:00:00Z");
        Assert.Equal("reinicia em 9 dias", QuotaText.ResetLabel(end, now));
        Assert.Equal("reinicia amanhã", QuotaText.ResetLabel(now.AddHours(20), now));
        Assert.Equal("reinicia hoje", QuotaText.ResetLabel(now.AddHours(-1), now));
    }

    [Fact]
    public void Tones_follow_the_thresholds()
    {
        Assert.Equal(QuotaTone.Ok, UsageQuotas.ToneOf(69));
        Assert.Equal(QuotaTone.Warn, UsageQuotas.ToneOf(70));
        Assert.Equal(QuotaTone.Hot, UsageQuotas.ToneOf(90));
    }

    [Fact]
    public void Place_sits_left_of_the_tray_on_a_bottom_taskbar()
    {
        var screen = new PxRect(0, 0, 1920, 1080);
        var taskbar = new PxRect(0, 1040, 1920, 1080);
        var tray = new PxRect(1680, 1040, 1910, 1080);

        var edge = TaskbarLayout.DetectEdge(taskbar, screen);
        var slot = TaskbarLayout.Place(taskbar, tray, edge, delta: 0);

        Assert.Equal(BarEdge.Bottom, edge);
        Assert.Equal(1680 - TaskbarLayout.Gap - TaskbarLayout.PreferredWidth, slot.Left);
        Assert.Equal(1040 + (40 - slot.Height) / 2, slot.Top);
        Assert.True(slot.Right <= tray.Left);
    }

    [Fact]
    public void Place_clamps_when_the_drag_offset_would_leave_the_taskbar()
    {
        var taskbar = new PxRect(0, 1040, 1920, 1080);
        var tray = new PxRect(1680, 1040, 1910, 1080);
        var slot = TaskbarLayout.Place(taskbar, tray, BarEdge.Bottom, delta: 5000);
        Assert.True(slot.Right <= taskbar.Right - 2);
        Assert.True(slot.Left >= taskbar.Left + 2);
    }

    [Fact]
    public void Missing_login_is_absent_instead_of_throwing()
    {
        var path = Path.Combine(Path.GetTempPath(), "cursormeter-" + Guid.NewGuid().ToString("n") + ".vscdb");
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "CREATE TABLE ItemTable (key TEXT PRIMARY KEY, value TEXT);";
            command.ExecuteNonQuery();
        }

        try
        {
            Assert.Null(CursorSession.TryLoadDatabase(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Session_reads_the_cursor_login_without_a_cookie()
    {
        var path = Path.Combine(Path.GetTempPath(), "cursormeter-" + Guid.NewGuid().ToString("n") + ".vscdb");
        using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE ItemTable (key TEXT PRIMARY KEY, value TEXT);
                INSERT INTO ItemTable (key, value) VALUES
                  ('cursorAuth/accessToken', 'app-token'),
                  ('cursorAuth/refreshToken', 'refresh-token'),
                  ('cursorAuth/cachedEmail', 'ana@example.com'),
                  ('cursorAuth/stripeMembershipType', 'pro');
                """;
            command.ExecuteNonQuery();
        }

        try
        {
            var session = CursorSession.ReadDatabase(path);
            Assert.Equal("app-token", session.AccessToken);
            Assert.Equal("refresh-token", session.RefreshToken);
            Assert.Equal("ana@example.com", session.Email);
            Assert.Equal("pro", session.Plan);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Client_sends_the_app_token_as_bearer_and_reads_both_pools()
    {
        var handler = new ScriptedHandler(request =>
        {
            Assert.Equal(UsageClient.UsageUrl, request.RequestUri?.ToString());
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("app-token", request.Headers.Authorization?.Parameter);
            Assert.True(request.Headers.TryGetValues("Connect-Protocol-Version", out var version));
            Assert.Equal("1", version!.Single());
            return Json("""
                {"planUsage":{"autoPercentUsed":37.4,"apiPercentUsed":64.2},"billingCycleEnd":"1700000000000"}
                """);
        });

        using var client = new UsageClient(handler);
        var quotas = await client.FetchAsync(new CursorSession("app-token", null, null, null));

        Assert.Equal("37% Cursor Models | 64% Other Models", QuotaText.Line(quotas));
        Assert.DoesNotContain("cookie", handler.Bodies[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Client_refreshes_once_when_the_app_token_is_rejected()
    {
        var calls = 0;
        var handler = new ScriptedHandler(request =>
        {
            calls++;
            if (calls == 1)
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            if (request.RequestUri!.AbsolutePath.EndsWith("/oauth/token", StringComparison.Ordinal))
                return Json("""{"access_token":"renewed"}""");
            Assert.Equal("renewed", request.Headers.Authorization?.Parameter);
            return Json("""{"planUsage":{"autoPercentUsed":12,"apiPercentUsed":8}}""");
        });

        using var client = new UsageClient(handler);
        var quotas = await client.FetchAsync(new CursorSession("stale", "refresh", null, null));

        Assert.Equal(12, quotas.CursorModelsPercent);
        Assert.Equal(8, quotas.OtherModelsPercent);
        Assert.Equal(3, calls);
    }

    private static HttpResponseMessage Json(string json)
        => new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };

    private sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _next;
        public List<string> Bodies { get; } = [];

        public ScriptedHandler(Func<HttpRequestMessage, HttpResponseMessage> next) => _next = next;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
            return _next(request);
        }
    }
}
