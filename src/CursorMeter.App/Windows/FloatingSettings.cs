namespace CursorMeter.App.Windows;

/// <summary>
/// What the user chose for the bars when they float above an islands taskbar: where they were
/// dropped and whether they are hidden. Kept in <c>floating.txt</c> beside the log.
/// </summary>
internal static class FloatingSettings
{
    private static FloatingState _state = Load();

    /// <summary>Raised when the bars are hidden or shown again.</summary>
    public static event EventHandler? Changed;

    public static FloatingState State => _state;

    public static void SetPosition(int left, int top)
    {
        _state = _state with { Left = left, Top = top };
        Save();
    }

    /// <summary>Forgets where the bars were dropped, so they float at the corner again.</summary>
    public static void ResetPosition()
    {
        _state = _state with { Left = null, Top = null };
        Save();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void ToggleHidden()
    {
        _state = _state with { Hidden = !_state.Hidden };
        Save();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static MenuEntry MenuEntry() => new("Esconder as barras flutuantes", ToggleHidden, () => _state.Hidden);

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CursorMeter",
        "floating.txt");

    private static FloatingState Load()
    {
        try
        {
            return File.Exists(FilePath) ? FloatingState.Parse(File.ReadAllText(FilePath)) : default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return default;
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, _state.Format());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The choice still holds until the app exits.
        }
    }
}
