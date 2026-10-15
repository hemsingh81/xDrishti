using XDrishti.Application.Abstractions.Portfolio;

namespace XDrishti.Application.Portfolio.Attention;

/// <summary>
/// The plain words for the codes of <see cref="BrokerDropReasons"/> in the text of a <c>rows_dropped</c> item (ADR 0030): a fixed table, never anything taken from a row, so a text can hold counts and these words only. A code this version does not know reads "other".
/// The frontend has its own table for the sync line (<c>dropReasons.ts</c>); this one is for the bell and stands alone.
/// </summary>
internal static class DropReasonWords
{
    /// <summary>The only reason that does not make the owner look: a ledger line where no money moved (an opening balance).</summary>
    public const string Harmless = BrokerDropReasons.ZeroAmount;

    private static readonly Dictionary<string, string> Words = new(StringComparer.Ordinal)
    {
        [BrokerDropReasons.UnreadableRow] = "unreadable row",
        [BrokerDropReasons.MissingId] = "no trade id",
        [BrokerDropReasons.BadId] = "trade id too long",
        [BrokerDropReasons.MissingSide] = "side is not buy or sell",
        [BrokerDropReasons.BadQuantity] = "quantity missing or not a whole positive number",
        [BrokerDropReasons.BadPrice] = "price unreadable",
        [BrokerDropReasons.BadTime] = "time unreadable",
        [BrokerDropReasons.BadDate] = "date unreadable",
        [BrokerDropReasons.MissingAmount] = "no amount",
        [BrokerDropReasons.BadAmount] = "amount unreadable",
        [BrokerDropReasons.ZeroAmount] = "zero amount (no money moved, e.g. an opening balance)",
        [BrokerDropReasons.FailedChecks] = "broke a storage rule",
        [BrokerDropReasons.StoreRefused] = "the database refused it",
        [BrokerDropReasons.Other] = "other",
    };

    /// <summary>The words of a reason code; "other" for a code that is not in the table.</summary>
    public static string Of(string code) => Words.TryGetValue(code, out var words) ? words : Words[BrokerDropReasons.Other];
}
