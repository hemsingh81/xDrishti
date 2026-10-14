namespace XDrishti.Application.Portfolio.DividendImport;

/// <summary>What importing a row of a dividend file would do with it (ADR 0028). The names are those of the API (<see cref="DividendStatuses"/>).</summary>
internal enum DividendStatus
{
    /// <summary>It would be stored (its scrip name was matched to a holding, an instrument, or the owner's remembered "nothing").</summary>
    New,

    /// <summary>The account stores this payment already (same identity, not voided): not stored again.</summary>
    Imported,

    /// <summary>Equal to a dividend the owner typed in (same holding, date and amount): not stored unless the owner ticks it.</summary>
    PossibleDuplicate,

    /// <summary>The scrip name matched nothing: it would be stored unallocated, with the file's name.</summary>
    NeedsMatch,

    /// <summary>More than one holding fits the scrip name and none is guessed: it would be stored unallocated, with the file's name, unless the owner chooses.</summary>
    Ambiguous,
}

/// <summary>The names of the statuses and warnings in the API.</summary>
internal static class DividendStatuses
{
    public const string AmountMismatch = "amount_mismatch";

    public const string BeforeFirstLot = "before_first_lot";

    public const string PartialNameMatch = "partial_name_match";

    public static string Name(DividendStatus status) => status switch
    {
        DividendStatus.New => "new",
        DividendStatus.Imported => "imported",
        DividendStatus.PossibleDuplicate => "possible_duplicate",
        DividendStatus.NeedsMatch => "needs_match",
        _ => "ambiguous",
    };

    public static string Name(DividendMatchKind kind) => kind switch
    {
        DividendMatchKind.Alias => "alias",
        DividendMatchKind.Isin => "isin",
        DividendMatchKind.Symbol => "symbol",
        DividendMatchKind.Name => "name",
        DividendMatchKind.Contains => "contains",
        DividendMatchKind.Master => "master",
        DividendMatchKind.Choice => "choice",
        _ => "none",
    };

    /// <summary>True for the statuses an import stores by default (the payment is not stored and not a possible duplicate).</summary>
    public static bool IsToAdd(DividendStatus status) => status is DividendStatus.New or DividendStatus.NeedsMatch or DividendStatus.Ambiguous;
}
