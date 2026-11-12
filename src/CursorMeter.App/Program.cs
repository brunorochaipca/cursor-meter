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
        using var mutex = new Mutex(true, name, out var created);
        if (!created)
            return 0;

        var code = BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        mutex.ReleaseMutex();
        return code;
    }

    // The widget is reparented into Explorer's taskbar; only plain GDI blits paint reliably inside another process's window.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions { RenderingMode = [Win32RenderingMode.Software] })
            .LogToTrace();
}
