using System.Globalization;
using System.Text.Json;

namespace CursorMeter;

/// <summary>
/// ExtraSpend. Reads the extra-plan total, in cents, from a usage response.
/// <c>null</c> means the response did not say: keep the previous reading.
/// An object with no amount is zero, not a missing reading.
/// </summary>
public static class ExtraSpendParser
{
    public static int? ReadCents(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return ReadCents(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static int? ReadCents(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("spendLimitUsage", out var spend)
            || spend.ValueKind != JsonValueKind.Object)
            return null;

        if (TryCents(spend, "individualUsed", out var individual))
            return individual;
        if (TryCents(spend, "pooledUsed", out var pooled))
            return pooled;
        return 0;
    }

    private static bool TryCents(JsonElement obj, string name, out int cents)
    {
        cents = 0;
        if (!obj.TryGetProperty(name, out var value))
            return false;

        long raw;
        switch (value.ValueKind)
        {
            case JsonValueKind.Number when value.TryGetInt64(out raw):
                break;
            case JsonValueKind.Number when value.TryGetDouble(out var number) && double.IsFinite(number):
                raw = (long)Math.Round(number);
                break;
            case JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out raw):
                break;
            default:
                return false;
        }

        if (raw < 0 || raw > int.MaxValue)
            return false;
        cents = (int)raw;
        return true;
    }
}
