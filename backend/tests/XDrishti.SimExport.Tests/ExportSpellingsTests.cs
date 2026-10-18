using XDrishti.Domain.Market.Actions;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.SimExport.Csv;
using XDrishti.SimExport.Json;
using XDrishti.SimExport.Tests.Support;

namespace XDrishti.SimExport.Tests;

/// <summary>Criterion 1 of P3-04b1: one spelling table for every enum of the record, used by both writers.</summary>
public sealed class ExportSpellingsTests
{
    private static readonly Type[] Named =
    [
        typeof(CorporateActionKind), typeof(Side), typeof(ExitReason), typeof(FillBasis), typeof(CostBasis), typeof(EntryFill), typeof(Timeframe), typeof(SkipReason), typeof(NoTradeReason), typeof(UniverseKind), typeof(SessionExclusion),
    ];

    private static string Spell(Type type, object member) =>
        (string)typeof(ExportSpellings).GetMethod(nameof(ExportSpellings.Of), [type])!.Invoke(null, [member])!;

    [Fact]
    public void Every_enum_of_the_record_graph_has_a_spelling()
    {
        var enums = RecordGraph.Enums(typeof(SimulationRun));

        enums.ShouldNotBeEmpty();
        enums.ShouldContain(typeof(CorporateActionKind), "the graph reaches the adjustments");
        enums.Except(ExportSpellings.Enums).ShouldBeEmpty("an enum of the record with no spelling would be written by default: add it to ExportSpellings");
    }

    [Fact]
    public void The_named_enums_of_the_spec_are_all_in_the_table()
    {
        ExportSpellings.Enums.ShouldBe(Named, ignoreOrder: true);
        RecordGraph.Enums(typeof(SimulationRun)).ShouldBeSubsetOf(Named);
    }

    [Fact]
    public void Every_member_of_every_enum_has_one_spelling_and_no_two_share_one()
    {
        foreach (var type in ExportSpellings.Enums)
        {
            var spellings = Enum.GetValues(type).Cast<object>().Select(member => Spell(type, member)).ToList();

            spellings.ShouldAllBe(s => !string.IsNullOrWhiteSpace(s), type.Name);
            spellings.Distinct(StringComparer.Ordinal).Count().ShouldBe(spellings.Count, type.Name + " has two members with one spelling");
        }
    }

    [Theory]
    [InlineData(Side.Buy, "BUY")]
    [InlineData(Side.Sell, "SELL")]
    public void The_side_is_upper_case(Side side, string expected) => ExportSpellings.Of(side).ShouldBe(expected);

    [Fact]
    public void The_spellings_of_the_other_enums_are_the_ones_of_the_format()
    {
        ExportSpellings.Of(ExitReason.Stop).ShouldBe("stop");
        ExportSpellings.Of(ExitReason.Target).ShouldBe("target");
        ExportSpellings.Of(ExitReason.SquareOff).ShouldBe("square_off");
        ExportSpellings.Of(ExitReason.EndOfData).ShouldBe("end_of_data");
        ExportSpellings.Of(FillBasis.Modelled).ShouldBe("modelled");
        ExportSpellings.Of(FillBasis.Exact).ShouldBe("no_slippage");
        ExportSpellings.Of(CostBasis.None).ShouldBe("none");
        ExportSpellings.Of(EntryFill.AtOpen).ShouldBe("at_open");
        ExportSpellings.Of(EntryFill.AtLimit).ShouldBe("at_limit");
        ExportSpellings.Of(Timeframe.M15).ShouldBe("15m");
        ExportSpellings.Of(Timeframe.D1).ShouldBe("1D");
        ExportSpellings.Of(SkipReason.TickSizeInvalid).ShouldBe("tick_size_invalid");
        ExportSpellings.Of(NoTradeReason.LimitNotReached).ShouldBe("limit_not_reached");
        ExportSpellings.Of(UniverseKind.InstrumentList).ShouldBe("instrument_list");
        ExportSpellings.Of(UniverseKind.Basket).ShouldBe("basket");
        ExportSpellings.Of(SessionExclusion.ShortSession).ShouldBe("short_session");
        ExportSpellings.Of(CorporateActionKind.Bonus).ShouldBe("bonus");
    }

