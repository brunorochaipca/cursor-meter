namespace CursorMeter.App;

internal static class StartupShortcut
{
    public static string LinkPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "CursorMeter.lnk");

    public static bool IsEnabled => File.Exists(LinkPath);

    public static void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            if (File.Exists(LinkPath))
                File.Delete(LinkPath);
            return;
        }

        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell não está disponível.");
        object shellObject = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("WScript.Shell não está disponível.");
        dynamic shell = shellObject;
        dynamic shortcut = shell.CreateShortcut(LinkPath);
        shortcut.TargetPath = Application.ExecutablePath;
        shortcut.WorkingDirectory = AppContext.BaseDirectory;
        shortcut.Description = "Cursor Models | Other Models";
        shortcut.Save();
    }
}
