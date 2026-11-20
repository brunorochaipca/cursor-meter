using Xunit;

namespace CursorMeter.Tests;

public sealed class FirefoxProfileRootsTests
{
    private const string Home = "/home/bruno";
    private const string Config = "/home/bruno/.config";

    private static string[] Roots() => FirefoxSession.LinuxProfileRoots(Home, Config).ToArray();

    [Fact]
    public void Looks_in_the_Flatpak_XDG_profile_folder()
    {
        // Firefox 160 as a Flatpak keeps its profiles here, not under .mozilla.
        Assert.Contains(Path.Combine(Home, ".var", "app", "org.mozilla.firefox", "config", "mozilla", "firefox"), Roots());
    }

    [Fact]
    public void Still_looks_in_the_older_Flatpak_folder()
    {
        Assert.Contains(Path.Combine(Home, ".var", "app", "org.mozilla.firefox", ".mozilla", "firefox"), Roots());
    }

    [Fact]
    public void Still_looks_in_the_package_snap_and_xdg_folders()
    {
        var roots = Roots();
        Assert.Contains(Path.Combine(Home, ".mozilla", "firefox"), roots);
        Assert.Contains(Path.Combine(Home, "snap", "firefox", "common", ".mozilla", "firefox"), roots);
        Assert.Contains(Path.Combine(Config, "mozilla", "firefox"), roots);
    }

    [Fact]
    public void Has_no_duplicate_folders()
    {
        var roots = Roots();
        Assert.Equal(roots.Length, roots.Distinct(StringComparer.Ordinal).Count());
    }
}
