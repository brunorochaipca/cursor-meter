namespace CursorMeter;

public enum QuotaTone
{
    Ok,
    Warn,
    Hot,
    Unknown,
}

public sealed record UsageQuotas(double CursorModelsPercent, double OtherModelsPercent, DateTimeOffset? CycleEnd)
{
    public QuotaTone CursorTone => ToneOf(CursorModelsPercent);
    public QuotaTone OtherTone => ToneOf(OtherModelsPercent);

    public static QuotaTone ToneOf(double percent)
    {
        if (double.IsNaN(percent))
            return QuotaTone.Unknown;
        if (percent >= 90)
            return QuotaTone.Hot;
        if (percent >= 70)
            return QuotaTone.Warn;
        return QuotaTone.Ok;
    }
}

public static class QuotaText
{
    public static string Format(double percent)
    {
        if (double.IsNaN(percent) || double.IsInfinity(percent))
            return "—%";
        if (percent > 0 && percent < 0.5)
            return "<1%";
        return $"{Math.Round(percent, MidpointRounding.AwayFromZero):0}%";
    }

    public static string Line(UsageQuotas quotas)
        => $"{Format(quotas.CursorModelsPercent)} Cursor Models | {Format(quotas.OtherModelsPercent)} Other Models";

    public const string PanelLabelGuide = "Cursor 100% · Outros 100%";

    public static string PanelLabel(UsageQuotas quotas)
        => $"Cursor {Format(quotas.CursorModelsPercent)} · Outros {Format(quotas.OtherModelsPercent)}";

    public static string ResetLabel(DateTimeOffset? cycleEnd, DateTimeOffset now)
    {
        if (cycleEnd is null)
            return "";
        var days = (int)Math.Ceiling((cycleEnd.Value - now).TotalDays);
        if (days <= 0)
            return "reinicia hoje";
        if (days == 1)
            return "reinicia amanhã";
        return $"reinicia em {days} dias";
    }

    public static string Summary(string? plan, string? email, DateTimeOffset? cycleEnd, DateTimeOffset now)
    {
        var identity = new List<string>();
        if (!string.IsNullOrWhiteSpace(plan))
            identity.Add(plan.Trim());
        if (!string.IsNullOrWhiteSpace(email))
            identity.Add(email.Trim());

        var lines = new List<string>();
        if (identity.Count > 0)
            lines.Add(string.Join(" · ", identity));
        var reset = ResetLabel(cycleEnd, now);
        if (reset.Length > 0)
            lines.Add(reset);
        return lines.Count == 0 ? "Cada barra é a quota desse pool." : string.Join("\n", lines);
    }

    public static double BarFill(double percent)
    {
        if (double.IsNaN(percent) || percent <= 0)
            return 0;
        return Math.Min(100, percent) / 100d;
    }
}
