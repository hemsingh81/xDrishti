namespace XDrishti.Domain.Options;

/// <summary>
/// A lot-size row known before the first refresh, keyed by the underlying's canonical symbol (spec P14-01 criterion 6, ADR 0175). <see cref="All"/> holds only
/// values from the secondary sources listed in P14-00; none was read in a circular by the build, so every seed is <c>secondary</c> and unconfirmed. The import
/// writes a seed once the underlying is in the master list and the same row is not there yet.
/// </summary>
public sealed record LotSizeSeed(string UnderlyingSymbol, DateOnly EffectiveFrom, int LotSize, string Source, string SourceUrl, bool Confirmed)
{
    private const string Sebi20241001 = "https://www.business-standard.com/amp/markets/news/sebi-announces-six-key-changes-to-curb-speculation-in-derivatives-trading-124100101316_1.html";

    private const string Lots20251230 = "https://www.kotakneo.com/bulletins/lot-size-changes-in-index-derivatives-effective-december-30-2025/";

    public static IReadOnlyList<LotSizeSeed> All { get; } =
    [
        new("NSE:NIFTY", new DateOnly(2024, 11, 20), 75, LotSizeEntry.Secondary, Sebi20241001, false),
        new("NSE:NIFTY", new DateOnly(2025, 12, 30), 65, LotSizeEntry.Secondary, Lots20251230, false),
        new("NSE:BANKNIFTY", new DateOnly(2025, 12, 30), 30, LotSizeEntry.Secondary, Lots20251230, false),
    ];
}
