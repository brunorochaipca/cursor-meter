using Microsoft.Data.Sqlite;

namespace CursorMeter;

public static class FirefoxSession
{
    public const string CookieName = "WorkosCursorSessionToken";

    public static string? TryLoadDefault()
    {
        string? best = null;
        long bestExpiry = long.MinValue;
        foreach (var path in CookieDatabases())
        {
            StoredCookie? found;
            try
            {
                found = TryReadBest(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
            {
                continue;
            }

            if (found is not { } cookie || IsExpired(cookie.Expiry))
                continue;
            var expiry = Rank(cookie.Expiry);
            if (expiry < bestExpiry)
                continue;
            bestExpiry = expiry;
            best = cookie.Value;
        }

        return best;
    }

    /// <summary>What the Firefox profiles say about reading a Cursor session from them.</summary>
    /// <param name="ProfileFound">A Firefox profile exists for this user.</param>
    /// <param name="CookieFound">A usable Cursor cookie is stored in a profile.</param>
    /// <param name="ClearsCookiesOnClose">Firefox is set to forget cookies when it closes, so none are ever written to disk.</param>
    /// <param name="AlwaysPrivate">Firefox always starts in private browsing, which never writes cookies to disk.</param>
    public sealed record FirefoxStatus(bool ProfileFound, bool CookieFound, bool ClearsCookiesOnClose, bool AlwaysPrivate = false);

    private static readonly string[] CookiePrefs =
    [
        "network.cookie.lifetimePolicy",
        "network.cookie.cookieBehavior",
        "privacy.sanitize.sanitizeOnShutdown",
        "privacy.clearOnShutdown.cookies",
        "privacy.clearOnShutdown_v2.cookiesAndStorage",
        "browser.privatebrowsing.autostart",
    ];

    public static FirefoxStatus Inspect()
    {
        bool profileFound = false;
        bool clears = OperatingSystem.IsWindows() && PolicyClearsCookies();
        bool alwaysPrivate = false;
        foreach (var root in ProfileRoots().Distinct(StringComparer.Ordinal))
        {
            string[] profiles;
            try
            {
                if (!Directory.Exists(root))
                    continue;
                profiles = Directory.GetDirectories(root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var profile in profiles)
            {
                var prefs = Path.Combine(profile, "prefs.js");
                bool hasPrefs = File.Exists(prefs);
                if (!hasPrefs && !File.Exists(Path.Combine(profile, "cookies.sqlite")))
                    continue;
                profileFound = true;
                if (!hasPrefs)
                    continue;
                try
                {
                    var text = File.ReadAllText(prefs);
                    if (ClearsCookiesOnClose(text))
                        clears = true;
                    if (AlwaysPrivate(text))
                        alwaysPrivate = true;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        return new FirefoxStatus(profileFound, TryLoadDefault() is not null, clears, alwaysPrivate);
    }

    /// <summary>
    /// The "Delete cookies when Firefox closes" and "Clear history when Firefox closes" options can
    /// also be set by the administrator with a policy, which never shows up in <c>prefs.js</c>.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static bool PolicyClearsCookies()
    {
        try
        {
            foreach (var hive in new[] { Microsoft.Win32.Registry.LocalMachine, Microsoft.Win32.Registry.CurrentUser })
            {
                using var key = hive.OpenSubKey(@"SOFTWARE\Policies\Mozilla\Firefox");
                if (key is null)
                    continue;
                if (key.GetValue("SanitizeOnShutdown") is 1)
                    return true;
                using var sanitize = key.OpenSubKey("SanitizeOnShutdown");
                if (sanitize is not null && (sanitize.GetValue("Cookies") is 1 || sanitize.GetValue("Everything") is 1))
                    return true;
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }

        return false;
    }

    private static Dictionary<string, string> ParsePrefs(string prefs)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                     prefs, @"user_pref\(\s*""([^""]+)""\s*,\s*([^)]*?)\s*\)\s*;"))
            values[match.Groups[1].Value] = match.Groups[2].Value.Trim();
        return values;
    }

    /// <summary>True when a <c>prefs.js</c> makes Firefox start in permanent private browsing.</summary>
    public static bool AlwaysPrivate(string prefs)
        => ParsePrefs(prefs).TryGetValue("browser.privatebrowsing.autostart", out var value) && value == "true";

    /// <summary>The cookie settings found in a <c>prefs.js</c>, as <c>name=value</c>, for the log.</summary>
    public static string CookieSettings(string prefs)
    {
        var values = ParsePrefs(prefs);
        var found = CookiePrefs.Where(values.ContainsKey).Select(name => $"{name}={values[name]}").ToList();
        return found.Count == 0 ? "no cookie settings" : string.Join(", ", found);
    }

    /// <summary>
    /// True when the contents of a <c>prefs.js</c> tell Firefox to clear cookies on exit: the old
    /// cookie lifetime policy, or the "clear history when Firefox closes" option with cookies ticked.
    /// </summary>
    public static bool ClearsCookiesOnClose(string prefs)
    {
        var values = ParsePrefs(prefs);

        bool On(string name) => values.TryGetValue(name, out var value) && value == "true";

        if (values.TryGetValue("network.cookie.lifetimePolicy", out var policy) && policy == "2")
            return true;
        return On("privacy.sanitize.sanitizeOnShutdown")
            && (On("privacy.clearOnShutdown.cookies") || On("privacy.clearOnShutdown_v2.cookiesAndStorage"));
    }

    /// <summary>
    /// Says why no session was found, for the log: which profile folders exist, which cookie files could
    /// be opened, and what the Cursor cookie in them looks like. Never includes the cookie value.
    /// </summary>
    public static string Diagnose()
    {
        var lines = new List<string> { $"user {Environment.UserName}, APPDATA {Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify)}" };
        foreach (var root in ProfileRoots().Distinct(StringComparer.Ordinal))
        {
            if (!Directory.Exists(root))
            {
                lines.Add($"no Firefox folder at {root}");
                continue;
            }

            string[] profiles;
            try
            {
                profiles = Directory.GetDirectories(root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                lines.Add($"cannot list {root}: {ex.Message}");
                continue;
            }

            lines.Add($"{profiles.Length} profile(s) in {root}");
            foreach (var profile in profiles)
            {
                var path = Path.Combine(profile, "cookies.sqlite");
                if (!File.Exists(path))
                {
                    lines.Add($"{Path.GetFileName(profile)}: no cookies.sqlite");
                    continue;
                }

                lines.Add($"{Path.GetFileName(profile)}: {DescribeCookies(path)} [{DescribePrefs(Path.Combine(profile, "prefs.js"))}]");
            }
        }

        if (OperatingSystem.IsWindows())
            lines.Add("policy clears cookies: " + PolicyClearsCookies());
        return string.Join("; ", lines);
    }

    private static string DescribePrefs(string path)
    {
        try
        {
            return File.Exists(path) ? CookieSettings(File.ReadAllText(path)) : "no prefs.js";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "cannot read prefs.js: " + ex.Message;
        }
    }

    private static string DescribeCookies(string path)
    {
        string? copy = null;
        try
        {
            string source = path;
            try
            {
                return Describe(source);
            }
            catch (SqliteException)
            {
                copy = CopyToTemp(path);
                return Describe(copy);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            return "cannot read: " + ex.Message;
        }
        finally
        {
            if (copy is not null)
                TryDelete(copy);
        }

        static string Describe(string file)
        {
            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = file,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Private,
                Pooling = false,
            };
            using var connection = new SqliteConnection(builder.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT host, expiry FROM moz_cookies WHERE name = $name";
            command.Parameters.AddWithValue("$name", CookieName);
            var found = new List<string>();
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                long expiry = reader.IsDBNull(1) ? 0 : reader.GetInt64(1);
                string state = expiry <= 0 ? "session" : IsExpired(expiry) ? "expired" : "valid";
                found.Add($"{(reader.IsDBNull(0) ? "?" : reader.GetString(0))} {state}");
            }

            return found.Count == 0 ? "opened, no Cursor cookie" : "cookie " + string.Join(", ", found);
        }
    }

    public static IEnumerable<string> ProfileRoots()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        if (OperatingSystem.IsWindows())
        {
            yield return Path.Combine(appData, "Mozilla", "Firefox", "Profiles");
            yield break;
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile, Environment.SpecialFolderOption.DoNotVerify);
        yield return Path.Combine(home, ".mozilla", "firefox");
        yield return Path.Combine(home, "snap", "firefox", "common", ".mozilla", "firefox");
        yield return Path.Combine(home, ".var", "app", "org.mozilla.firefox", ".mozilla", "firefox");
        yield return Path.Combine(appData, "mozilla", "firefox");
    }

    private static IEnumerable<string> CookieDatabases()
    {
        foreach (var root in ProfileRoots().Distinct(StringComparer.Ordinal))
        {
            string[] profiles;
            try
            {
                if (!Directory.Exists(root))
                    continue;
                profiles = Directory.GetDirectories(root);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var profile in profiles)
            {
                var path = Path.Combine(profile, "cookies.sqlite");
                if (File.Exists(path))
                    yield return path;
            }
        }
    }

    public static string? TryReadDatabase(string path)
    {
        if (TryReadBest(path) is not { } cookie || IsExpired(cookie.Expiry))
            return null;
        return cookie.Value;
    }

    private readonly record struct StoredCookie(string Value, long Expiry);

    private static StoredCookie? TryReadBest(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            return Query(path);
        }
        catch (SqliteException)
        {
            var copy = CopyToTemp(path);
            try
            {
                return Query(copy);
            }
            catch (SqliteException)
            {
                return null;
            }
            finally
            {
                TryDelete(copy);
            }
        }
    }

    private static StoredCookie? Query(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
            Pooling = false,
        };
        using var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT host, value, expiry
            FROM moz_cookies
            WHERE name = $name
            """;
        command.Parameters.AddWithValue("$name", CookieName);

        StoredCookie? best = null;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (reader.IsDBNull(0) || reader.IsDBNull(1))
                continue;
            var host = reader.GetString(0);
            if (!IsCursorHost(host))
                continue;
            var value = reader.GetString(1).Trim();
            if (!IsUsableCookie(value))
                continue;
            var expiry = reader.IsDBNull(2) ? 0 : reader.GetInt64(2);
            if (IsExpired(expiry))
                continue;
            if (best is null || Rank(expiry) >= Rank(best.Value.Expiry))
                best = new StoredCookie(value, expiry);
        }

        return best;
    }

    private static bool IsCursorHost(string host)
        => host.Equals("cursor.com", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".cursor.com", StringComparison.OrdinalIgnoreCase);

    private static bool IsUsableCookie(string value)
        => value.Length > 0
            && !value.Contains(';')
            && !value.Contains('\r')
            && !value.Contains('\n');

    private static bool IsExpired(long expiry)
        => expiry > 0 && Rank(expiry) <= DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static long Rank(long expiry)
    {
        if (expiry <= 0)
            return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return expiry > 100_000_000_000 ? expiry / 1000 : expiry;
    }

    private static string CopyToTemp(string path)
    {
        var copy = Path.Combine(Path.GetTempPath(), "cursormeter-" + Guid.NewGuid().ToString("n") + ".sqlite");
        File.Copy(path, copy, overwrite: true);
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            var extra = path + suffix;
            if (File.Exists(extra))
                File.Copy(extra, copy + suffix, overwrite: true);
        }

        return copy;
    }

    private static void TryDelete(string path)
    {
        SqliteConnection.ClearAllPools();
        foreach (var candidate in new[] { path, path + "-wal", path + "-shm" })
        {
            try
            {
                if (File.Exists(candidate))
                    File.Delete(candidate);
            }
            catch (IOException)
            {
            }
        }
    }
}
