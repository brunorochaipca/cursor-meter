namespace CursorMeter.App;

/// <summary>
/// The settings chosen in the panel, kept in <c>settings.txt</c> beside the log. Every change is
/// saved at once and announced through <see cref="Changed"/>, so the bars, the timer and the panel
/// follow it without a restart.
/// </summary>
internal static class SettingsStore
{
    private static AppSettings _current = Load();

    /// <summary>Raised on the thread that made the change, after the new settings are in <see cref="Current"/>.</summary>
    public static event EventHandler? Changed;

    public static AppSettings Current => _current;

    // A property, not a field: _current above is built first and needs the path.
    public static string FilePath => Path.Combine(AppLog.Folder, "settings.txt");

    public static void Update(Func<AppSettings, AppSettings> change)
    {
        var next = change(_current).Normalize();
        if (next == _current)
            return;
        _current = next;
        Save();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void Reset() => Update(_ => AppSettings.Default);

    private static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath) ? AppSettings.Parse(File.ReadAllText(FilePath)) : AppSettings.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return AppSettings.Default;
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, _current.Format());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The choice still holds until the app exits.
            AppLog.Error("Could not save the settings", ex);
        }
    }
}
