using System.Text;
using System.Text.Json;

namespace CursorMeter;

/// <summary>
/// What the Linux app hands to the GNOME Shell extension that draws the bars inside the Zorin taskbar:
/// one JSON document, already reduced to text, fill and tone, so the extension has no quota logic of its own.
/// </summary>
public static class PanelFeed
{
    public const int Version = 1;

    public const string CursorLabel = "Cursor Models";
    public const string OtherLabel = "Other Models";

    /// <param name="status">What to show instead of the bars while there are no quotas ("A ler…", "Abre o Cursor").</param>
    /// <param name="detail">The tooltip text.</param>
    /// <param name="pid">The app's process id, so the extension can tell a stale file from a live app.</param>
    public static string Build(string? status, string detail, UsageQuotas? quotas, AppSettings settings, int pid, DateTimeOffset now)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            json.WriteNumber("version", Version);
            json.WriteNumber("pid", pid);
            json.WriteNumber("updated", now.ToUnixTimeSeconds());
            if (status is null)
                json.WriteNull("status");
            else
                json.WriteString("status", status);
            json.WriteString("detail", detail);

            json.WriteStartArray("pools");
            if (quotas is not null)
            {
                if (settings.Pools != PoolView.OtherOnly)
                    WritePool(json, "cursor", CursorLabel, quotas.CursorModelsPercent, settings);
                if (settings.Pools != PoolView.CursorOnly)
                    WritePool(json, "other", OtherLabel, quotas.OtherModelsPercent, settings);
            }

            json.WriteEndArray();

            // Null means the extension's own colour, which is the app's default palette.
            json.WriteStartObject("colors");
            WriteColor(json, "text", settings.TextColor);
            WriteColor(json, "muted", settings.MutedColor);
            WriteColor(json, "cursor", settings.CursorColor);
            WriteColor(json, "other", settings.OtherColor);
            WriteColor(json, "warn", settings.WarnColor);
            WriteColor(json, "hot", settings.HotColor);
            json.WriteEndObject();

            json.WriteNumber("textScale", settings.TextScale);
            json.WriteNumber("barSpacing", settings.BarSpacing);
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void WritePool(Utf8JsonWriter json, string id, string label, double used, AppSettings settings)
    {
        var shown = settings.ShowRemaining && !double.IsNaN(used) ? Math.Max(0, 100 - used) : used;
        json.WriteStartObject();
        json.WriteString("id", id);
        json.WriteString("label", label);
        json.WriteString("text", QuotaText.Format(shown));
        json.WriteNumber("fill", QuotaText.BarFill(shown));
        json.WriteString("tone", ToneName(UsageQuotas.ToneOf(used, settings.WarnAt, settings.HotAt)));
        json.WriteEndObject();
    }

    private static void WriteColor(Utf8JsonWriter json, string name, string? hex)
    {
        if (AppSettings.NormalizeColor(hex) is { } color)
            json.WriteString(name, color);
        else
            json.WriteNull(name);
    }

    private static string ToneName(QuotaTone tone) => tone switch
    {
        QuotaTone.Hot => "hot",
        QuotaTone.Warn => "warn",
        QuotaTone.Ok => "ok",
        _ => "unknown",
    };
}
