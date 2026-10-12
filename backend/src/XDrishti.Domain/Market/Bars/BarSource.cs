namespace XDrishti.Domain.Market.Bars;

/// <summary>Where a stored 1-minute bar came from. Dhan bars win over CSV bars for the same minute (ADR 0016 §3).</summary>
public enum BarSource
{
    Csv,
    Dhan,
}

/// <summary>The stored and wire spelling of <see cref="BarSource"/>: <c>csv</c>, <c>dhan</c>.</summary>
public static class BarSourceCodes
{
    public const string Csv = "csv";
    public const string Dhan = "dhan";

    public static string ToCode(this BarSource source) => source switch
    {
        BarSource.Csv => Csv,
        BarSource.Dhan => Dhan,
        _ => throw new ArgumentOutOfRangeException(nameof(source)),
    };
}
