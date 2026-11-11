namespace CursorMeter.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\CursorMeter", out var created);
        if (!created)
            return;

        ApplicationConfiguration.Initialize();
        Application.Run(new MeterContext());
        mutex.ReleaseMutex();
    }
}
