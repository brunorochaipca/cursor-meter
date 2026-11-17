using Xunit;

namespace CursorMeter.Tests;

public sealed class AppSettingsTests
{
    [Fact]
    public void Nothing_saved_gives_the_defaults()
    {
        Assert.Equal(AppSettings.Default, AppSettings.Parse(null));
        Assert.Equal(AppSettings.Default, AppSettings.Parse(""));
        Assert.Equal(BarTheme.Auto, AppSettings.Default.Theme);
        Assert.Equal(3, AppSettings.Default.RefreshMinutes);
        Assert.Equal((70, 90), (AppSettings.Default.WarnAt, AppSettings.Default.HotAt));
    }

    [Fact]
    public void Settings_survive_a_round_trip()
    {
        var settings = new AppSettings
        {
            Theme = BarTheme.Transparent,
            BackgroundOpacity = 35,
            TextColor = "#FFEEDD",
            HotColor = "#FF0000",
            TextScale = 90,
            Outline = false,
            Radius = 8,
            RefreshMinutes = 10,
            WarnAt = 60,
            HotAt = 85,
            FadedOpacity = 25,
            Pools = PoolView.CursorOnly,
            ShowRemaining = true,
        };

        Assert.Equal(settings, AppSettings.Parse(settings.Format()));
    }

    [Fact]
    public void Colours_are_kept_in_capitals_and_bad_ones_are_dropped()
    {
        var parsed = AppSettings.Parse("TextColor=#ffeedd\nMutedColor=red\nCursorColor=#12345\nOtherColor=#GG0000\nHotColor= #aabbcc ");

        Assert.Equal("#FFEEDD", parsed.TextColor);
        Assert.Null(parsed.MutedColor);
        Assert.Null(parsed.CursorColor);
        Assert.Null(parsed.OtherColor);
        Assert.Equal("#AABBCC", parsed.HotColor);
    }

    [Fact]
    public void Numbers_are_brought_into_range()
    {
        var parsed = AppSettings.Parse("BackgroundOpacity=400\nTextScale=10\nRadius=99\nRefreshMinutes=0\nFadedOpacity=0");

        Assert.Equal(100, parsed.BackgroundOpacity);
        Assert.Equal(80, parsed.TextScale);
        Assert.Equal(16, parsed.Radius);
        Assert.Equal(1, parsed.RefreshMinutes);
        Assert.Equal(10, parsed.FadedOpacity);
    }

    [Fact]
    public void The_critical_threshold_stays_above_the_warning_one()
    {
        var parsed = AppSettings.Parse("WarnAt=95\nHotAt=50");

        Assert.Equal(95, parsed.WarnAt);
        Assert.Equal(96, parsed.HotAt);
        Assert.Equal((98, 99), (AppSettings.Parse("WarnAt=200").WarnAt, AppSettings.Parse("WarnAt=200").HotAt));
    }

    [Fact]
    public void Garbage_and_unknown_lines_fall_back_to_the_defaults()
    {
        var parsed = AppSettings.Parse("Theme=Neon\nPools=7\nOutline=maybe\nRadius=wide\nFuture=1\n=\nnot a line\n\u0000");

        Assert.Equal(AppSettings.Default, parsed);
    }

    [Fact]
    public void Names_and_booleans_are_read_loosely()
    {
        var parsed = AppSettings.Parse("theme=transparent\nPOOLS=otheronly\nOutline=false\nShowRemaining=true\r\nWarnAt = 50 \r\n");

        Assert.Equal(BarTheme.Transparent, parsed.Theme);
        Assert.Equal(PoolView.OtherOnly, parsed.Pools);
        Assert.False(parsed.Outline);
        Assert.True(parsed.ShowRemaining);
        Assert.Equal(50, parsed.WarnAt);
    }

    [Theory]
    [InlineData(69.9, QuotaTone.Ok)]
    [InlineData(70, QuotaTone.Warn)]
    [InlineData(89.9, QuotaTone.Warn)]
    [InlineData(90, QuotaTone.Hot)]
    [InlineData(double.NaN, QuotaTone.Unknown)]
    public void The_default_thresholds_are_the_ones_the_bars_always_used(double percent, QuotaTone tone)
        => Assert.Equal(tone, UsageQuotas.ToneOf(percent));

    [Fact]
    public void Thresholds_can_be_moved()
    {
        Assert.Equal(QuotaTone.Ok, UsageQuotas.ToneOf(49, 50, 80));
        Assert.Equal(QuotaTone.Warn, UsageQuotas.ToneOf(50, 50, 80));
        Assert.Equal(QuotaTone.Hot, UsageQuotas.ToneOf(80, 50, 80));
    }
}