    [Fact]
    public void A_member_with_no_spelling_throws_instead_of_being_written_as_a_default()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ExportSpellings.Of((Side)99));
        Should.Throw<ArgumentOutOfRangeException>(() => ExportSpellings.Of((ExitReason)99));
        Should.Throw<ArgumentOutOfRangeException>(() => ExportSpellings.Of((FillBasis)99));
        Should.Throw<ArgumentOutOfRangeException>(() => ExportSpellings.Of((CostBasis)99));
        Should.Throw<ArgumentOutOfRangeException>(() => ExportSpellings.Of((EntryFill)99));
        Should.Throw<ArgumentOutOfRangeException>(() => ExportSpellings.Of((Timeframe)99));
        Should.Throw<ArgumentOutOfRangeException>(() => ExportSpellings.Of((SkipReason)99));
        Should.Throw<ArgumentOutOfRangeException>(() => ExportSpellings.Of((NoTradeReason)99));
        Should.Throw<ArgumentOutOfRangeException>(() => ExportSpellings.Of((UniverseKind)99));
        Should.Throw<ArgumentOutOfRangeException>(() => ExportSpellings.Of((SessionExclusion)99));
        Should.Throw<ArgumentOutOfRangeException>(() => ExportSpellings.Of((CorporateActionKind)99));
    }

    [Fact]
    public void An_enum_that_is_not_in_the_table_has_no_speller()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => ExportSpellings.SpellerFor<DayOfWeek>());
    }

    [Fact]
    public void Both_writers_throw_for_a_member_with_no_spelling()
    {
        var run = FabricatedRun.Standard();
        var broken = run with { Trades = [.. run.Trades.Select(t => t with { Trade = t.Trade with { ExitReason = (ExitReason)99 } })] };

        Should.Throw<ArgumentOutOfRangeException>(() => Sinks.Render(s => TradesCsv.Write(s, broken.RunKey, broken.Trades)));
        Should.Throw<ArgumentOutOfRangeException>(() => Sinks.Render(s => SimExportJson.Write(s, broken, RunLabels.Of(broken, FillRules.Default), "xd-sim-export-1")));
    }

    [Fact]
    public void Both_writers_spell_a_trade_the_same_way()
    {
        var run = FabricatedRun.Standard();
        var csv = Rfc4180Reader.Read(Sinks.Render(s => TradesCsv.Write(s, run.RunKey, run.Trades)));
        var json = System.Text.Json.JsonDocument.Parse(Sinks.Bytes(s => SimExportJson.Write(s, run, RunLabels.Of(run, FillRules.Default), "xd-sim-export-1"))).RootElement.GetProperty("run").GetProperty("trades");
        var header = csv[0];

        for (var i = 0; i < run.Trades.Count; i++)
        {
            var trade = json[i].GetProperty("trade");
            csv[i + 1][header.IndexOf("side")].ShouldBe(trade.GetProperty("side").GetString());
            csv[i + 1][header.IndexOf("exit_reason")].ShouldBe(trade.GetProperty("exit_reason").GetString());
            csv[i + 1][header.IndexOf("fills")].ShouldBe(trade.GetProperty("fills").GetString());
            csv[i + 1][header.IndexOf("costs")].ShouldBe(trade.GetProperty("costs").GetString());
            csv[i + 1][header.IndexOf("entry_fill")].ShouldBe(trade.GetProperty("entry_fill").GetString());
            csv[i + 1][header.IndexOf("trigger_timeframe")].ShouldBe(trade.GetProperty("trigger_timeframe").GetString());
        }
    }

    [Fact]
    public void A_no_slippage_run_is_written_no_slippage()
    {
        var rules = FillRules.Create(0.1, SlippageConfig.None).Value;
        var trade = FabricatedRun.Trade(FabricatedRun.IdA, "NSE:AAA", Side.Buy, FabricatedRun.Monday, fills: FillBasis.Exact);
        var run = FabricatedRun.Assemble(FabricatedRun.List((FabricatedRun.IdA, "NSE:AAA")), [FabricatedRun.Ran(FabricatedRun.IdA, "NSE:AAA", FabricatedRun.Monday, FabricatedRun.Monday)], [trade], fills: rules.ToCanonicalJson());

        var csv = Rfc4180Reader.Read(Sinks.Render(s => TradesCsv.Write(s, run.RunKey, run.Trades)));

        csv[1][csv[0].IndexOf("fills")].ShouldBe("no_slippage");
    }
}
