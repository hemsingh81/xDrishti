using System.Reflection;
using XDrishti.Domain.Research;

namespace XDrishti.Domain.Tests.Research;

/// <summary>P3-04d: the card guard names the first differing field of <see cref="SimTrade"/> in declaration order, so the order of the fields is part of what it says; a change of it must be a decision.</summary>
public sealed class SimTradeDifferenceTests
{
    [Fact]
    public void The_fields_of_a_trade_are_in_the_order_the_guard_reports_them()
    {
        var names = typeof(SimTrade).GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(p => p.Name).Where(n => n is not ("Key" or "EqualityContract")).ToList();

        names.ShouldBe(
        [
            "SchemaVersion", "StrategyId", "InstrumentId", "Symbol", "Side", "TradeDate", "DecisionEpochSeconds", "TriggerTimeframe", "TriggerBar", "TriggerBarMinutes", "TriggerBarComplete", "EntryEpochSeconds", "EntryPrice", "StopPrice", "TargetPrice",
            "ExitEpochSeconds", "ExitPrice", "ExitReason", "RExact", "MaeR", "MfeR", "Fills", "Costs", "EntrySnapshotJson", "RFilled", "SlippageR", "EntryFilledPrice", "ExitFilledPrice", "EntryFill", "TickFallback", "SessionsHeld", "SessionsUnseen", "Costing", "Legs",
        ]);
    }
}
