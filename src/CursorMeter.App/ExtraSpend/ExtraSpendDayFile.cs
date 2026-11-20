namespace CursorMeter.App.ExtraSpend;

/// <summary>ExtraSpend. The day mark, one line in <c>extra-day.txt</c> beside the settings. Amounts never go to the log.</summary>
internal static class ExtraSpendDayFile
{
    public static string Path => System.IO.Path.Combine(AppLog.Folder, "extra-day.txt");

    public static (DateOnly Date, int Cents)? Load()
    {
        try
        {
            return File.Exists(Path) ? ExtraSpendDay.TryRead(File.ReadAllText(Path)) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Save(DateOnly date, int cents)
    {
        try
        {
            Directory.CreateDirectory(AppLog.Folder);
            File.WriteAllText(Path, ExtraSpendDay.Format(date, cents));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
