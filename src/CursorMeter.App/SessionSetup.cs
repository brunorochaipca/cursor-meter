using System.ComponentModel;
using System.Diagnostics;

namespace CursorMeter.App;

/// <summary>
/// Where the app could read the Cursor account from, and what is missing. Shown by the flyout
/// when there is no usable session, so the user knows what to do instead of guessing.
/// </summary>
/// <param name="CursorInstalled">The Cursor desktop app, or its data folder, exists on this computer.</param>
/// <param name="CursorHasSession">Cursor holds a session, but it was rejected.</param>
/// <param name="CursorPath">The Cursor program, when it can be started from here.</param>
/// <param name="FirefoxPath">The Firefox program, when it can be started from here.</param>
/// <param name="Firefox">What the Firefox profiles hold.</param>
internal sealed record SessionSetup(
    bool CursorInstalled,
    bool CursorHasSession,
    string? CursorPath,
    string? FirefoxPath,
    FirefoxSession.FirefoxStatus Firefox)
{
    public const string DownloadUrl = "https://cursor.com/download";
    public const string LoginUrl = "https://cursor.com/dashboard";

    public static SessionSetup Inspect(CursorSession? session)
    {
        var cursor = FindCursor();
        bool installed = cursor is not null || Directory.Exists(Path.Combine(Folder(Environment.SpecialFolder.ApplicationData), "Cursor"));
        return new SessionSetup(installed, session is not null, cursor, FindFirefox(), FirefoxSession.Inspect());
    }

    /// <summary>Starts Cursor. False when it cannot be started.</summary>
    public bool OpenCursor() => CursorPath is { } path && Start(path, null);

    /// <summary>Opens the Cursor login page in Firefox, whichever browser is the default.</summary>
    public bool OpenFirefox() => FirefoxPath is { } path && Start(path, LoginUrl);

    public static bool OpenDownload()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = DownloadUrl, UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static bool Start(string program, string? argument)
    {
        try
        {
            var info = new ProcessStartInfo { FileName = program, UseShellExecute = false };
            if (argument is not null)
                info.ArgumentList.Add(argument);
            Process.Start(info);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static string? FindCursor()
    {
        if (OperatingSystem.IsWindows())
        {
            return First(
                Path.Combine(Folder(Environment.SpecialFolder.LocalApplicationData), "Programs", "cursor", "Cursor.exe"),
                Path.Combine(Folder(Environment.SpecialFolder.ProgramFiles), "cursor", "Cursor.exe"));
        }

        return OnPath("cursor");
    }

    internal static string? FindFirefox()
    {
        if (OperatingSystem.IsWindows())
        {
            return First(
                       Path.Combine(Folder(Environment.SpecialFolder.ProgramFiles), "Mozilla Firefox", "firefox.exe"),
                       Path.Combine(Folder(Environment.SpecialFolder.ProgramFilesX86), "Mozilla Firefox", "firefox.exe"),
                       Path.Combine(Folder(Environment.SpecialFolder.LocalApplicationData), "Mozilla Firefox", "firefox.exe"))
                   ?? FindFirefoxInRegistry()
                   ?? OnPath("firefox.exe");
        }

        return OnPath("firefox");
    }

    /// <summary>
    /// The Firefox installer registers the program under <c>App Paths</c>, wherever it was installed
    /// (another drive, a per-user install), and keeps the install folder under <c>Mozilla\Mozilla Firefox</c>.
    /// </summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static string? FindFirefoxInRegistry()
    {
        try
        {
            foreach (var hive in new[] { Microsoft.Win32.RegistryHive.LocalMachine, Microsoft.Win32.RegistryHive.CurrentUser })
            foreach (var view in new[] { Microsoft.Win32.RegistryView.Registry64, Microsoft.Win32.RegistryView.Registry32 })
            {
                using var root = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, view);

                using (var appPath = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\firefox.exe"))
                {
                    if (ExistingProgram(appPath?.GetValue(null) as string) is { } found)
                        return found;
                }

                using var mozilla = root.OpenSubKey(@"SOFTWARE\Mozilla\Mozilla Firefox");
                if (mozilla is null)
                    continue;
                if (mozilla.GetValue("CurrentVersion") is string version
                    && mozilla.OpenSubKey(version + @"\Main") is { } main)
                {
                    using (main)
                    {
                        if (ExistingProgram(main.GetValue("PathToExe") as string) is { } found)
                            return found;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
        }

        return null;
    }

    private static string? ExistingProgram(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;
        path = path.Trim().Trim('"');
        return File.Exists(path) ? path : null;
    }

    private static string Folder(Environment.SpecialFolder folder)
        => Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);

    private static string? First(params string[] candidates)
        => candidates.FirstOrDefault(path => path.Length > 0 && File.Exists(path));

    private static string? OnPath(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
            }
        }

        return null;
    }
}
