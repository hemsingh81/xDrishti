using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Tests.Support;

namespace XDrishti.Simulation.Tests;

/// <summary>
/// AC1 and criterion 16: the trade record is frozen. Changing any of the 32 fields, their order or their types, or a member of the enums, fails here until the schema version is raised and an ADR says why (schema 2 is ADR 0038, schema 3 is ADR 0040).
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
        ("RExact", typeof(double)),
        ("MaeR", typeof(double)),
        ("MfeR", typeof(double)),
        ("Fills", typeof(FillBasis)),
        ("Costs", typeof(CostBasis)),
        ("EntrySnapshotJson", typeof(string)),
        ("RFilled", typeof(double)),
        ("SlippageR", typeof(double)),
        ("EntryFilledPrice", typeof(double)),
        ("ExitFilledPrice", typeof(double)),
        ("EntryFill", typeof(EntryFill)),
        ("TickFallback", typeof(bool)),
        ("SessionsHeld", typeof(int)),
        ("SessionsUnseen", typeof(int)),
    ];

    [Fact]
    public void The_positional_properties_are_exactly_the_30_fields_of_schema_2_in_order_followed_by_the_two_of_schema_3()
    {
        var constructor = typeof(SimTrade).GetConstructors().Single();

        var parameters = constructor.GetParameters().Select(p => (p.Name!, p.ParameterType)).ToArray();

        parameters.ShouldBe(Fields);
        Fields.Length.ShouldBe(32);
        Fields.Take(30).Select(f => f.Name).Last().ShouldBe("TickFallback");
        Fields[30].Name.ShouldBe("SessionsHeld");
        Fields[31].Name.ShouldBe("SessionsUnseen");
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
    public void The_schema_version_is_3_and_the_enums_have_exactly_their_members()
    {
        SimTrade.CurrentSchemaVersion.ShouldBe(3);
        Enum.GetNames<ExitReason>().ShouldBe(["Stop", "Target", "SquareOff", "EndOfData", "MaxHold"]);
        Enum.GetNames<FillBasis>().ShouldBe(["Exact", "Modelled"]);
        Enum.GetNames<EntryFill>().ShouldBe(["AtOpen", "AtLimit"]);
        Enum.GetNames<CostBasis>().ShouldBe(["None"]);
        Enum.GetNames<Side>().ShouldBe(["Buy", "Sell"]);
        Enum.GetNames<NoTradeReason>().ShouldBe(["NoFillBar", "AfterSquareOff", "EntryThroughStop", "LimitNotReached", "PositionOpen"]);
    }

    [Fact]
    public void Every_trade_the_walker_makes_with_no_slippage_says_exact_fills_no_costs_and_no_snapshot()
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
        var one = new WalkCounters(1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27);

        var sum = one.Add(one);

        sum.ShouldBe(new WalkCounters(2, 4, 6, 8, 10, 12, 14, 16, 18, 20, 22, 24, 26, 28, 30, 32, 34, 36, 38, 40, 42, 44, 46, 48, 50, 52, 54));
        one.Add(WalkCounters.Empty).ShouldBe(one);
        WalkCounters.Empty.ShouldBe(new WalkCounters(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
    }

    [Fact]
    public void The_twenty_seven_counters_keep_their_positions_the_seven_of_schema_3_appended_with_default_zero()
    {
        var names = typeof(WalkCounters).GetConstructors().Single().GetParameters();

        names.Length.ShouldBe(27);
        names.Take(20).Last().Name.ShouldBe("RiskUnderFourTicks");
        names.Skip(20).Select(p => p.Name).ShouldBe(["PositionOpen", "ExitsMaxHold", "MaxHoldAtLastClose", "OvernightStopGaps", "OvernightTargetGaps", "SessionsHeld", "SessionsUnseen"]);
        names.Skip(20).ShouldAllBe(p => p.HasDefaultValue && Equals(p.DefaultValue, 0L));
    }

    [Fact]
    public void Counters_add_sums_every_property_of_the_record_and_fates_add_does_too_found_by_reflection()
    {
        // Criterion 27: a field appended later cannot be left out of Add, because each property gets its own distinct value and the sum must double it.
        var counterProperties = typeof(WalkCounters).GetProperties().Where(p => p.PropertyType == typeof(long) && p.GetMethod!.IsPublic && p.DeclaringType == typeof(WalkCounters) && p.Name != "Empty").ToList();
        counterProperties.Count.ShouldBe(27);
        var counters = (WalkCounters)ConstructWithDistinctValues(typeof(WalkCounters));
        var counterSum = counters.Add(counters);
        foreach (var property in counterProperties)
        {
            ((long)property.GetValue(counterSum)!).ShouldBe(2 * (long)property.GetValue(counters)!, property.Name);
        }

        var fateProperties = typeof(RunFates).GetProperties().Where(p => p.PropertyType == typeof(long) && p.DeclaringType == typeof(RunFates) && p.Name is not ("Total" or "NoTrade")).ToList();
        fateProperties.Count.ShouldBe(10);
        var fates = (RunFates)ConstructWithDistinctValues(typeof(RunFates));
        var fateSum = fates.Add(fates);
        foreach (var property in fateProperties)
        {
            ((long)property.GetValue(fateSum)!).ShouldBe(2 * (long)property.GetValue(fates)!, property.Name);
        }
    }

    [Fact]
    public void The_two_fates_of_schema_3_are_appended_and_the_total_counts_every_fate_once()
    {
        var parameters = typeof(RunFates).GetConstructors().Single().GetParameters();

        parameters.Select(p => p.Name).ShouldBe(["EntriesSuppressed", "TradesBeforeWindow", "OpenAtEnd", "Trades", "NoFillBar", "AfterSquareOff", "EntryThroughStop", "LimitNotReached", "ExcludedAcrossDiscontinuity", "PositionOpen"]);
        new RunFates(1, 2, 3, 4, 5, 6, 7, 8, 9, 10).Total.ShouldBe(55);
        new RunFates(1, 2, 3, 4, 5, 6, 7, 8, 9, 10).NoTrade.ShouldBe(5 + 6 + 7 + 8 + 10);
        RunFates.Empty.ShouldBe(new RunFates(0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
    }

    [Fact]
    public void A_flagged_day_note_counts_the_positions_excluded_at_it_and_defaults_to_none()
    {
        new DiscontinuityNote(new DateOnly(2026, 3, 3), 0.5).ExcludedPositions.ShouldBe(0);
        new DiscontinuityNote(new DateOnly(2026, 3, 3), 0.5, 2).ExcludedPositions.ShouldBe(2);
        typeof(DiscontinuityNote).GetConstructors().Single().GetParameters().Select(p => p.Name).ShouldBe(["Date", "Ratio", "ExcludedPositions"]);
    }

    private static object ConstructWithDistinctValues(Type type)
    {
        var parameters = type.GetConstructors().Single().GetParameters();
        var values = parameters.Select((_, i) => (object)(long)(i + 1)).ToArray();
        return Activator.CreateInstance(type, values)!;
    }
}
