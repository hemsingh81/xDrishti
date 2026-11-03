namespace XDrishti.Domain.Options.Remark;

/// <summary>
/// The part of a stored cash trade the re-mark reads (spec P14-08): its key, side, entry and exit minutes (epoch seconds, the start of the fill minute), the exact entry and stop, the quantity (<see cref="QuantityKnown"/> false for a gross trade, which has none: the
/// risk is then per share), the rupee risk <c>qty x |entry - stop|</c>, the cash exit reason as a word, the cash R on the trade's own cost basis (<see cref="CashNet"/>) and whether the trade was still open at the end of the run.
/// </summary>
public sealed record RemarkTrade(
    string Key,
    bool IsBuy,
    DateOnly TradeDate,
    long EntryEpoch,
    long ExitEpoch,
    string ExitReason,
    decimal EntryPrice,
    decimal StopPrice,
    decimal Quantity,
    bool QuantityKnown,
    decimal RiskRupees,
    double CashR,
    bool CashNet,
    bool OpenAtEnd);
