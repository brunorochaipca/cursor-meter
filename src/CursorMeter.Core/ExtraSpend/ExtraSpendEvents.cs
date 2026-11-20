using System.Globalization;
using System.Text.Json;

namespace CursorMeter;

/// <summary>ExtraSpend. One on-demand charge from the usage list, in cents.</summary>
public readonly record struct ExtraCharge(DateTimeOffset At, int Cents);

/// <summary>ExtraSpend. One page of the usage list. <see cref="Rows"/> counts every row, <see cref="Oldest"/> is the oldest row's time.</summary>
public readonly record struct ExtraPage(IReadOnlyList<ExtraCharge> Charges, int Rows, DateTimeOffset? Oldest);

/// <summary>
/// ExtraSpend. Reads on-demand charges from a page of <c>get-filtered-usage-events</c>,
/// the same list as cursor.com/dashboard/usage. Included rows are not extra money.
/// <c>null</c> means the page was not that list.
/// </summary>
public static class ExtraSpendEvents
{
    /// <summary>
    /// One page: its charges, how many rows it has (extra or not) and the time of its oldest row.
    /// A page is full by the row count, not by the number of charges: most rows are included in the plan.
    /// </summary>
    public static ExtraPage? ReadPage(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var charges = ReadCharges(document.RootElement);
            if (charges is null)
                return null;
            var rows = document.RootElement.GetProperty("usageEventsDisplay");
            DateTimeOffset? oldest = null;
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind == JsonValueKind.Object && TryTimestamp(row, out var at) && (oldest is null || at < oldest))
                    oldest = at;
            }

            return new ExtraPage(charges, rows.GetArrayLength(), oldest);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static IReadOnlyList<ExtraCharge>? ReadCharges(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return ReadCharges(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static IReadOnlyList<ExtraCharge>? ReadCharges(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("usageEventsDisplay", out var rows)
            || rows.ValueKind != JsonValueKind.Array)
            return null;

        var charges = new List<ExtraCharge>();
        foreach (var row in rows.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Object || !TryTimestamp(row, out var at))
                continue;
            int? cents = ReadUsageCents(row);
            if (cents is null && IsOnDemand(row) && TryCents(row, "chargedCents", out var charged) && charged > 0)
                cents = charged;
            if (cents is > 0)
                charges.Add(new ExtraCharge(at, cents.Value));
        }

        return charges;
    }

    private static int? ReadUsageCents(JsonElement row)
    {
        if (!row.TryGetProperty("usageBasedCosts", out var value))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) && double.IsFinite(number) && number > 0)
            return (int)Math.Round(number * 100);
        if (value.ValueKind != JsonValueKind.String)
            return null;
        var text = value.GetString()?.Trim();
        if (string.IsNullOrEmpty(text) || text is "-" or "—")
            return null;
        if (text.StartsWith('$'))
            text = text[1..];
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var dollars) || dollars <= 0)
            return null;
        return (int)Math.Round(dollars * 100);
    }

    private static bool IsOnDemand(JsonElement row)
    {
        if (!row.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String)
            return false;
        var name = kind.GetString() ?? "";
        return name.Contains("ON_DEMAND", StringComparison.OrdinalIgnoreCase)
            || name.Contains("USAGE_BASED", StringComparison.OrdinalIgnoreCase)
            || name.Contains("OVERAGE", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryTimestamp(JsonElement row, out DateTimeOffset at)
    {
        at = default;
        if (!row.TryGetProperty("timestamp", out var value))
            return false;
        long ms;
        switch (value.ValueKind)
        {
            case JsonValueKind.Number when value.TryGetInt64(out ms):
                break;
            case JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ms):
                break;
            default:
                return false;
        }

        if (ms <= 0)
            return false;
        at = DateTimeOffset.FromUnixTimeMilliseconds(ms);
        return true;
    }

    private static bool TryCents(JsonElement row, string name, out int cents)
    {
        cents = 0;
        if (!row.TryGetProperty(name, out var value))
            return false;
        long raw;
        switch (value.ValueKind)
        {
            case JsonValueKind.Number when value.TryGetInt64(out raw):
                break;
            case JsonValueKind.String when long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out raw):
                break;
            default:
                return false;
        }

        if (raw <= 0 || raw > int.MaxValue)
            return false;
        cents = (int)raw;
        return true;
    }
}
