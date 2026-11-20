namespace CursorMeter.App.Linux;

/// <summary>
/// The hand-off to the GNOME Shell extension that draws the bars inside the taskbar. The app writes
/// <c>bars.json</c> under <c>$XDG_RUNTIME_DIR/cursormeter</c>; the extension creates <c>panel-extension</c>
/// there while it is enabled, which is how the app knows to stop writing the percentages as tray text.
/// </summary>
internal sealed class PanelFeedWriter : IDisposable
{
    public const string FeedFileName = "bars.json";
    public const string MarkerFileName = "panel-extension";

    private readonly string _feedPath;
    private readonly string _markerPath;
    private readonly FileSystemWatcher _watcher;
    private bool _warned;
    private bool _disposed;

    private PanelFeedWriter(string directory)
    {
        _feedPath = Path.Combine(directory, FeedFileName);
        _markerPath = Path.Combine(directory, MarkerFileName);
        _watcher = new FileSystemWatcher(directory, MarkerFileName)
        {
            NotifyFilter = NotifyFilters.FileName,
        };
        _watcher.Created += (_, _) => ExtensionChanged?.Invoke(this, EventArgs.Empty);
        _watcher.Deleted += (_, _) => ExtensionChanged?.Invoke(this, EventArgs.Empty);
        _watcher.Renamed += (_, _) => ExtensionChanged?.Invoke(this, EventArgs.Empty);
        _watcher.EnableRaisingEvents = true;
    }

    /// <summary>Raised, on a background thread, when the extension is enabled or disabled.</summary>
    public event EventHandler? ExtensionChanged;

    /// <summary>True while the extension is enabled and so draws the bars itself.</summary>
    public bool ExtensionActive => File.Exists(_markerPath);

    /// <summary>Where the extension's bars are on the screen, or null while it is off or has not placed them yet.</summary>
    public Avalonia.PixelRect? ReadAnchor()
    {
        try
        {
            if (PanelAnchor.TryParse(File.ReadAllText(_markerPath), out var anchor))
                return new Avalonia.PixelRect(anchor.X, anchor.Y, anchor.Width, anchor.Height);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return null;
    }

    /// <summary>The directory is under the session's runtime dir; null when there is none or it cannot be used.</summary>
    public static PanelFeedWriter? TryCreate()
    {
        var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrEmpty(runtime) || !Directory.Exists(runtime))
            return null;

        try
        {
            var directory = Directory.CreateDirectory(Path.Combine(runtime, "cursormeter")).FullName;
            return new PanelFeedWriter(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            AppLog.Info("Panel feed unavailable: " + ex.Message);
            return null;
        }
    }

    /// <summary>Replaces the file in one step, so the extension never reads half a document.</summary>
    public void Write(string json)
    {
        if (_disposed)
            return;
        try
        {
            var temp = _feedPath + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _feedPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (_warned)
                return;
            _warned = true;
            AppLog.Info("Could not write the panel feed: " + ex.Message);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _watcher.Dispose();
        try
        {
            File.Delete(_feedPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
