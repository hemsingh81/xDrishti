using XDrishti.Domain.Portfolio;

namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>
/// Why a row of Dhan's trade history or ledger was dropped (roadmap ISS-03): a closed set of plain codes, counted per reason in the run (<see cref="BrokerSyncCounts.DroppedByReason"/>). A dropped row is one that could not be used at all; a row that is
/// not wanted (another segment, intraday) is <i>skipped</i> instead (<see cref="BrokerSkipReasons"/>). Where a row has several problems the first one in the order of the lists below is the reason. A code never carries content of the row.
/// </summary>
public static class BrokerDropReasons
{
    /// <summary>Trades and ledger: the row is not a JSON object (a number, text, <c>null</c>...).</summary>
    public const string UnreadableRow = "unreadable_row";

    /// <summary>Trades: the row has no trade id (<c>exchangeTradeId</c> / <c>tradeId</c>), so it cannot be told from another trade.</summary>
    public const string MissingId = "missing_id";

    /// <summary>Trades: the trade id is longer than the store keeps (64 characters).</summary>
    public const string BadId = "bad_id";

    /// <summary>Trades: the side (<c>transactionType</c>) is missing or neither buy nor sell.</summary>
    public const string MissingSide = "missing_side";

    /// <summary>Trades: the quantity is missing, not a number, zero or negative, not a whole number or absurdly large.</summary>
    public const string BadQuantity = "bad_quantity";

    /// <summary>Trades: the price is missing, not a number, zero or negative, or absurdly large.</summary>
    public const string BadPrice = "bad_price";

    /// <summary>Trades: none of the exchange, create and update times is present and readable as a date.</summary>
    public const string BadTime = "bad_time";

    /// <summary>Ledger: the voucher date is missing or not a date that can be read.</summary>
    public const string BadDate = "bad_date";

    /// <summary>Ledger: the row has neither a debit nor a credit.</summary>
    public const string MissingAmount = "missing_amount";

    /// <summary>Ledger: the debit or credit is present but is not a number, or is absurdly large.</summary>
    public const string BadAmount = "bad_amount";

    /// <summary>Ledger: debit and credit cancel out (typically an opening-balance line): the row moves no money.</summary>
    public const string ZeroAmount = "zero_amount";

    /// <summary>Trades, after the mapping: the trade was read but breaks a rule of the store (a date before 2000 or in the future, a quantity, price or charges the store does not accept).</summary>
    public const string FailedChecks = "failed_checks";

    /// <summary>Trades and ledger, at the save: the database refused the row (even when saved on its own) and nobody else had stored it.</summary>
    public const string StoreRefused = "store_refused";

    /// <summary>A drop no branch named (also a count that was kept without a reason).</summary>
    public const string Other = BrokerDrops.Other;

    /// <summary>Every code, in a stable order.</summary>
    public static IReadOnlyList<string> All { get; } =
    [
        UnreadableRow, MissingId, BadId, MissingSide, BadQuantity, BadPrice, BadTime, BadDate, MissingAmount, BadAmount, ZeroAmount, FailedChecks, StoreRefused, Other,
    ];
}
