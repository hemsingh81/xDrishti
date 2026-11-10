namespace XDrishti.Domain.Market.Baskets.Lists;

/// <summary>One seeded list basket (BK-05): a fixed id (the migration computes the same <c>md5('market.basket:list:' + key)::uuid</c>), its name, the text of its description and the format of its source.</summary>
public sealed record ListBasketDefinition(string Key, Guid Id, string Name, string Description, string Format);

/// <summary>The list baskets of BK-05 (ADR 0256) and the source formats of <c>basket_sources.format_version</c>. MIS-eligible is not seeded: the stored master has no intraday-leverage column.</summary>
public static class ListBaskets
{
    public const string IndexFormat = "nse-index-csv-1";
    public const string MasterFlagsFormat = "dhan-master-flags-1";
    public const string BanFormat = "nse-fo-secban-1";
    public const string LotsFormat = "nse-fo-mktlots-1";

    public const string BanDescription = "Excluded from new positions: ban period. Securities in the F&O ban for the trade date named in NSE's file; an exclusion filter, not a tradable universe.";

    public static readonly ListBasketDefinition FnoStocks = new(
        "fno-stocks", Guid.Parse("a8a02f03-bb08-7da6-1c07-11865502f36e"), "List: F&O stocks",
        "Shares with stock futures in the Dhan instrument master; membership dated from the first recorded master refresh.", MasterFlagsFormat);

    public static readonly ListBasketDefinition AsmGsm = new(
        "asm-gsm", Guid.Parse("6c22641d-0e4e-db44-c5e4-e453b20c65c7"), "List: ASM/GSM surveillance",
        "Shares the Dhan master flags for ASM or GSM surveillance (the category is the member note); dated from the first recorded master refresh.", MasterFlagsFormat);

    public static readonly ListBasketDefinition Mtf = new(
        "mtf-eligible", Guid.Parse("f408d1df-2fd5-dd9b-3f83-3998316e5c8e"), "List: MTF-eligible",
        "Shares the broker reports as MTF-eligible (broker-reported, may change); no leverage figure is shown or assumed.", MasterFlagsFormat);

    public static readonly ListBasketDefinition TradeForTrade = new(
        "series-t2t", Guid.Parse("b39fc7c1-f128-5485-3b44-e273eb7b3f28"), "List: SME and trade-for-trade series",
        "Shares in the series BE, BZ, SM or ST in the Dhan instrument master; dated from the first recorded master refresh.", MasterFlagsFormat);

    public static readonly ListBasketDefinition Ban = new(
        "fo-ban", Guid.Parse("65b8f00e-247b-fca9-a30b-fbf05133590b"), "List: In F&O ban", BanDescription, BanFormat);

    /// <summary>The source of the F&amp;O lot sizes: not a list of members but the catalogue row and the fetch log of <c>fo_mktlots.csv</c>. Its basket row is archived, so no screen lists it.</summary>
    public static readonly ListBasketDefinition LotSizes = new(
        "fo-lot-sizes", Guid.Parse("a8888d9f-d8ea-8afc-0208-7a5113895e6c"), "List: F&O lot sizes (source)",
        "The source row of NSE's F&O lot-size file; holds no members.", LotsFormat);

    /// <summary>The five baskets that carry members.</summary>
    public static IReadOnlyList<ListBasketDefinition> Baskets { get; } = [FnoStocks, AsmGsm, Mtf, TradeForTrade, Ban];

    /// <summary>The four baskets derived from the master (no network).</summary>
    public static IReadOnlyList<ListBasketDefinition> FromMaster { get; } = [FnoStocks, AsmGsm, Mtf, TradeForTrade];

    /// <summary>Whether the format is a file fetched from NSE (needs <c>Baskets:System:Lists:FetchEnabled</c>).</summary>
    public static bool IsListFile(string format) => format is BanFormat or LotsFormat;
}
