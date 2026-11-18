using Xunit;

namespace CursorMeter.Tests;

public sealed class FirefoxPrefsTests
{
    [Fact]
    public void Delete_cookies_on_close_is_detected()
    {
        const string prefs = """
            user_pref("browser.startup.homepage", "about:home");
            user_pref("network.cookie.lifetimePolicy", 2);
            """;

        Assert.True(FirefoxSession.ClearsCookiesOnClose(prefs));
    }

    [Theory]
    [InlineData("privacy.clearOnShutdown.cookies")]
    [InlineData("privacy.clearOnShutdown_v2.cookiesAndStorage")]
    public void Clear_history_on_close_with_cookies_ticked_is_detected(string cookiesPref)
    {
        string prefs = $"""
            user_pref("privacy.sanitize.sanitizeOnShutdown", true);
            user_pref("{cookiesPref}", true);
            """;

        Assert.True(FirefoxSession.ClearsCookiesOnClose(prefs));
    }

    [Fact]
    public void Clearing_something_else_on_close_is_not_clearing_cookies()
    {
        const string prefs = """
            user_pref("privacy.sanitize.sanitizeOnShutdown", true);
            user_pref("privacy.clearOnShutdown.history", true);
            user_pref("privacy.clearOnShutdown.cookies", false);
            """;

        Assert.False(FirefoxSession.ClearsCookiesOnClose(prefs));
    }

    [Fact]
    public void Cookies_ticked_without_clear_on_close_is_not_clearing_cookies()
    {
        const string prefs = """
            user_pref("privacy.sanitize.sanitizeOnShutdown", false);
            user_pref("privacy.clearOnShutdown.cookies", true);
            user_pref("network.cookie.lifetimePolicy", 0);
            """;

        Assert.False(FirefoxSession.ClearsCookiesOnClose(prefs));
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void Permanent_private_browsing_is_detected(string value, bool expected)
    {
        string prefs = $"user_pref(\"browser.privatebrowsing.autostart\", {value});";

        Assert.Equal(expected, FirefoxSession.AlwaysPrivate(prefs));
    }

    [Fact]
    public void Cookie_settings_list_only_the_ones_set()
    {
        const string prefs = """
            user_pref("browser.startup.homepage", "about:home");
            user_pref("network.cookie.lifetimePolicy", 2);
            """;

        Assert.Equal("network.cookie.lifetimePolicy=2", FirefoxSession.CookieSettings(prefs));
        Assert.Equal("no cookie settings", FirefoxSession.CookieSettings(""));
    }

    [Fact]
    public void Empty_prefs_do_not_clear_cookies()
    {
        Assert.False(FirefoxSession.ClearsCookiesOnClose(""));
    }
}
