using System.Text.Json;

namespace CursorMeter;

/// <summary>
/// Where the GNOME Shell extension's bars are on the screen, in pixels. The extension keeps it in the
/// file it creates while it is enabled, so the summary can open centred above the bars.
/// </summary>
public readonly record struct PanelAnchor(int X, int Y, int Width, int Height)
{
    /// <summary>False for an empty file, for anything that is not a rectangle with a size, and for garbage.</summary>
    public static bool TryParse(string? json, out PanelAnchor anchor)
    {
        anchor = default;
        if (string.IsNullOrWhiteSpace(json))
            return false;

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryNumber(root, "x", out int x)
                || !TryNumber(root, "y", out int y)
                || !TryNumber(root, "width", out int width)
                || !TryNumber(root, "height", out int height)
                || width <= 0
                || height <= 0)
            {
                return false;
            }

            anchor = new PanelAnchor(x, y, width, height);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryNumber(JsonElement root, string name, out int value)
    {
        value = 0;
        if (!root.TryGetProperty(name, out var element)
            || element.ValueKind != JsonValueKind.Number
            || !element.TryGetDouble(out var number)
            || double.IsNaN(number)
            || Math.Abs(number) > int.MaxValue)
        {
            return false;
        }

        value = (int)Math.Round(number, MidpointRounding.AwayFromZero);
        return true;
    }
}
