using System.Globalization;
using System.Text.Json;

namespace CursorMeter;

public static class UsageParser
{
    public static UsageQuotas Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return Parse(document.RootElement);
    }

    public static UsageQuotas Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new CursorUsageException("A resposta de utilização não é um objeto.");

        if (!TryGetObject(root, "planUsage", out var plan))
            throw new CursorUsageException("A resposta não inclui planUsage.");

        var cursor = ReadPercent(plan, "autoPercentUsed")
            ?? throw new CursorUsageException("Falta a percentagem de Cursor Models.");
        var other = ReadPercent(plan, "apiPercentUsed")
            ?? throw new CursorUsageException("Falta a percentagem de Other Models.");

        return new UsageQuotas(cursor, other, ReadCycleEnd(root));
    }

    private static DateTimeOffset? ReadCycleEnd(JsonElement root)
    {
        if (!root.TryGetProperty("billingCycleEnd", out var end))
            return null;
        return end.ValueKind switch
        {
            JsonValueKind.Number when end.TryGetInt64(out var ms) => FromUnix(ms),
            JsonValueKind.String => ParseEndString(end.GetString()),
            _ => null,
        };
    }

    private static DateTimeOffset? ParseEndString(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms))
            return FromUnix(ms);
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return parsed;
        return null;
    }

    private static DateTimeOffset FromUnix(long value)
    {
        var ms = value < 1_000_000_000_000 ? value * 1000 : value;
        return DateTimeOffset.FromUnixTimeMilliseconds(ms);
    }

    private static double? ReadPercent(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
            return null;
        double number = value.ValueKind switch
        {
            JsonValueKind.Number => value.GetDouble(),
            JsonValueKind.String when double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => double.NaN,
        };
        if (double.IsNaN(number) || double.IsInfinity(number) || number < 0)
            return null;
        return number;
    }

    private static bool TryGetObject(JsonElement element, string name, out JsonElement obj)
    {
        if (element.TryGetProperty(name, out obj) && obj.ValueKind == JsonValueKind.Object)
            return true;
        obj = default;
        return false;
    }
}
