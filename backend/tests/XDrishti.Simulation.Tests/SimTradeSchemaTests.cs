using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// AC1: the trade record is frozen. Changing any of the 24 fields, their order or their types, or a member of the enums, fails here until the schema version is raised and ADR 0037 is amended.
/// </summary>
public sealed class SimTradeSchemaTests
{
    private static readonly (string Name, Type Type)[] Fields =
    [
        ("SchemaVersion", typeof(int)),
        ("StrategyId", typeof(string)),
        ("InstrumentId", typeof(Guid)),
        ("Symbol", typeof(string)),
        ("Side", typeof(Side)),
        ("TradeDate", typeof(DateOnly)),
        ("DecisionEpochSeconds", typeof(long)),
        ("TriggerTimeframe", typeof(Timeframe)),
        ("TriggerBar", typeof(Bar)),
        ("TriggerBarMinutes", typeof(int)),
        ("TriggerBarComplete", typeof(bool)),
        ("EntryEpochSeconds", typeof(long)),
        ("EntryPrice", typeof(double)),
        ("StopPrice", typeof(double)),
        ("TargetPrice", typeof(double?)),
        ("ExitEpochSeconds", typeof(long)),
        ("ExitPrice", typeof(double)),
        ("ExitReason", typeof(ExitReason)),
        ("R", typeof(double)),
        ("MaeR", typeof(double)),
        ("MfeR", typeof(double)),
        ("Fills", typeof(FillBasis)),
        ("Costs", typeof(CostBasis)),
        ("EntrySnapshotJson", typeof(string)),
    ];

    [Fact]
    public void The_positional_properties_are_exactly_the_24_fields_of_the_contract_in_order()
    {
        var constructor = typeof(SimTrade).GetConstructors().Single();

        var parameters = constructor.GetParameters().Select(p => (p.Name!, p.ParameterType)).ToArray();

        parameters.ShouldBe(Fields);
        Fields.Length.ShouldBe(24);
    }

    [Fact]
    public void The_key_is_computed_and_is_not_a_positional_field()
    {
        var key = typeof(SimTrade).GetProperty(nameof(SimTrade.Key))!;

        key.CanWrite.ShouldBeFalse();
        Fields.Select(f => f.Name).ShouldNotContain(nameof(SimTrade.Key));
        key.GetMethod!.GetCustomAttributes(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false).ShouldBeEmpty("a computed property, not an auto-property");
    }

    [Fact]
    public void The_schema_version_is_1_and_the_enums_have_exactly_their_members()
    {
        SimTrade.CurrentSchemaVersion.ShouldBe(1);
        Enum.GetNames<ExitReason>().ShouldBe(["Stop", "Target", "SquareOff", "EndOfData"]);
        Enum.GetNames<FillBasis>().ShouldBe(["Exact"]);
        Enum.GetNames<CostBasis>().ShouldBe(["None"]);
        Enum.GetNames<Side>().ShouldBe(["Buy", "Sell"]);
        Enum.GetNames<NoTradeReason>().ShouldBe(["NoFillBar", "AfterSquareOff", "EntryThroughStop"]);
    }

    [Fact]
    public void Every_trade_the_walker_makes_says_exact_fills_no_costs_and_no_snapshot()
    {
        foreach (var walkCase in Cases.All)
        {
            foreach (var item in walkCase.Items)
            {
                if (WalkAssert.Walk(walkCase, item).Value.Trade is { } trade)
                {
                    trade.SchemaVersion.ShouldBe(SimTrade.CurrentSchemaVersion);
                    trade.Fills.ShouldBe(FillBasis.Exact);
                    trade.Costs.ShouldBe(CostBasis.None);
                    trade.EntrySnapshotJson.ShouldBeNull();
                }
            }
        }
    }

    [Fact]
    public void The_walker_counters_add_field_by_field_and_empty_is_the_identity()
    {
        var one = new WalkCounters(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13);

        var sum = one.Add(one);

        sum.ShouldBe(new WalkCounters(2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 22, 24, 26));
        one.Add(WalkCounters.Empty).ShouldBe(one);
        WalkCounters.Empty.ShouldBe(new WalkCounters(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
    }
}
