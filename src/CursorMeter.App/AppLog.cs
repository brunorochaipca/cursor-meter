using System.Text;

namespace CursorMeter.App;

/// <summary>
/// A small diagnostic log in the app's data folder. It records events (the taskbar being rebuilt,
/// a layout change, unexpected errors), never usage data, the account or tokens. Logging never throws.
/// </summary>
internal static class AppLog
{
    private const long MaxBytes = 256 * 1024;
    private static readonly object Gate = new();

    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CursorMeter");

    public static string FilePath { get; } = Path.Combine(Folder, "cursormeter.log");

    public static void Info(string message) => Write("INFO ", message);

    public static void Error(string message, Exception? exception = null)
        => Write("ERROR", exception is null ? message : $"{message}{Environment.NewLine}{exception}");

    /// <summary>The last <paramref name="lines"/> lines of the log, empty when there is none.</summary>
    public static string Tail(int lines)
    {
        try
        {
            lock (Gate)
            {
                if (!File.Exists(FilePath))
                    return "";
                using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var last = new Queue<string>();
                while (reader.ReadLine() is { } line)
                {
                    if (last.Count == lines)
                        last.Dequeue();
                    last.Enqueue(line);
                }

                return string.Join(Environment.NewLine, last);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    /// <summary>Records errors that would otherwise vanish, or end the process without a trace.</summary>
    public static void HookUnhandledErrors()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Error(e.IsTerminating ? "Unhandled exception, the process ends" : "Unhandled exception", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Error("Unobserved task exception", e.Exception);
        Avalonia.Threading.Dispatcher.UIThread.UnhandledException += (_, e) => Error("Unhandled UI exception", e.Exception);
    }

    private static void Write(string level, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Folder);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                    File.Move(FilePath, FilePath + ".1", overwrite: true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A full disk or a locked file must not take the app down.
        }
    }
}
