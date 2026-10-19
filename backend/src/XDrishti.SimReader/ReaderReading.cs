namespace XDrishti.SimReader;

/// <summary>What the chart shows for one trade, with exact prices (no slippage, no costs). Epochs are bar starts.</summary>
public sealed record ReaderReading(
    decimal OpeningRangeHigh,
    decimal OpeningRangeLow,
    long TriggerBarStartEpoch,
    decimal TriggerClose,
    long EntryEpoch,
    decimal EntryPrice,
    bool EntryAtLimit,
    decimal Stop,
    decimal Target,
    long ExitEpoch,
    decimal ExitPrice,
    ReaderExitReason ExitReason,
    decimal SeenR);
