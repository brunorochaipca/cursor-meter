using System.Runtime.Versioning;
using Avalonia.Media;
using Microsoft.Win32;

namespace CursorMeter.App.Windows;

/// <summary>How the bars look as an island of their own.</summary>
/// <param name="OutlineWidth">Thickness of the outline in device-independent pixels.</param>
/// <param name="Radius">Radius of the rounded corners in screen pixels.</param>
internal sealed record IslandLook(Color Fill, Color? Outline, double OutlineWidth, double Radius)
{
    /// <summary>Pixels of window around the island for the outer half of the outline.</summary>
    public int Margin { get; init; }

    /// <summary>The same look with room for the outline at <paramref name="dpi"/>.</summary>
    public IslandLook At(int dpi)
    {
        if (Outline is null)
            return this with { Margin = 0 };
        double half = OutlineWidth * dpi / 96 / 2;
        return this with { Margin = Math.Max(1, (int)Math.Ceiling(half - 0.5)) };
    }
}

/// <summary>Reads the taskbar colours set in Start11, so the bars pass for one more of its islands.</summary>
internal static class Start11Theme
{
    private const string SettingsKey = @"Software\Stardock\Start8\Start8.ini\Start8";

    /// <summary>
    /// True when the taskbar style chosen in Start11 is islands. The rounded and solid styles also
    /// leave a window region on the taskbar, but one that is not made of islands.
    /// </summary>
    public static bool IslandsStyle()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);
        return Flag(key, "TaskbarIslands");
    }

    public static IslandLook Look(double radius)
    {
        var fill = Palette.Background.Color;
        Color? outline = null;
        double width = 1;
        if (OperatingSystem.IsWindows())
        {
            using var key = Registry.CurrentUser.OpenSubKey(SettingsKey);
            if (Flag(key, "CustomColourTaskbar") && ColorOf(key, "CustomColourTaskbarV") is { } custom)
                fill = custom;
            if (Flag(key, "TaskbarBorder"))
                outline = ColorOf(key, "TaskbarBorderColour");
            // Hundredths of a pixel at 100%: 100, 125, 150 or 200. Start11 scales it with the monitor.
            if (Number(key, "EdgeLineWidth") is { } hundredths)
                width = Math.Clamp(hundredths, 50, 400) / 100d;
        }

        return new IslandLook(fill, outline, width, radius);
    }

    [SupportedOSPlatform("windows")]
    private static bool Flag(RegistryKey? key, string name) => Number(key, name) == 1;

    /// <summary>Start11 stores colours as a COLORREF, 0x00BBGGRR.</summary>
    [SupportedOSPlatform("windows")]
    private static Color? ColorOf(RegistryKey? key, string name) =>
        Number(key, name) is { } value ? Color.FromRgb((byte)value, (byte)(value >> 8), (byte)(value >> 16)) : null;

    [SupportedOSPlatform("windows")]
    private static int? Number(RegistryKey? key, string name) =>
        int.TryParse(key?.GetValue(name)?.ToString(), out var value) ? value : null;
}
