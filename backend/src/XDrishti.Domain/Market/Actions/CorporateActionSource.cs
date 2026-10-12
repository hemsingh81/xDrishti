namespace XDrishti.Domain.Market.Actions;

/// <summary>Where a corporate action came from: typed by the owner, found in the daily bars, or imported from a CSV text.</summary>
public enum CorporateActionSource
{
    Manual,
    Detected,
    Csv,
}

/// <summary>The codes of <see cref="CorporateActionSource"/> used in the database and the API: <c>manual</c> · <c>detected</c> · <c>csv</c>.</summary>
public static class CorporateActionSourceCodes
{
    public const string Manual = "manual";
    public const string Detected = "detected";
    public const string Csv = "csv";

    public static string ToCode(this CorporateActionSource source) => source switch
    {
        CorporateActionSource.Manual => Manual,
        CorporateActionSource.Detected => Detected,
        CorporateActionSource.Csv => Csv,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Unknown corporate action source."),
    };
}
