using System.Text.Json;
using Xunit;

namespace CursorMeter.Tests;

public sealed class PanelFeedTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 20, 1, 0, 0, TimeSpan.Zero);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public void Quotas_become_one_pool_each_with_text_fill_and_tone()
    {
        var feed = Parse(PanelFeed.Build(null, "detalhe", new UsageQuotas(20, 100, null), AppSettings.Default, 4242, Now));

        Assert.Equal(PanelFeed.Version, feed.GetProperty("version").GetInt32());
        Assert.Equal(4242, feed.GetProperty("pid").GetInt32());
        Assert.Equal(Now.ToUnixTimeSeconds(), feed.GetProperty("updated").GetInt64());
        Assert.Equal(JsonValueKind.Null, feed.GetProperty("status").ValueKind);
        Assert.Equal("detalhe", feed.GetProperty("detail").GetString());

        var pools = feed.GetProperty("pools").EnumerateArray().ToArray();
        Assert.Equal(2, pools.Length);
        Assert.Equal(("cursor", "Cursor Models", "20%", "ok"), Describe(pools[0]));
        Assert.Equal(0.2, pools[0].GetProperty("fill").GetDouble(), 3);
        Assert.Equal(("other", "Other Models", "100%", "hot"), Describe(pools[1]));
        Assert.Equal(1.0, pools[1].GetProperty("fill").GetDouble(), 3);
    }

    private static (string?, string?, string?, string?) Describe(JsonElement pool) => (
        pool.GetProperty("id").GetString(),
        pool.GetProperty("label").GetString(),
        pool.GetProperty("text").GetString(),
        pool.GetProperty("tone").GetString());

    [Fact]
    public void Without_quotas_it_carries_the_status_and_no_pools()
    {
        var feed = Parse(PanelFeed.Build("Abre o Cursor", "Sem sessão.", null, AppSettings.Default, 1, Now));

        Assert.Equal("Abre o Cursor", feed.GetProperty("status").GetString());
        Assert.Empty(feed.GetProperty("pools").EnumerateArray());
    }

    [Theory]
    [InlineData(PoolView.CursorOnly, "cursor")]
    [InlineData(PoolView.OtherOnly, "other")]
    public void The_pool_choice_limits_the_pools(PoolView view, string expected)
    {
        var settings = AppSettings.Default with { Pools = view };
        var feed = Parse(PanelFeed.Build(null, "", new UsageQuotas(10, 10, null), settings, 1, Now));

        string[] ids = feed.GetProperty("pools").EnumerateArray().Select(p => p.GetProperty("id").GetString()!).ToArray();
        Assert.Equal([expected], ids);
    }

    [Fact]
    public void The_thresholds_come_from_the_settings()
    {
        var settings = AppSettings.Default with { WarnAt = 40, HotAt = 60 };
        var feed = Parse(PanelFeed.Build(null, "", new UsageQuotas(45, 65, null), settings, 1, Now));

        string[] tones = feed.GetProperty("pools").EnumerateArray().Select(p => p.GetProperty("tone").GetString()!).ToArray();
        Assert.Equal(["warn", "hot"], tones);
    }

    [Fact]
    public void Show_remaining_flips_the_text_and_fill_but_not_the_tone()
    {
        var settings = AppSettings.Default with { ShowRemaining = true };
        var feed = Parse(PanelFeed.Build(null, "", new UsageQuotas(95, 5, null), settings, 1, Now));

        var first = feed.GetProperty("pools")[0];
        Assert.Equal("5%", first.GetProperty("text").GetString());
        Assert.Equal(0.05, first.GetProperty("fill").GetDouble(), 3);
        Assert.Equal("hot", first.GetProperty("tone").GetString());
    }

    [Fact]
    public void Colours_are_passed_only_when_the_user_chose_them()
    {
        var settings = AppSettings.Default with { CursorColor = "#112233", HotColor = "not a colour" };
        var colors = Parse(PanelFeed.Build(null, "", new UsageQuotas(1, 1, null), settings, 1, Now)).GetProperty("colors");

        Assert.Equal("#112233", colors.GetProperty("cursor").GetString());
        Assert.Equal(JsonValueKind.Null, colors.GetProperty("hot").ValueKind);
        Assert.Equal(JsonValueKind.Null, colors.GetProperty("text").ValueKind);
    }

    [Fact]
    public void The_text_size_and_the_space_between_the_bars_are_passed_on()
    {
        var settings = AppSettings.Default with { TextScale = 90, BarSpacing = 7 };
        var feed = Parse(PanelFeed.Build(null, "", new UsageQuotas(1, 1, null), settings, 1, Now));

        Assert.Equal(90, feed.GetProperty("textScale").GetInt32());
        Assert.Equal(7, feed.GetProperty("barSpacing").GetInt32());
        Assert.Equal(AppSettings.DefaultBarSpacing,
            Parse(PanelFeed.Build(null, "", null, AppSettings.Default, 1, Now)).GetProperty("barSpacing").GetInt32());
    }

    [Fact]
    public void An_unknown_percentage_reads_as_a_dash_with_an_empty_bar()
    {
        var feed = Parse(PanelFeed.Build(null, "", new UsageQuotas(double.NaN, 0, null), AppSettings.Default, 1, Now));

        var first = feed.GetProperty("pools")[0];
        Assert.Equal("—%", first.GetProperty("text").GetString());
        Assert.Equal(0, first.GetProperty("fill").GetDouble());
        Assert.Equal("unknown", first.GetProperty("tone").GetString());
    }
}
