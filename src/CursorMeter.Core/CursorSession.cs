using Microsoft.Data.Sqlite;

namespace CursorMeter;

public sealed record CursorSession(string AccessToken, string? RefreshToken, string? Email, string? Plan)
{
    public static string WindowsDatabasePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Cursor", "User", "globalStorage", "state.vscdb");
    }

    public static string WindowsCliAuthPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Cursor", "auth.json");
    }

    public static CursorSession? TryLoadDefault()
    {
        try
        {
            return TryLoadDatabase(WindowsDatabasePath()) ?? TryLoadCliAuth(WindowsCliAuthPath());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or SqliteException or CursorAuthException)
        {
            return null;
        }
    }

    public static CursorSession? TryLoadDatabase(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        try
        {
            return ReadDatabase(path);
        }
        catch (CursorAuthException)
        {
            return null;
        }
        catch (SqliteException)
        {
            var copy = CopyToTemp(path);
            try
            {
                return ReadDatabase(copy);
            }
            catch (Exception ex) when (ex is SqliteException or CursorAuthException)
            {
                return null;
            }
            finally
            {
                TryDelete(copy);
            }
        }
    }

    public static CursorSession? TryLoadCliAuth(string path)
    {
        if (!File.Exists(path))
            return null;
        using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        var token = ReadString(root, "accessToken");
        if (string.IsNullOrWhiteSpace(token))
            return null;
        return new CursorSession(token, ReadString(root, "refreshToken"), ReadString(root, "email"), null);
    }

    public static CursorSession ReadDatabase(string path)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Cache = SqliteCacheMode.Private,
        };
        using var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        var token = ReadItem(connection, "cursorAuth/accessToken");
        if (string.IsNullOrWhiteSpace(token))
            throw new CursorAuthException("O Cursor está instalado, mas não há sessão. Inicia sessão no Cursor.");
        return new CursorSession(
            token.Trim(),
            NullIfEmpty(ReadItem(connection, "cursorAuth/refreshToken")),
            NullIfEmpty(ReadItem(connection, "cursorAuth/cachedEmail")),
            NullIfEmpty(ReadItem(connection, "cursorAuth/stripeMembershipType")));
    }

    private static string? ReadItem(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM ItemTable WHERE key = $key LIMIT 1";
        command.Parameters.AddWithValue("$key", key);
        var value = command.ExecuteScalar();
        return value switch
        {
            string text => text,
            byte[] bytes => System.Text.Encoding.UTF8.GetString(bytes),
            _ => null,
        };
    }

    private static string? ReadString(System.Text.Json.JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != System.Text.Json.JsonValueKind.String)
            return null;
        return value.GetString();
    }

    private static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string CopyToTemp(string path)
    {
        var copy = Path.Combine(Path.GetTempPath(), "cursormeter-" + Guid.NewGuid().ToString("n") + ".vscdb");
        File.Copy(path, copy, overwrite: true);
        var wal = path + "-wal";
        var shm = path + "-shm";
        if (File.Exists(wal))
            File.Copy(wal, copy + "-wal", overwrite: true);
        if (File.Exists(shm))
            File.Copy(shm, copy + "-shm", overwrite: true);
        return copy;
    }

    private static void TryDelete(string path)
    {
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
