using Avalonia;
using Avalonia.Controls;

namespace CursorMeter.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // On Unix, Local\ is scoped to the terminal session, so the desktop menu, autostart and a shell would each get their own.
        var name = OperatingSystem.IsWindows() ? @"Local\CursorMeter" : @"Global\CursorMeter-" + Environment.UserName;
        if (IsAlreadyRunning(name))
            return 0;

        if (OperatingSystem.IsWindows() && Windows.UiAccess.RelaunchIfNeeded(args))
            return 0;

        using var mutex = new Mutex(true, name, out var created);
        if (!created)
            return 0;

        var code = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        mutex.ReleaseMutex();
        return code;
    }

    private static bool IsAlreadyRunning(string mutexName)
    {
        try
        {
            if (Mutex.TryOpenExisting(mutexName, out var existing))
            {
                existing.Dispose();
                return true;
            }

            return false;
        }
        catch (UnauthorizedAccessException)
        {
            // An elevated UIAccess instance already holds the session mutex.
            return true;
        }
    }

        // Windows 10 reparents the bars into the taskbar, where only a plain GDI blit shows.
        // Windows 11 does not present that child, so the bars stay a top-level window and Avalonia can use
        // composition, the only mode where the flyout gets its Mica backdrop.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)
                ? new Win32PlatformOptions()
                : new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
            .LogToTrace();
}
