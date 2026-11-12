using System.Runtime.Versioning;
using System.Text;

namespace CursorMeter.App.Linux;

[SupportedOSPlatform("linux")]
internal static class AutostartEntry
{
    public static string EntryPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData, Environment.SpecialFolderOption.DoNotVerify),
        "autostart",
        "cursormeter.desktop");

    public static bool IsEnabled => File.Exists(EntryPath);

    public static MenuEntry MenuEntry() => new("Iniciar com a sessão", Toggle, () => IsEnabled);

    public static void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            if (File.Exists(EntryPath))
                File.Delete(EntryPath);
            return;
        }

        var exe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Não sei onde está o executável.");
        Directory.CreateDirectory(Path.GetDirectoryName(EntryPath)!);
        File.WriteAllText(EntryPath, $"""
            [Desktop Entry]
            Type=Application
            Name=CursorMeter
            Comment=Cursor Models | Other Models
            Exec={QuoteExec(exe)}
            Icon=cursormeter
            Terminal=false
            X-GNOME-Autostart-enabled=true

            """);
    }

    // Exec holds a quoted argument inside an escaped string value, so every quoting backslash is escaped again; % starts a field code.
    private static string QuoteExec(string path)
    {
        var quoted = new StringBuilder("\"");
        foreach (var c in path)
        {
            switch (c)
            {
                case '"' or '`' or '$':
                    quoted.Append(@"\\").Append(c);
                    break;
                case '\\':
                    quoted.Append(@"\\\\");
                    break;
                case '%':
                    quoted.Append("%%");
                    break;
                default:
                    quoted.Append(c);
                    break;
            }
        }

        return quoted.Append('"').ToString();
    }

    private static void Toggle()
    {
        try
        {
            SetEnabled(!IsEnabled);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
        }
    }
}
