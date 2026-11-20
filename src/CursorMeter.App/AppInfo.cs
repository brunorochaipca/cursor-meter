using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CursorMeter.App;

/// <summary>What the About window and the diagnostic report say about the app.</summary>
internal static class AppInfo
{
    public const string Repository = "https://github.com/brunorochaipca/cursor-meter";

    public static string Version
    {
        get
        {
            var version = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "?";
            var plus = version.IndexOf('+');
            return plus >= 0 ? version[..plus] : version;
        }
    }

    /// <summary>The edition this build belongs to: Island (stable, Start11 v3) or Mainland (beta, Start11 v1/v2).</summary>
#if MAINLAND
    public static string Edition => "Mainland";
#else
    public static string Edition => "Island";
#endif

    /// <summary>True for the Mainland edition, the only one that carries the experimental administrator mode.</summary>
    public static bool IsMainland => Edition == "Mainland";

    /// <summary>Product name, for the About window and reports.</summary>
    public static string Product => "CursorMeter";

    public static string Platform => $"{RuntimeInformation.OSDescription}, {RuntimeInformation.FrameworkDescription}";

    /// <summary>Opens a web address or a folder with the system's default program. False when there is none.</summary>
    public static bool TryOpen(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true })?.Dispose();
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or PlatformNotSupportedException)
        {
            // On Linux, UseShellExecute throws when xdg-open, gnome-open and kfmclient are all missing.
            AppLog.Error($"Could not open {target}", ex);
            return false;
        }
    }
}
