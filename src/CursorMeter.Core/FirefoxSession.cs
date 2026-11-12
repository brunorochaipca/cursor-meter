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
