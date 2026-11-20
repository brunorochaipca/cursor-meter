using System.Globalization;

namespace CursorMeter;

/// <summary>
/// ExtraSpend. The one remembered fact: the extra total at the start of the local day, and the day it belongs to.
/// </summary>
public static class ExtraSpendDay
{
    public static string Format(DateOnly date, int cents)
        => string.Create(CultureInfo.InvariantCulture, $"{date:yyyy-MM-dd} {cents}");

    public static (DateOnly Date, int Cents)? TryRead(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var parts = text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
            return null;
        if (!DateOnly.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return null;
        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var cents) || cents < 0)
            return null;
        return (date, cents);
    }
}
