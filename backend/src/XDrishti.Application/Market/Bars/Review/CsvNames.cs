namespace XDrishti.Application.Market.Bars.Review;

/// <summary>Names a caller sent, as they may appear in a message back to the caller: cut short and without control characters, so a hostile name stays harmless text.</summary>
internal static class CsvNames
{
    private const int MaxEchoLength = 80;

    public static string Echo(string? name)
    {
        var text = name ?? string.Empty;
        var clean = new string([.. text.Take(MaxEchoLength).Select(c => char.IsControl(c) ? '?' : c)]);
        return text.Length > MaxEchoLength ? clean + "…" : clean;
    }
}
